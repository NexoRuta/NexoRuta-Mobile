using System.Text.Json;
using NexoRuta.Mobile.Api.Identidad;
using NexoRuta.Mobile.Api.Reparto;
using NexoRuta.Mobile.Core.Jornadas;
using NexoRuta.Mobile.Storage;
namespace NexoRuta.Mobile.Application.Ubicacion;
public sealed class RegistroUbicacion(IAlmacenSesionMovil sesion, RepositorioJornadasSqlite datos, SincronizadorJornada? sync = null) : IDisposable
{
    private readonly SemaphoreSlim exclusividad = new(1, 1);
    public async Task<bool> TieneActivaAsync(CancellationToken ct = default) => await BuscarActiva(ct) is not null;
    private async Task<(CuentaMovil Cuenta, JornadaLocal Jornada)?> BuscarActiva(CancellationToken ct)
    {
        var estado = await sesion.LeerAsync(); if (estado is null) return null;
        var activas = new List<JornadaLocal>();
        foreach (var j in await datos.LeerJornadasAsync(estado.Cuenta, ct, soloAbiertas: true))
        {
            if (!j.ExportarEstado().Confirmadas.Contains(j.Operaciones[0].Id)) continue;
            var bytes = await datos.LeerConfirmacionAsync(estado.Cuenta, j.Id, j.Operaciones[0].Id, ct);
            if (bytes is not null && JsonSerializer.Deserialize(bytes, ContratosRepartoJson.Default.ConfirmacionOperacion)?.Resultado == "Aplicada") activas.Add(j);
        }
        return activas.Count == 1 ? (estado.Cuenta, activas[0]) : null;
    }
    public async Task<bool> RegistrarAsync(double latitud, double longitud, double precision, DateTimeOffset fecha, CancellationToken ct)
    {
        await exclusividad.WaitAsync(ct);
        try
        {
            var contexto = await BuscarActiva(ct); if (contexto is null) return false;
            var estado = contexto.Value; var jornada = estado.Jornada;
            if (!double.IsFinite(latitud) || !double.IsFinite(longitud) || !double.IsFinite(precision)
                || latitud is < -90 or > 90 || longitud is < -180 or > 180 || precision <= 0 || precision > 100000 || fecha == default) return true;
            var agregada = false;
            var actual = await datos.ModificarAsync(estado.Cuenta, jornada.Id, j =>
            {
                // Un callback tardío no reabre la jornada ni convierte una posición anterior al inicio en una nueva.
                if (j.EstaCerrada || fecha < j.Operaciones[0].OcurridaEn) return;
                var anterior = j.Operaciones.LastOrDefault(x => x.Tipo == TipoOperacionLocal.Posicion);
                if (anterior is not null && anterior.Datos.TryGetProperty("posicionEnUtc", out var valor)
                    && valor.TryGetDateTimeOffset(out var ultima) && fecha - ultima < TimeSpan.FromSeconds(30)) return;
                j.RegistrarPosicion(JsonSerializer.SerializeToElement(new DatosOperacionMovil
                { Latitud = latitud, Longitud = longitud, PrecisionMetros = precision, PosicionEnUtc = fecha.ToUniversalTime() },
                    DatosOperacionJson.Default.DatosOperacionMovil), DateTimeOffset.UtcNow);
                agregada = true;
            }, ct);
            if (actual.EstaCerrada) return false;
            if (agregada && sync is not null)
            {
                try { await sync.SincronizarAsync(estado.Cuenta, jornada.Id, ct); }
                catch (Exception ex) when (ex is HttpRequestException or SesionMovilException or OperationCanceledException) { }
            }
            return true;
        }
        finally { exclusividad.Release(); }
    }
    public void Dispose() => exclusividad.Dispose();
}
