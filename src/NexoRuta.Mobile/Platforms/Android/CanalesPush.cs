using Android.App;
using Android.Content;
namespace NexoRuta.Mobile.Platforms.Android;

public static class CanalesPush
{
    public const string Id = "nexoruta.avisos";
    public static void Crear()
    {
        var manager = (NotificationManager?)global::Android.App.Application.Context.GetSystemService(Context.NotificationService);
        manager?.CreateNotificationChannel(new NotificationChannel(Id, "Cambios urgentes de la ruta", NotificationImportance.High)
        { Description = "Reasignaciones y cancelaciones que requieren revisar una entrega.", LockscreenVisibility = NotificationVisibility.Private });
    }
}
