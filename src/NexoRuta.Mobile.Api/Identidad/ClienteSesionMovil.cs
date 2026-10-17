using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using NexoRuta.Mobile.Core.Jornadas;

namespace NexoRuta.Mobile.Api.Identidad;

// Una única instancia por proceso: serializa ingreso, renovación y llamadas autenticadas.
public sealed partial class ClienteSesionMovil : IDisposable
{
    private readonly HttpClient http;
    private readonly Uri api;
    private readonly IAlmacenSesionMovil almacen;
    private readonly TimeProvider reloj;
    private readonly SemaphoreSlim exclusividad = new(1, 1);

    public ClienteSesionMovil(Uri api, IAlmacenSesionMovil almacen,
        HttpMessageHandler? transporte = null, TimeProvider? reloj = null)
    {
        if (!api.IsAbsoluteUri || api.Scheme != Uri.UriSchemeHttps || api.UserInfo.Length != 0
            || api.Query.Length != 0 || api.Fragment.Length != 0 || api.AbsolutePath != "/")
            throw new ArgumentException("La API requiere un origen HTTPS sin credenciales ni ruta.", nameof(api));
        this.api = api;
        this.almacen = almacen;
        this.reloj = reloj ?? TimeProvider.System;
        http = new HttpClient(transporte ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        {
            Timeout = TimeSpan.FromSeconds(30),
            MaxResponseContentBufferSize = 4 * 1024 * 1024
        };
    }

    // Solo metadatos: no acredita desbloqueo local, jornada offline ni autorización operativa.
    public async Task<CuentaMovil?> LeerCuentaGuardadaAsync(CancellationToken ct = default)
    {
        await exclusividad.WaitAsync(ct);
        try { return (await LeerAsync())?.Cuenta; }
        finally { exclusividad.Release(); }
    }

    public async Task<CuentaMovil> IngresarAsync(string email, string password, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        await exclusividad.WaitAsync(ct);
        try
        {
            var anterior = await LeerAsync();
            using var respuesta = await PostAsync("api/auth/mobile/login",
                new IngresoRequest { Email = email.Trim(), Password = password },
                ContratosSesionJson.Default.IngresoRequest, ct);
            ExigirExito(respuesta, ErrorSesionMovil.CredencialesRechazadas);
            var tokens = await LeerTokensAsync(respuesta, ct);
            CuentaMovil cuenta;
            try
            {
                using var contexto = await GetAsync("api/usuarios/actual", tokens, ct);
                ExigirExito(contexto);
                cuenta = (await LeerJsonAsync(contexto, ContratosSesionJson.Default.UsuarioActualResponse, ct)).ObtenerCuenta();
                if (anterior is not null && anterior.Cuenta != cuenta)
                    throw new SesionMovilException(ErrorSesionMovil.CuentaDiferente);
                await almacen.GuardarAsync(new() { Api = api.AbsoluteUri, Cuenta = cuenta, Tokens = tokens });
            }
            catch
            {
                await RevocarNuevaSesionAsync(tokens);
                throw;
            }
            return cuenta;
        }
        finally { exclusividad.Release(); }
    }

    public async Task<IReadOnlyList<string>> ObtenerPermisosAsync(CancellationToken ct = default)
    {
        await exclusividad.WaitAsync(ct);
        try
        {
            var estado = await LeerAsync() ?? throw new SesionMovilException(ErrorSesionMovil.ReingresoNecesario);
            estado = await AsegurarTokenAsync(estado, false, ct);
            using var primera = await GetAsync("api/usuarios/actual/permisos", estado.Tokens!, ct);
            if (primera.StatusCode != HttpStatusCode.Unauthorized)
            {
                ExigirExito(primera);
                return await LeerJsonAsync(primera, ContratosSesionJson.Default.StringArray, ct);
            }
            estado = await AsegurarTokenAsync(estado, true, ct);
            using var segunda = await GetAsync("api/usuarios/actual/permisos", estado.Tokens!, ct);
            if (segunda.StatusCode == HttpStatusCode.Unauthorized)
                await InvalidarAsync(estado);
            ExigirExito(segunda);
            return await LeerJsonAsync(segunda, ContratosSesionJson.Default.StringArray, ct);
        }
        finally { exclusividad.Release(); }
    }

    private async Task<EstadoSesionMovil?> LeerAsync()
    {
        var estado = await almacen.LeerAsync();
        if (estado is not null)
        {
            SerializadorSesionMovil.Validar(estado);
            if (estado.Api != api.AbsoluteUri)
                throw new SesionMovilException(ErrorSesionMovil.EstadoLocalInvalido);
        }
        return estado;
    }

    private async Task<EstadoSesionMovil> AsegurarTokenAsync(EstadoSesionMovil estado, bool forzar, CancellationToken ct)
    {
        var tokens = estado.Tokens ?? throw new SesionMovilException(ErrorSesionMovil.ReingresoNecesario);
        if (tokens.RefreshExpiresAtUtc <= reloj.GetUtcNow())
        {
            await InvalidarAsync(estado);
            throw new SesionMovilException(ErrorSesionMovil.ReingresoNecesario);
        }
        if (!forzar && tokens.ExpiresAtUtc > reloj.GetUtcNow().AddSeconds(30)) return estado;
        ct.ThrowIfCancellationRequested();
        // Antes de enviar: un cierre o respuesta perdida nunca permite reutilizar el refresh anterior.
        await InvalidarAsync(estado);
        try
        {
            using var respuesta = await PostAsync("api/auth/mobile/refresh",
                new RenovacionRequest { RefreshToken = tokens.RefreshToken }, ContratosSesionJson.Default.RenovacionRequest, ct);
            ExigirExito(respuesta);
            var nuevos = await LeerTokensAsync(respuesta, ct);
            if (nuevos.SesionId != tokens.SesionId || nuevos.RefreshExpiresAtUtc != tokens.RefreshExpiresAtUtc
                || nuevos.RefreshToken == tokens.RefreshToken)
                throw new SesionMovilException(ErrorSesionMovil.RespuestaInvalida);
            var renovado = new EstadoSesionMovil { Api = estado.Api, Cuenta = estado.Cuenta, Tokens = nuevos };
            await almacen.GuardarAsync(renovado);
            return renovado;
        }
        catch (HttpRequestException)
        {
            throw new SesionMovilException(ErrorSesionMovil.ReingresoNecesario);
        }
    }

    private Task InvalidarAsync(EstadoSesionMovil estado) =>
        almacen.GuardarAsync(new() { Api = estado.Api, Cuenta = estado.Cuenta });

    private Task<HttpResponseMessage> PostAsync<T>(string ruta, T datos, JsonTypeInfo<T> tipo, CancellationToken ct) =>
        http.PostAsJsonAsync(new Uri(api, ruta), datos, tipo, ct);

    private async Task<HttpResponseMessage> GetAsync(string ruta, TokensSesionMovil tokens, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(api, ruta));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return await http.SendAsync(request, ct);
    }

