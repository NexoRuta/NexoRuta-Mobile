using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Serialization.Metadata;
using NexoRuta.Mobile.Core.Jornadas;
using NexoRuta.Mobile.Core.Rutas;
namespace NexoRuta.Mobile.Api.Identidad;

public sealed partial class ClienteSesionMovil
{
    public async Task<CuentaMovil?> ObtenerCuentaLocalAsync(CancellationToken ct = default)
    {
        await exclusividad.WaitAsync(ct);
        try { return (await LeerAsync())?.Cuenta; }
        finally { exclusividad.Release(); }
    }
    public async Task<bool> TieneTokensGuardadosAsync(CancellationToken ct = default)
    {
        await exclusividad.WaitAsync(ct);
        try { return (await LeerAsync())?.Tokens is not null; }
        finally { exclusividad.Release(); }
    }
    public async Task<HojaRuta[]> ObtenerHojasAsync(DateOnly fecha, CancellationToken ct = default)
    {
        RegistrarDiagnostico($"api.mobile_routes.request.date={fecha:yyyy-MM-dd}");
        var hojas = await ConsultarAsync($"api/mobile/rutas?fecha={fecha:yyyy-MM-dd}", HojaRutaJson.Default.HojaRutaArray, ct);
        var cuenta = await ObtenerCuentaLocalAsync(ct) ?? throw new SesionMovilException(ErrorSesionMovil.ReingresoNecesario);
        foreach (var hoja in hojas) hoja.Validar(cuenta);
        return hojas;
    }

    public async Task<(HojaRuta[] Hojas, DateTimeOffset? FechaServidorUtc)> ObtenerHojasConFechaServidorAsync(
        DateOnly fecha, CancellationToken ct = default)
    {
        RegistrarDiagnostico($"api.mobile_routes.request.date={fecha:yyyy-MM-dd}");
        DateTimeOffset? fechaServidor = null;
        var hojas = await ConsultarAsync($"api/mobile/rutas?fecha={fecha:yyyy-MM-dd}",
            HojaRutaJson.Default.HojaRutaArray, ct,
            capturarFechaServidor: fechaUtc => fechaServidor = fechaUtc);
        var cuenta = await ObtenerCuentaLocalAsync(ct) ?? throw new SesionMovilException(ErrorSesionMovil.ReingresoNecesario);
        foreach (var hoja in hojas) hoja.Validar(cuenta);
        return (hojas, fechaServidor);
    }
    public async Task ExigirPerfilAsync(CancellationToken ct = default)
    {
        var perfil = await ConsultarAsync("api/mobile/perfil", ContratosSesionJson.Default.PerfilRepartidorResponse, ct);
        var cuenta = await ObtenerCuentaLocalAsync(ct);
        if (cuenta is null) throw new SesionMovilException(ErrorSesionMovil.AccesoDenegado);
        ValidarPerfil(perfil, cuenta);
    }

    private async Task ValidarPerfilNuevoAsync(CuentaMovil cuenta, TokensSesionMovil tokens, CancellationToken ct)
    {
        using var respuesta = await GetAsync("api/mobile/perfil", tokens, ct);
        ExigirExito(respuesta);
        ValidarPerfil(await LeerJsonAsync(respuesta, ContratosSesionJson.Default.PerfilRepartidorResponse, ct), cuenta);
    }

    private static void ValidarPerfil(PerfilRepartidorResponse perfil, CuentaMovil cuenta)
    {
        if (!perfil.Activo || perfil.AccesoId != cuenta.AccesoId || perfil.UsuarioId != cuenta.UsuarioId || perfil.OperadorId != cuenta.OperadorId)
            throw new SesionMovilException(ErrorSesionMovil.AccesoDenegado);
    }

    private async Task<T> ConsultarAsync<T>(string ruta, JsonTypeInfo<T> tipo, CancellationToken ct,
        CuentaMovil? cuentaEsperada = null, Action<DateTimeOffset?>? capturarFechaServidor = null)
    {
        await exclusividad.WaitAsync(ct);
        try
        {
            var estado = await LeerAsync() ?? throw new SesionMovilException(ErrorSesionMovil.ReingresoNecesario);
            if (cuentaEsperada is not null && estado.Cuenta != cuentaEsperada)
                throw new SesionMovilException(ErrorSesionMovil.CuentaDiferente);
            estado = await AsegurarTokenAsync(estado, false, ct);
            using var primera = await GetAsync(ruta, estado.Tokens!, ct);
            if (primera.StatusCode != HttpStatusCode.Unauthorized)
            {
                ExigirExito(primera); capturarFechaServidor?.Invoke(primera.Headers.Date);
                return await LeerJsonAsync(primera, tipo, ct);
            }
            estado = await AsegurarTokenAsync(estado, true, ct);
            using var segunda = await GetAsync(ruta, estado.Tokens!, ct);
            if (segunda.StatusCode == HttpStatusCode.Unauthorized) await InvalidarAsync(estado);
            ExigirExito(segunda); capturarFechaServidor?.Invoke(segunda.Headers.Date);
            return await LeerJsonAsync(segunda, tipo, ct);
        }
        finally { exclusividad.Release(); }
    }
}
