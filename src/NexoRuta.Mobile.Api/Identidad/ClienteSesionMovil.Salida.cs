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
        await exclusividad.WaitAsync(ct);
        try
        {
            var estado = await LeerAsync() ?? throw new SesionMovilException(ErrorSesionMovil.ReingresoNecesario);
            if (estado.Cuenta != cuenta) throw new SesionMovilException(ErrorSesionMovil.CuentaDiferente);
            if (estado.Tokens is null) throw new SesionMovilException(ErrorSesionMovil.ReingresoNecesario);
            if (instalacionId is { } id)
            {
                estado = await AsegurarTokenAsync(estado, false, ct);
                for (var intento = 0; intento < 2; intento++)
                {
                    using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri(api, $"api/mobile/instalaciones/{id}"));
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", estado.Tokens!.AccessToken);
                    using var respuesta = await http.SendAsync(request, ct);
                    if (respuesta.StatusCode == HttpStatusCode.Unauthorized && intento == 0)
                    { estado = await AsegurarTokenAsync(estado, true, ct); continue; }
                    if (respuesta.StatusCode != HttpStatusCode.NotFound) ExigirSinContenido(respuesta);
                    // 404 significa que no existe una instalación de esta cuenta; no autoriza tocar otra.
                    break;
                }
            }
            using var salida = await PostAsync("api/auth/mobile/logout", new RenovacionRequest
                { RefreshToken = estado.Tokens!.RefreshToken }, ContratosSesionJson.Default.RenovacionRequest, ct);
            ExigirSinContenido(salida);
            await almacen.EliminarAsync(api.AbsoluteUri);
        }
        finally { exclusividad.Release(); }
    }
    private static void ExigirSinContenido(HttpResponseMessage respuesta)
    {
        ExigirExito(respuesta);
        if (respuesta.StatusCode != HttpStatusCode.NoContent)
            throw new SesionMovilException(ErrorSesionMovil.RespuestaInvalida);
    }
}
