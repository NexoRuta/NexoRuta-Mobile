namespace NexoRuta.Mobile.Ubicacion;
public sealed partial class TrackingJornada : ITrackingJornada
{
    private readonly RegistroUbicacion registro;
    public TrackingJornada(RegistroUbicacion registro) => this.registro = registro;
    public async Task<bool> IniciarAsync()
    {
        if (!await registro.TieneActivaAsync()) return false;
        if (await Permissions.RequestAsync<Permissions.LocationWhenInUse>() != PermissionStatus.Granted) return false;
#if ANDROID
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            _ = await Permissions.RequestAsync<PermisoNotificaciones>();
        AndroidX.Core.Content.ContextCompat.StartForegroundService(Android.App.Application.Context,
            new Android.Content.Intent(Android.App.Application.Context, typeof(Platforms.Android.ServicioUbicacion)));
        return true;
#elif IOS
        return IniciarIos();
#else
        return false;
#endif
    }
    public void Detener()
    {
#if ANDROID
        Android.App.Application.Context.StopService(new Android.Content.Intent(Android.App.Application.Context, typeof(Platforms.Android.ServicioUbicacion)));
#elif IOS
        DetenerIos();
#endif
    }
#if ANDROID
    [System.Runtime.Versioning.SupportedOSPlatform("android33.0")]
    private sealed class PermisoNotificaciones : Permissions.BasePlatformPermission
    {
        public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
            [(Android.Manifest.Permission.PostNotifications, true)];
    }
#endif
}
