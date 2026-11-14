using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using NexoRuta.Mobile.Api.Reparto;
using NexoRuta.Mobile.Core.Jornadas;
namespace NexoRuta.Mobile.Api.Identidad;

public sealed partial class ClienteSesionMovil
{
    public async Task<ConfirmacionOperacion> EnviarOperacionAsync(
        CuentaMovil cuenta, Guid jornadaId, byte[] sobre, Guid operacionId, string traceParent, int pendientes, CancellationToken ct = default)
    {
        if (operacionId == Guid.Empty || pendientes is < 0 or > 10_000
            || !ActivityContext.TryParse(traceParent, traceState: null, out _))
            throw new ArgumentOutOfRangeException(nameof(pendientes));
        using var activity = TelemetryMovil.IniciarIntento(traceParent, "mobile.sync.operation.send");
        activity?.SetTag("nexoruta.operation.id", operacionId);
        activity?.SetTag("nexoruta.sync.pending", pendientes);
        using var scope = logger?.BeginScope(new Dictionary<string, object?>
        {
            ["correlation_id"] = operacionId.ToString("D"),
            ["mobile_operation_id"] = operacionId.ToString("D"),
            ["journey_id"] = jornadaId.ToString("D")
        });
        logger?.LogInformation("Mobile operation sync attempt started. PendingOperations: {PendingOperations}, TraceId: {TraceId}",
            pendientes, ActivityContext.TryParse(traceParent, null, out var parent) ? parent.TraceId.ToHexString() : null);
        try
        {
            using var respuesta = await EnviarAutorizadoAsync(() =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, new Uri(api, $"api/mobile/jornadas/{jornadaId}/operaciones"))
                {
                    Content = new ByteArrayContent(sobre) { Headers = { ContentType = new MediaTypeHeaderValue("application/json") } }
                };
                request.Headers.TryAddWithoutValidation("X-NexoRuta-Pending-Sync", pendientes.ToString(CultureInfo.InvariantCulture));
                return request;
            }, ct, cuenta);
            var resultado = await LeerJsonAsync(respuesta, ContratosRepartoJson.Default.ConfirmacionOperacion, ct);
            activity?.SetStatus(ActivityStatusCode.Ok);
            logger?.LogInformation("Mobile operation sync attempt finished. Result: {Result}", resultado.Resultado);
            return resultado;
        }
        catch (Exception error)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Mobile operation sync failed");
            logger?.LogWarning("Mobile operation sync attempt failed. ErrorType: {ErrorType}", error.GetType().Name);
            throw;
        }
    }
    public async Task SubirEvidenciaAsync(CuentaMovil cuenta, Guid jornadaId, Guid envioId, Guid archivoId,
        Guid operacionId, string traceParent, string tipo, byte[] contenido, CancellationToken ct = default)
    {
        if (contenido.Length is 0 or > 2097152) throw new ArgumentException("La evidencia excede el tamaño admitido.");
        using var activity = TelemetryMovil.IniciarIntento(traceParent, "mobile.sync.evidence.upload");
        activity?.SetTag("nexoruta.operation.id", operacionId);
        activity?.SetTag("nexoruta.evidence.id", archivoId);
        using var scope = logger?.BeginScope(new Dictionary<string, object?>
        {
            ["correlation_id"] = operacionId.ToString("D"),
            ["mobile_operation_id"] = operacionId.ToString("D")
        });
        using var respuesta = await EnviarAutorizadoAsync(() => new(HttpMethod.Put, new Uri(api, $"api/mobile/jornadas/{jornadaId}/evidencias/{archivoId}"))
        { Content = JsonContent.Create(new EvidenciaRequest { EnvioId = envioId, Tipo = tipo, Contenido = contenido }, ContratosRepartoJson.Default.EvidenciaRequest) }, ct, cuenta);
        activity?.SetStatus(ActivityStatusCode.Ok);
    }

    public async Task ReportarEstadoSincronizacionAsync(CuentaMovil cuenta, EstadoSincronizacionMovilRequest estado, CancellationToken ct = default)
    {
        if (estado.InstallationId == Guid.Empty || estado.Sequence <= 0 || estado.PendingOperations is < 0 or > 10_000)
            throw new ArgumentOutOfRangeException(nameof(estado));
        using var activity = TelemetryMovil.IniciarEstadoSync();
        activity?.SetTag("nexoruta.sync.pending", estado.PendingOperations);
        using var scope = logger?.BeginScope(new Dictionary<string, object?>
        {
            ["mobile_installation_id"] = estado.InstallationId.ToString("D"),
            ["sync_report_sequence"] = estado.Sequence
        });
        using var respuesta = await EnviarAutorizadoAsync(() => new(HttpMethod.Post, new Uri(api, "api/mobile/sync-status"))
        {
            Content = JsonContent.Create(estado, ContratosRepartoJson.Default.EstadoSincronizacionMovilRequest)
        }, ct, cuenta);
        activity?.SetStatus(ActivityStatusCode.Ok);
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
                using var requestActivity = TelemetryMovil.IniciarSolicitudHttp();
                requestActivity?.SetTag("http.request.method", request.Method.Method);
                if (Activity.Current?.Id is { } traceParent)
                    request.Headers.TryAddWithoutValidation("traceparent", traceParent);
                HttpResponseMessage respuesta;
                try
                {
                    respuesta = await http.SendAsync(request, ct);
                    requestActivity?.SetTag("http.response.status_code", (int)respuesta.StatusCode);
                    requestActivity?.SetStatus(respuesta.IsSuccessStatusCode ? ActivityStatusCode.Ok : ActivityStatusCode.Error,
                        respuesta.IsSuccessStatusCode ? null : "Mobile API request failed");
                }
                catch (Exception error)
                {
                    requestActivity?.SetStatus(ActivityStatusCode.Error, "Mobile API transport failed");
                    requestActivity?.SetTag("error.type", error.GetType().Name);
                    throw;
                }
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
