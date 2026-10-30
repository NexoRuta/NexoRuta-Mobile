using System.Net;
using System.Net.Http.Headers;
using NexoRuta.Mobile.Core.Jornadas;
namespace NexoRuta.Mobile.Api.Identidad;

public sealed partial class ClienteSesionMovil
{
    // Application debe suspender el almacén sin pendientes antes de invocar esta operación.
    public async Task CerrarSesionAsync(CuentaMovil cuenta, Guid? instalacionId, CancellationToken ct = default)
    {
        if (instalacionId == Guid.Empty) throw new ArgumentException("La instalación no es válida.");
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(TimeSpan.FromSeconds(12));
        var cancelacion = limite.Token;
        var adquirida = false;
        try
        {
            await exclusividad.WaitAsync(cancelacion);
            adquirida = true;
            var estado = await LeerAsync() ?? throw new SesionMovilException(ErrorSesionMovil.ReingresoNecesario);
            if (estado.Cuenta != cuenta) throw new SesionMovilException(ErrorSesionMovil.CuentaDiferente);
            if (estado.Tokens is null)
            {
                RegistrarDiagnostico("logout.reauth reason=no_local_tokens");
                throw new SesionMovilException(ErrorSesionMovil.ReautenticarParaCerrarSesion);
            }
            if (instalacionId is { } id)
            {
                estado = await RenovarParaCerrarAsync(estado, false, cancelacion);
                for (var intento = 0; intento < 2; intento++)
                {
                    using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri(api, $"api/mobile/instalaciones/{id}"));
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", estado.Tokens!.AccessToken);
                    using var respuesta = await http.SendAsync(request, cancelacion);
                    if (respuesta.StatusCode == HttpStatusCode.Unauthorized && intento == 0)
                    { estado = await RenovarParaCerrarAsync(estado, true, cancelacion); continue; }
                    if (respuesta.StatusCode != HttpStatusCode.NotFound) ExigirSinContenido(respuesta);
                    // 404 significa que no existe una instalación de esta cuenta; no autoriza tocar otra.
                    break;
                }
            }
            using var salida = await PostAsync("api/auth/mobile/logout", new RenovacionRequest
                { RefreshToken = estado.Tokens!.RefreshToken }, ContratosSesionJson.Default.RenovacionRequest, cancelacion);
            RegistrarDiagnostico($"logout.response.status={(int)salida.StatusCode}");
            ExigirSinContenido(salida);
            await almacen.EliminarAsync(api.AbsoluteUri);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && limite.IsCancellationRequested)
        {
            RegistrarDiagnostico("logout.result=timeout_unconfirmed");
            throw new SesionMovilException(ErrorSesionMovil.CierreNoConfirmado);
        }
        finally { if (adquirida) exclusividad.Release(); }
    }

    private async Task<EstadoSesionMovil> RenovarParaCerrarAsync(EstadoSesionMovil estado, bool forzar, CancellationToken ct)
    {
        try { return await AsegurarTokenAsync(estado, forzar, ct); }
        catch (SesionMovilException ex) when (ex.Error == ErrorSesionMovil.ReingresoNecesario)
        { throw new SesionMovilException(ErrorSesionMovil.ReautenticarParaCerrarSesion); }
    }
    private static void ExigirSinContenido(HttpResponseMessage respuesta)
    {
        ExigirExito(respuesta);
        if (respuesta.StatusCode != HttpStatusCode.NoContent)
            throw new SesionMovilException(ErrorSesionMovil.RespuestaInvalida);
    }
}
