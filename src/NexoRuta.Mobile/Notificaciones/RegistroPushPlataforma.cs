#if ANDROID
using Android.Gms.Extensions;
using Firebase;
using Firebase.Messaging;
#elif IOS
using Firebase.Installations;
using UIKit;
using UserNotifications;
#endif
namespace NexoRuta.Mobile.Notificaciones;

public sealed class RegistroPushPlataforma : IRegistroPush
{
    public string? MotivoNoDisponible
    {
        get
        {
#if ANDROID
            return FirebaseApp.InitializeApp(Android.App.Application.Context) is null
                ? "Tu operador todavía no configuró las notificaciones." : null;
#elif IOS
            return Firebase.Core.App.DefaultInstance is null
                ? "Tu operador todavía no configuró las notificaciones." : null;
#else
            return "Las notificaciones remotas no están preparadas para esta plataforma.";
#endif
        }
    }
    public async Task<bool> SolicitarPermisoAsync()
    {
#if ANDROID
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            _ = await Permissions.RequestAsync<PermisoAvisos>();
        return await PermitidoAsync();
#elif IOS
        if (MotivoNoDisponible is not null) return false;
        var concedido = await UNUserNotificationCenter.Current.RequestAuthorizationAsync(
            UNAuthorizationOptions.Alert | UNAuthorizationOptions.Badge | UNAuthorizationOptions.Sound);
        if (concedido) UIApplication.SharedApplication.RegisterForRemoteNotifications();
        return concedido;
#else
        await Task.CompletedTask; return false;
#endif
    }
    public Task<bool> PermitidoAsync()
    {
#if ANDROID
        var manager = (Android.App.NotificationManager?)Android.App.Application.Context.GetSystemService(Android.Content.Context.NotificationService);
        if (manager?.AreNotificationsEnabled() != true) return Task.FromResult(false);
        return Task.FromResult(manager.GetNotificationChannel(Platforms.Android.CanalesPush.Id)?.Importance != Android.App.NotificationImportance.None);
#elif IOS
        return PermitidoIosAsync();
#else
        return Task.FromResult(false);
#endif
    }
    public async Task RegistrarAsync()
    {
#if ANDROID
        if (MotivoNoDisponible is not null || !await PermitidoAsync()) return;
        // AutoInitEnabled también puede esperar por el FID; el SDK prohíbe hacerlo en el hilo de UI.
        await Task.Run(async () =>
        {
            FirebaseMessaging.Instance.AutoInitEnabled = true;
            await FirebaseMessaging.Instance.Register().AsAsync();
        });
#elif IOS
        if (MotivoNoDisponible is not null || !await PermitidoAsync()) return;
        Firebase.CloudMessaging.Messaging.SharedInstance.AutoInitEnabled = true;
        UIApplication.SharedApplication.RegisterForRemoteNotifications();
        await ActualizarFidAsync();
#else
        await Task.CompletedTask;
#endif
    }
#if ANDROID
    [System.Runtime.Versioning.SupportedOSPlatform("android33.0")]
    public sealed class PermisoAvisos : Permissions.BasePlatformPermission
    {
        public override (string androidPermission, bool isRuntime)[] RequiredPermissions => [(Android.Manifest.Permission.PostNotifications, true)];
    }
#elif IOS
    internal static async Task<bool> PermisoIosAsync() => await PermitidoIosAsync();

    private static async Task<bool> PermitidoIosAsync()
    {
        var configuracion = await UNUserNotificationCenter.Current.GetNotificationSettingsAsync();
        return configuracion.AuthorizationStatus is UNAuthorizationStatus.Authorized or UNAuthorizationStatus.Provisional;
    }

    internal static async Task ActualizarFidAsync()
    {
        try
        {
            var fid = await Installations.DefaultInstance.GetInstallationIdAsync();
            if (IPlatformApplication.Current?.Services.GetService<ControlNotificaciones>() is { } control)
                await control.RegistroRecibidoAsync(fid);
        }
        catch { /* Firebase/Apple puede no estar disponible; se reintenta al activar avisos. */ }
    }
#endif
}
