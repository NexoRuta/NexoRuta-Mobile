using Android.App;
using Android.Content;
using Firebase.Messaging;
using NexoRuta.Mobile.Core.Notificaciones;
using NexoRuta.Mobile.Notificaciones;
namespace NexoRuta.Mobile.Platforms.Android;

[Service(Exported = false)]
[IntentFilter(["com.google.firebase.MESSAGING_EVENT"])]
public sealed class ServicioMensajeria : FirebaseMessagingService
{
    public override void OnRegistered(string installationId)
    {
        _ = GuardarRegistro(installationId);
    }
    private static async Task GuardarRegistro(string fid)
    {
        try
        {
            if (IPlatformApplication.Current?.Services.GetService<ControlNotificaciones>() is { } control)
                await control.RegistroRecibidoAsync(fid);
        }
        catch { /* El registro se reintenta al abrir la app; no se registran identificadores en logs. */ }
    }
    public override void OnMessageReceived(RemoteMessage message)
    {
        var datos = message.Data?.ToDictionary(x => x.Key, x => x.Value);
        if (!AvisoPush.IntentarLeer(datos, out var aviso)) return;
        CanalesPush.Crear();
        var manager = (NotificationManager?)GetSystemService(NotificationService);
        if (manager?.AreNotificationsEnabled() == true)
        {
            var intent = new Intent(this, typeof(MainActivity))
                .SetFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop)
                .SetData(global::Android.Net.Uri.Parse($"nexoruta://avisos/{aviso!.Id}"));
            foreach (var dato in datos!) intent.PutExtra(dato.Key, dato.Value);
            using var pendiente = PendingIntent.GetActivity(this, 0, intent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
            var notificacion = new Notification.Builder(this, CanalesPush.Id)
                .SetSmallIcon(Resource.Drawable.ic_reparto).SetContentTitle("Cambio en tu hoja de ruta")
                .SetContentText("Hay una entrega que debés revisar. Abrí NexoRuta.")
                .SetVisibility(NotificationVisibility.Private).SetAutoCancel(true).SetContentIntent(pendiente).Build();
            manager.Notify(aviso!.Id.ToString(), 0, notificacion);
        }
        if (IPlatformApplication.Current?.Services.GetService<ControlNotificaciones>() is { } control)
            _ = control.RecibirAsync(datos!, abierto: false);
    }
}
