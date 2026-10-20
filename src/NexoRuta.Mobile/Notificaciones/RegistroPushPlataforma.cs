#if ANDROID
using Android.Gms.Extensions;
using Firebase;
using Firebase.Messaging;
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
                ? "Las notificaciones aún no tienen un proyecto Firebase configurado." : null;
#elif IOS
            return "Push iOS requiere actualizar el binding Firebase para registro por FID y validarlo en Mac.";
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
#else
        return Task.FromResult(false);
#endif
    }
    public async Task RegistrarAsync()
    {
#if ANDROID
        if (MotivoNoDisponible is not null || !await PermitidoAsync()) return;
        FirebaseMessaging.Instance.AutoInitEnabled = true;
        await FirebaseMessaging.Instance.Register().AsAsync();
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
#endif
}