    private async Task<TokensSesionMovil> LeerTokensAsync(HttpResponseMessage respuesta, CancellationToken ct)
    {
        var tokens = await LeerJsonAsync(respuesta, ContratosSesionJson.Default.TokensSesionMovil, ct);
        if (!tokens.EsValido || tokens.ExpiresAtUtc <= reloj.GetUtcNow() || tokens.RefreshExpiresAtUtc <= reloj.GetUtcNow())
            throw new SesionMovilException(ErrorSesionMovil.RespuestaInvalida);
        return tokens;
    }

    private static async Task<T> LeerJsonAsync<T>(HttpResponseMessage respuesta, JsonTypeInfo<T> tipo, CancellationToken ct)
    {
        try
        {
            return await respuesta.Content.ReadFromJsonAsync(tipo, ct)
                ?? throw new SesionMovilException(ErrorSesionMovil.RespuestaInvalida);
        }
        catch (JsonException) { throw new SesionMovilException(ErrorSesionMovil.RespuestaInvalida); }
    }

    private static void ExigirExito(HttpResponseMessage respuesta,
        ErrorSesionMovil rechazo = ErrorSesionMovil.ReingresoNecesario)
    {
        if (respuesta.IsSuccessStatusCode) return;
        throw new SesionMovilException(respuesta.StatusCode switch
        {
            HttpStatusCode.Unauthorized => rechazo,
            HttpStatusCode.Forbidden => ErrorSesionMovil.AccesoDenegado,
            HttpStatusCode.TooManyRequests => ErrorSesionMovil.DemasiadasSolicitudes,
            >= HttpStatusCode.InternalServerError => ErrorSesionMovil.ServicioNoDisponible,
            _ => ErrorSesionMovil.RespuestaInvalida
        });
    }

    private async Task RevocarNuevaSesionAsync(TokensSesionMovil tokens)
    {
        // Limpieza acotada; un fallo no debe ocultar el rechazo de la cuenta ni cambiar datos locales.
        using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            using var respuesta = await PostAsync("api/auth/mobile/logout",
                new RenovacionRequest { RefreshToken = tokens.RefreshToken }, ContratosSesionJson.Default.RenovacionRequest, limite.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException) { }
    }

    public void Dispose()
    {
        http.Dispose();
        exclusividad.Dispose();
    }
}
