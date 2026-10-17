using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Locations;
using Android.OS;
using NexoRuta.Mobile.Ubicacion;
namespace NexoRuta.Mobile.Platforms.Android;

[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeLocation)]
public sealed class ServicioUbicacion : Service, ILocationListener
{
    private LocationManager? ubicacion;
    private readonly CancellationTokenSource cancelacion = new();
    private const string Canal = "nexoruta.jornada";
    public override void OnCreate()
    {
        base.OnCreate();
        var notificaciones = (NotificationManager)GetSystemService(NotificationService)!;
        notificaciones.CreateNotificationChannel(new NotificationChannel(Canal, "Jornada de reparto", NotificationImportance.Low));
        var abrir = PackageManager!.GetLaunchIntentForPackage(PackageName!)!;
        var accion = PendingIntent.GetActivity(this, 70, abrir, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        var notificacion = new Notification.Builder(this, Canal).SetSmallIcon(Resource.Drawable.ic_reparto)
            .SetContentTitle("Jornada de reparto activa").SetContentText("Se registra la ubicación durante la jornada.")
            .SetOngoing(true).SetContentIntent(accion).Build();
        var fina = AndroidX.Core.Content.ContextCompat.CheckSelfPermission(this, global::Android.Manifest.Permission.AccessFineLocation) == Permission.Granted;
        var aproximada = AndroidX.Core.Content.ContextCompat.CheckSelfPermission(this, global::Android.Manifest.Permission.AccessCoarseLocation) == Permission.Granted;
        if (!fina && !aproximada) { StopSelf(); return; }
        try { StartForeground(70, notificacion, ForegroundService.TypeLocation); }
        catch (Java.Lang.SecurityException) { StopSelf(); return; }
        ubicacion = (LocationManager)GetSystemService(LocationService)!;
        foreach (var proveedor in new[] { LocationManager.GpsProvider, LocationManager.NetworkProvider })
            if ((proveedor != LocationManager.GpsProvider || fina) && ubicacion.AllProviders!.Contains(proveedor)) ubicacion.RequestLocationUpdates(proveedor, 30000, 0, this, Looper.MainLooper);
    }
    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        _ = ComprobarJornada(); return StartCommandResult.Sticky;
    }
    private async Task ComprobarJornada()
    {
        try
        {
            if (!await IPlatformApplication.Current!.Services.GetRequiredService<RegistroUbicacion>().TieneActivaAsync(cancelacion.Token)) StopSelf();
        }
        catch (Exception) { StopSelf(); }
    }
    public override IBinder? OnBind(Intent? intent) => null;
    public void OnLocationChanged(global::Android.Locations.Location location)
    {
        if (location.HasAccuracy) _ = Guardar(location.Latitude, location.Longitude, location.Accuracy, DateTimeOffset.FromUnixTimeMilliseconds(location.Time));
    }
    private async Task Guardar(double lat, double lon, double precision, DateTimeOffset fecha)
    {
        try
        {
            var servicio = IPlatformApplication.Current!.Services.GetRequiredService<RegistroUbicacion>();
            if (!await servicio.RegistrarAsync(lat, lon, precision, fecha, cancelacion.Token)) StopSelf();
        }
        catch (System.OperationCanceledException) { }
        catch (Exception)
        {
            // Sin coordenadas ni credenciales en logs; no inventar un punto si falla la persistencia.
            global::Android.Util.Log.Warn("NexoRuta.Location", "No se pudo registrar una posición; se conserva el trabajo previo.");
        }
    }
    public void OnProviderEnabled(string provider) { }
    public void OnProviderDisabled(string provider) { }
    public void OnStatusChanged(string? provider, Availability status, Bundle? extras) { }
    public override void OnDestroy()
    {
        cancelacion.Cancel(); ubicacion?.RemoveUpdates(this); ubicacion?.Dispose();
        StopForeground(StopForegroundFlags.Remove); base.OnDestroy();
    }
}
