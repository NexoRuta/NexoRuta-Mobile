using Indiko.Maui.Controls.MapBox;
using Indiko.Maui.Controls.MapBox.Models;
using NexoRuta.Mobile.Core.Rutas;
namespace NexoRuta.Mobile.Mapas;

public sealed class ProveedorMapbox(ConfiguracionAplicacion configuracion) : IProveedorMapas
{
    public IMapaDestino Crear() => string.IsNullOrEmpty(configuracion.MapboxToken) ? new MapaPendiente() : new MapaMapbox();
    private sealed class MapaPendiente : IMapaDestino
    {
        public View Vista { get; } = new Label { Text = "El mapa aún no está disponible. La dirección y las paradas se conservan en el teléfono.", Margin = 20, VerticalOptions = LayoutOptions.Center };
        public bool Disponible => false;
        public void Mostrar(ParadaRuta parada) { }
        public Task DescargarZonaAsync(HojaRuta hoja, IProgress<double> progreso, CancellationToken ct) => throw new InvalidOperationException("El mapa no está configurado.");
        public void Dispose() { }
    }
    private sealed class MapaMapbox : IMapaDestino
    {
        private readonly MapView mapa = new() { StyleUri = MapStyles.Streets, ShowUserLocation = false };
        private readonly SemaphoreSlim descarga = new(1, 1);
        public View Vista => mapa;
        public bool Disponible => true;
        public void Mostrar(ParadaRuta parada)
        {
            mapa.Annotations.Clear();
            mapa.Annotations.Add(new MapAnnotation { Latitude = parada.Latitud, Longitude = parada.Longitud, Title = parada.Direccion, Color = "#0D6EFD" });
            mapa.Camera = new MapCameraPosition(parada.Latitud, parada.Longitud, zoom: 14);
        }
        public async Task DescargarZonaAsync(HojaRuta hoja, IProgress<double> progreso, CancellationToken ct)
        {
            await descarga.WaitAsync(ct);
            var terminado = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var id = $"nexoruta-{hoja.Id:N}-p{hoja.Politica.Version}";
            void Avance(object? sender, OfflineRegionProgressEventArgs e) => progreso.Report(e.Progress);
            void Fin(object? sender, OfflineRegionCompletedEventArgs e)
            {
                if (e.Success) terminado.TrySetResult();
                else terminado.TrySetException(new IOException("No se pudo completar la descarga de la zona."));
            }
            mapa.OfflineRegionProgress += Avance; mapa.OfflineRegionCompleted += Fin;
            try
            {
                var latitudes = hoja.Paradas.Select(x => x.Latitud).ToArray();
                var longitudes = hoja.Paradas.Select(x => x.Longitud).ToArray();
                mapa.DownloadOfflineRegion(new MapOfflineRegion
                {
                    Id = id, StyleUri = MapStyles.Streets, MinLatitude = Math.Clamp(latitudes.Min() - 0.015, -85, 85),
                    MaxLatitude = Math.Clamp(latitudes.Max() + 0.015, -85, 85), MinLongitude = Math.Max(-180, longitudes.Min() - 0.015),
                    MaxLongitude = Math.Min(180, longitudes.Max() + 0.015), MinZoom = 6, MaxZoom = 14
                });
                using var registro = ct.Register(() =>
                {
                    MainThread.BeginInvokeOnMainThread(() => mapa.RemoveOfflineRegion(id));
                    terminado.TrySetCanceled(ct);
                });
                await terminado.Task;
            }
            finally { mapa.OfflineRegionProgress -= Avance; mapa.OfflineRegionCompleted -= Fin; descarga.Release(); }
        }
        public void Dispose() { mapa.Handler?.DisconnectHandler(); descarga.Dispose(); }
    }
}
