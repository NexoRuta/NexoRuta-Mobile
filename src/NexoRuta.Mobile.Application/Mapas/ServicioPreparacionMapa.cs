using System.Text.Json;
using System.Text.Json.Serialization;
using NexoRuta.Mobile.Core.Jornadas;
using NexoRuta.Mobile.Core.Mapas;
using NexoRuta.Mobile.Core.Rutas;
using NexoRuta.Mobile.Storage;

namespace NexoRuta.Mobile.Application.Mapas;

public sealed class ServicioPreparacionMapa(RepositorioJornadasSqlite datos, IProveedorMapaOffline proveedor) : IDisposable
{
    private readonly SemaphoreSlim acceso = new(1, 1);
    public bool Disponible => proveedor.Disponible;
    public async Task<PreparacionMapa> ConsultarAsync(CuentaMovil cuenta, HojaRuta hoja, CancellationToken ct = default)
    {
        await acceso.WaitAsync(ct);
        try
        {
            if (!Disponible) return new(false, 0, "Mapa no configurado. La dirección está disponible; podés operar online.");
            var estado = await Recuperar(cuenta, hoja.Id, ct);
            if (estado.Vigente is not { } mapa) return new(false, 0, "Prepará tu recorrido antes de salir del depósito.");
            var zona = ZonaMapa.Para(hoja, proveedor.Proveedor, proveedor.Estilo);
            if (mapa.Huella != zona.Huella) return new(false, mapa.Bytes, "Las paradas o el mapa cambiaron. Prepará la zona actual; la descarga anterior se conserva.");
            var recursos = await proveedor.ConsultarAsync(mapa.RegionId, ct);
            return recursos.Completo && recursos.Bytes > 0
                ? new(true, recursos.Bytes, "Recorrido preparado.")
                : new(false, 0, "Prepará nuevamente tu recorrido antes de salir.");
        }
        finally { acceso.Release(); }
    }
    public async Task<PreparacionMapa> DescargarAsync(CuentaMovil cuenta, HojaRuta hoja,
        IProgress<ProgresoMapa> progreso, CancellationToken ct = default)
    {
        await acceso.WaitAsync(ct);
        string? intento = null; var publicada = false;
        try
        {
            if (!Disponible) throw new InvalidOperationException("El mapa offline aún no está disponible en este equipo.");
            var zona = ZonaMapa.Para(hoja, proveedor.Proveedor, proveedor.Estilo);
            var anterior = await Recuperar(cuenta, hoja.Id, ct);
            if (anterior.Intento is not null || anterior.Retirada is not null)
                throw new IOException("No se pudo limpiar la descarga anterior. Reintentá antes de descargar otra zona.");
            intento = $"nexoruta-{Guid.NewGuid():N}";
            await Guardar(cuenta, hoja.Id, anterior with { Intento = intento }, ct);
            await proveedor.DescargarAsync(intento, zona, progreso, ct);
            var recursos = await proveedor.ConsultarAsync(intento, ct);
            if (!recursos.Completo || recursos.Bytes <= 0)
                throw new IOException("El estilo y las teselas no están completos. La descarga anterior se conserva.");
            ct.ThrowIfCancellationRequested();
            var vigente = new MapaGuardado(intento, zona.Huella, recursos.Bytes, DateTimeOffset.UtcNow);
            // Una vez iniciada la publicación local, terminar el commit antes de atender una cancelación.
            await Guardar(cuenta, hoja.Id, new(vigente, null, anterior.Vigente?.RegionId), CancellationToken.None);
            publicada = true;
            if (anterior.Vigente is { } previo)
            {
                try
                {
                    await proveedor.EliminarAsync(previo.RegionId, CancellationToken.None);
                    await Guardar(cuenta, hoja.Id, new(vigente, null, null), CancellationToken.None);
                }
                catch { /* La región retirada queda registrada para repetir su limpieza tras el reinicio. */ }
            }
            return new(true, recursos.Bytes, "Recorrido preparado.");
        }
        catch
        {
            if (!publicada && intento is not null)
            {
                try { await proveedor.EliminarAsync(intento, CancellationToken.None); }
                catch { /* El intento queda registrado para recuperarlo al volver a abrir. */ }
            }
            throw;
        }
        finally { acceso.Release(); }
    }
    private async Task<EstadoMapaGuardado> Recuperar(CuentaMovil cuenta, Guid hoja, CancellationToken ct)
    {
        var bytes = await datos.LeerMetadatoAsync(cuenta, Clave(hoja), ct);
        var estado = bytes is null ? new(null, null, null) : JsonSerializer.Deserialize(bytes, MapasLocalesJson.Default.EstadoMapaGuardado)
            ?? throw new InvalidDataException("La preparación local del mapa está incompleta.");
        if (estado.Vigente is { } vigente && (!RegionValida(vigente.RegionId) || vigente.Huella is not { Length: 64 }
            || vigente.Bytes <= 0 || vigente.DescargadaUtc == default)) throw new InvalidDataException("La preparación local del mapa no es válida.");
        var limpiar = new[] { estado.Intento, estado.Retirada }.OfType<string>().Distinct().ToArray();
        if (limpiar.Any(r => !RegionValida(r) || estado.Vigente?.RegionId == r)) throw new InvalidDataException("El intento de mapa no es válido.");
        if (limpiar.Length > 0)
        {
            try { foreach (var region in limpiar) await proveedor.EliminarAsync(region, ct); }
            catch (OperationCanceledException) { throw; }
            catch { return estado; }
            estado = estado with { Intento = null, Retirada = null };
            await Guardar(cuenta, hoja, estado, ct);
        }
        return estado;
    }
    private Task Guardar(CuentaMovil cuenta, Guid hoja, EstadoMapaGuardado estado, CancellationToken ct) =>
        datos.GuardarMetadatoAsync(cuenta, Clave(hoja), JsonSerializer.SerializeToUtf8Bytes(estado, MapasLocalesJson.Default.EstadoMapaGuardado), ct);
    private static string Clave(Guid hoja) => hoja != Guid.Empty ? $"mapa.{hoja:N}" : throw new ArgumentException("La hoja es obligatoria.");
    private static bool RegionValida(string? region) => region is not null && region.StartsWith("nexoruta-", StringComparison.Ordinal)
        && Guid.TryParseExact(region[9..], "N", out _);
    public void Dispose() => acceso.Dispose();
}
public sealed record MapaGuardado(string RegionId, string Huella, long Bytes, DateTimeOffset DescargadaUtc);
public sealed record EstadoMapaGuardado(MapaGuardado? Vigente, string? Intento, string? Retirada);
[JsonSerializable(typeof(EstadoMapaGuardado))]
internal partial class MapasLocalesJson : JsonSerializerContext;
