using System.Text.Json;
using System.Text.Json.Serialization;
using Indiko.Maui.Controls.MapBox;
using Indiko.Maui.Controls.MapBox.Models;
using NexoRuta.Mobile.Application.Mapas;
using NexoRuta.Mobile.Core.Mapas;
using NexoRuta.Mobile.Core.Rutas;
namespace NexoRuta.Mobile.Mapas;

public sealed class ProveedorMapbox(ConfiguracionAplicacion configuracion) : IProveedorMapas, IProveedorMapaOffline
{
    public string Proveedor => "mapbox";
    public string Estilo => MapStyles.Streets;
    public bool Disponible => !string.IsNullOrEmpty(configuracion.MapboxToken)
#if ANDROID
        ;
#else
        && false;
#endif
    public IMapaDestino Crear() => string.IsNullOrEmpty(configuracion.MapboxToken) ? new MapaPendiente() : new MapaMapbox();
    public async Task<RecursosMapa> ConsultarAsync(string regionId, CancellationToken ct)
    {
#if ANDROID
        ExigirConfiguracion();
        var estado = await Task.Run(() => Leer(MapboxOfflineAndroid.Inspect(regionId, Estilo)), ct);
        ct.ThrowIfCancellationRequested();
        return new(estado.Complete, estado.Bytes);
#else
        await Task.CompletedTask;
        return new(false, 0);
#endif
    }
    public async Task DescargarAsync(string regionId, ZonaMapa zona, IProgress<ProgresoMapa> progreso, CancellationToken ct)
    {
#if ANDROID
        ExigirConfiguracion();
        try
        {
            ct.ThrowIfCancellationRequested();
            MapboxOfflineAndroid.Start(regionId, zona.Estilo, zona.LatitudMinima, zona.LatitudMaxima,
                zona.LongitudMinima, zona.LongitudMaxima, zona.ZoomMinimo, zona.ZoomMaximo);
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var estado = Leer(MapboxOfflineAndroid.Progress(regionId));
                progreso.Report(new(estado.Fraction, estado.Bytes));
                if (estado.Phase == "complete") return;
                if (estado.Phase != "running") throw new IOException("No se completaron el estilo y las teselas. Verificá conexión y espacio disponible.");
                await Task.Delay(250, ct);
            }
        }
        finally { MapboxOfflineAndroid.Cancel(regionId); MapboxOfflineAndroid.Forget(regionId); }
#else
        await Task.CompletedTask;
        throw new InvalidOperationException("La descarga offline todavía no está disponible en este dispositivo.");
#endif
    }
    public async Task EliminarAsync(string regionId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
#if ANDROID
        ExigirConfiguracion();
        await Task.Run(() => MapboxOfflineAndroid.Remove(regionId), ct);
        ct.ThrowIfCancellationRequested();
#else
        await Task.CompletedTask;
#endif
    }
#if ANDROID
    private void ExigirConfiguracion()
    {
        if (!Disponible) throw new InvalidOperationException("El mapa no está configurado.");
        MapboxOfflineAndroid.Configure(configuracion.MapboxToken!);
    }
    private static EstadoNativo Leer(string? json)
    {
        if (json is null || json.Length > 2048) throw new InvalidDataException("El proveedor no pudo comprobar los recursos del mapa.");
        var estado = JsonSerializer.Deserialize(json, MapboxEstadoJson.Default.EstadoNativo)
            ?? throw new InvalidDataException("El estado del mapa está vacío.");
        if (estado.Bytes < 0 || !double.IsFinite(estado.Fraction) || estado.Fraction is < 0 or > 1)
            throw new InvalidDataException("El estado del mapa no es válido.");
        return estado;
    }
#endif
    private sealed class MapaPendiente : IMapaDestino
    {
        public View Vista { get; } = new Label { Text = "El mapa aún no está disponible. La dirección y las paradas se conservan en el teléfono.", Margin = 20, VerticalOptions = LayoutOptions.Center };
        public void Mostrar(ParadaRuta parada) { }
        public void Dispose() { }
    }
    private sealed class MapaMapbox : IMapaDestino
    {
        private readonly MapView mapa = new() { StyleUri = MapStyles.Streets, ShowUserLocation = false };
        public View Vista => mapa;
        public void Mostrar(ParadaRuta parada)
        {
            mapa.Annotations.Clear();
            mapa.Annotations.Add(new MapAnnotation { Latitude = parada.Latitud, Longitude = parada.Longitud, Title = parada.Direccion, Color = "#0D6EFD" });
            mapa.Camera = new MapCameraPosition(parada.Latitud, parada.Longitud, zoom: 14);
        }
        public void Dispose() => mapa.Handler?.DisconnectHandler();
    }
}
#if ANDROID
internal sealed record EstadoNativo(string Phase, long Bytes, double Fraction, bool Complete);
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(EstadoNativo))]
internal partial class MapboxEstadoJson : JsonSerializerContext;
#endif
