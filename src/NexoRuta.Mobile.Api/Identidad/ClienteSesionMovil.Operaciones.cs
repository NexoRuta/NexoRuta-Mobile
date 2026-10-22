using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization.Metadata;
using NexoRuta.Mobile.Api.Reparto;
using NexoRuta.Mobile.Core.Jornadas;
namespace NexoRuta.Mobile.Api.Identidad;

public sealed partial class ClienteSesionMovil
{
    public async Task<ConfirmacionOperacion> EnviarOperacionAsync(CuentaMovil cuenta, Guid jornadaId, byte[] sobre, CancellationToken ct = default)
    {
        using var respuesta = await EnviarAutorizadoAsync(() => new(HttpMethod.Post, new Uri(api, $"api/mobile/jornadas/{jornadaId}/operaciones"))
        { Content = new ByteArrayContent(sobre) { Headers = { ContentType = new MediaTypeHeaderValue("application/json") } } }, ct, cuenta);
        return await LeerJsonAsync(respuesta, ContratosRepartoJson.Default.ConfirmacionOperacion, ct);
    }
    public async Task SubirEvidenciaAsync(CuentaMovil cuenta, Guid jornadaId, Guid envioId, Guid archivoId, string tipo, byte[] contenido, CancellationToken ct = default)
    {
        if (contenido.Length is 0 or > 2097152) throw new ArgumentException("La evidencia excede el tamaño admitido.");
        using var respuesta = await EnviarAutorizadoAsync(() => new(HttpMethod.Put, new Uri(api, $"api/mobile/jornadas/{jornadaId}/evidencias/{archivoId}"))
        { Content = JsonContent.Create(new EvidenciaRequest { EnvioId = envioId, Tipo = tipo, Contenido = contenido }, ContratosRepartoJson.Default.EvidenciaRequest) }, ct, cuenta);
    }
    private async Task<HttpResponseMessage> EnviarAutorizadoAsync(Func<HttpRequestMessage> crear, CancellationToken ct,
        NexoRuta.Mobile.Core.Jornadas.CuentaMovil? cuentaEsperada = null)
    {
        await exclusividad.WaitAsync(ct);
        try
        {
            var estado = await LeerAsync() ?? throw new SesionMovilException(ErrorSesionMovil.ReingresoNecesario);
            if (cuentaEsperada is not null && estado.Cuenta != cuentaEsperada)
                throw new SesionMovilException(ErrorSesionMovil.CuentaDiferente);
            estado = await AsegurarTokenAsync(estado, false, ct);
            for (var intento = 0; intento < 2; intento++)
            {
                using var request = crear(); request.Headers.Authorization = new("Bearer", estado.Tokens!.AccessToken);
                var respuesta = await http.SendAsync(request, ct);
                if (respuesta.StatusCode == HttpStatusCode.Unauthorized && intento == 0)
                { respuesta.Dispose(); estado = await AsegurarTokenAsync(estado, true, ct); continue; }
                try
                {
                    if (respuesta.StatusCode == HttpStatusCode.Unauthorized) await InvalidarAsync(estado);
                    ExigirExito(respuesta); return respuesta;
                }
                catch { respuesta.Dispose(); throw; }
            }
            throw new InvalidOperationException("No se recibió respuesta de la operación.");
        }
        finally { exclusividad.Release(); }
    }
}
