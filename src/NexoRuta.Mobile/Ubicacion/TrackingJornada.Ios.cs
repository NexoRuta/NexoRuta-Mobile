#if IOS
using CoreLocation;
namespace NexoRuta.Mobile.Ubicacion;
public sealed partial class TrackingJornada
{
    private CLLocationManager? gestor;
    private DelegadoUbicacion? delegado;
    private bool IniciarIos()
    {
        if (!CLLocationManager.LocationServicesEnabled) return false;
        gestor ??= new CLLocationManager
        {
            DesiredAccuracy = CLLocation.AccuracyBest, DistanceFilter = CLLocationDistance.FilterNone,
            AllowsBackgroundLocationUpdates = true, PausesLocationUpdatesAutomatically = false,
            ShowsBackgroundLocationIndicator = true
        };
        delegado ??= new DelegadoUbicacion(registro);
        gestor.Delegate = delegado; gestor.StartUpdatingLocation(); return true;
    }
    private void DetenerIos() { gestor?.StopUpdatingLocation(); gestor?.Dispose(); gestor = null; delegado?.Dispose(); delegado = null; }
    private sealed class DelegadoUbicacion(RegistroUbicacion registro) : CLLocationManagerDelegate
    {
        public override void LocationsUpdated(CLLocationManager manager, CLLocation[] locations)
        {
            foreach (var punto in locations)
            {
                if (punto.HorizontalAccuracy <= 0) continue;
                var fecha = new DateTimeOffset(DateTime.SpecifyKind((DateTime)punto.Timestamp, DateTimeKind.Utc));
                _ = Guardar(manager, punto.Coordinate.Latitude, punto.Coordinate.Longitude, punto.HorizontalAccuracy, fecha);
            }
        }
        private async Task Guardar(CLLocationManager manager, double lat, double lon, double precision, DateTimeOffset fecha)
        {
            try { if (!await registro.RegistrarAsync(lat, lon, precision, fecha, CancellationToken.None)) manager.StopUpdatingLocation(); }
            catch (Exception) { System.Diagnostics.Debug.WriteLine("No se pudo registrar una posición de la jornada."); }
        }
    }
}
#endif
