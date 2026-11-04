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
        if (!estado.Complete && estado.Error is { Length: > 0 } error) Android.Util.Log.Warn("NexoRuta.Mapbox", $"offline.inspect.failed code={error}");
        return new(estado.Complete, estado.Bytes, estado.Error);
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
                if (estado.Phase != "running")
                {
                    var codigo = estado.Error ?? estado.Phase;
                    Android.Util.Log.Warn("NexoRuta.Mapbox", $"offline.download.failed code={codigo}");
                    throw new IOException(MensajeError(estado.Error, estado.Phase));
                }
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
    private static string MensajeError(string? codigo, string fase) => codigo switch
    {
        "style_pack:DISK_FULL" or "tile_region:DISK_FULL" => "Mapbox informó que falta espacio en el dispositivo.",
        "style_pack:TILE_COUNT_EXCEEDED" or "tile_region:TILE_COUNT_EXCEEDED" => "La zona seleccionada supera el límite de recursos del mapa.",
        "style_pack:CANCELED" or "tile_region:CANCELED" => "Mapbox canceló la preparación.",
        "style_pack:DOES_NOT_EXIST" or "tile_region:DOES_NOT_EXIST" => "El estilo o la zona no están disponibles en la caché offline.",
        "inspect_timeout" => "Mapbox no respondió a la verificación dentro del tiempo esperado.",
        "sdk_exception:IllegalArgumentException" => "Mapbox rechazó la geometría, estilo o nivel de zoom solicitado.",
        null when fase == "failed" => "Mapbox no completó el estilo y las teselas. Revisá la conexión y el espacio disponible.",
        _ => "El SDK de Mapbox no confirmó todos los recursos del estilo y la zona. Revisá la conexión y el espacio disponible."
    };
#endif
    private sealed class MapaPendiente : IMapaDestino
    {
        public View Vista { get; } = new Label { Text = "Mapbox no está configurado para esta compilación. La dirección sigue disponible.", Margin = 20, VerticalOptions = LayoutOptions.Center };
        public string EstadoInicial => "";
        public event Action<string>? EstadoCambiado { add { } remove { } }
        public void Mostrar(IReadOnlyCollection<ParadaRuta> paradas) { }
        public void Dispose() { }
    }
    private sealed class MapaMapbox : IMapaDestino
    {
        private readonly MapView mapa = new() { StyleUri = MapStyles.Streets, ShowUserLocation = false, FitBoundsPadding = 56 };
        public string EstadoInicial => "Cargando mapa online…";
        public event Action<string>? EstadoCambiado;
        public MapaMapbox() => mapa.StyleLoaded += (_, _) => EstadoCambiado?.Invoke("listo");
        public View Vista => mapa;
        public void Mostrar(IReadOnlyCollection<ParadaRuta> paradas)
        {
            mapa.Annotations.Clear();
            var ubicadas = paradas.Where(p => double.IsFinite(p.Latitud) && p.Latitud is >= -90 and <= 90
                && double.IsFinite(p.Longitud) && p.Longitud is >= -180 and <= 180).OrderBy(p => p.Orden).ToArray();
            foreach (var parada in ubicadas)
                mapa.Annotations.Add(new MapAnnotation { Id = parada.EnvioId.ToString("N"), Latitude = parada.Latitud,
                    Longitude = parada.Longitud, Title = parada.Titulo, Color = "#0D6EFD" });
            if (ubicadas.Length == 1) mapa.Camera = new MapCameraPosition(ubicadas[0].Latitud, ubicadas[0].Longitud, zoom: 14);
            else if (ubicadas.Length > 1) mapa.FitBoundsToContent();
        }
        public void Dispose() => mapa.Handler?.DisconnectHandler();
    }
}
#if ANDROID
internal sealed record EstadoNativo(string Phase, long Bytes, double Fraction, bool Complete, string? Error);
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(EstadoNativo))]
internal partial class MapboxEstadoJson : JsonSerializerContext;
#endif
