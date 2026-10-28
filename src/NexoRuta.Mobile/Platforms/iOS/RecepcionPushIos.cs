#if IOS
using Foundation;
using NexoRuta.Mobile.Core.Notificaciones;
using NexoRuta.Mobile.Notificaciones;
using UserNotifications;

namespace NexoRuta.Mobile.Platforms.iOS;

public sealed class RecepcionPushIos : UNUserNotificationCenterDelegate
{
    private sealed record Referencia(Dictionary<string, string> Datos, bool Abierto);
    private static readonly object Bloqueo = new();
    private static readonly SemaphoreSlim Procesando = new(1, 1);
    private static readonly List<Referencia> Pendientes = [];

    public Task RecibirAsync(NSDictionary? userInfo, bool abierto)
    {
        var datos = LeerDatos(userInfo);
        if (!AvisoPush.IntentarLeer(datos, out var aviso)) return Task.CompletedTask;

        lock (Bloqueo)
        {
            var indice = Pendientes.FindIndex(x => x.Datos["notificacionId"] == aviso!.Id.ToString("D"));
            if (indice >= 0) Pendientes[indice] = Pendientes[indice] with { Abierto = Pendientes[indice].Abierto || abierto };
            else
            {
                if (Pendientes.Count == 100) Pendientes.RemoveAt(0);
                Pendientes.Add(new(datos!, abierto));
            }
        }
        return ProcesarPendientesAsync();
    }

    public static async Task ProcesarPendientesAsync()
    {
        if (IPlatformApplication.Current?.Services.GetService<ControlNotificaciones>() is not { } control) return;
        await Procesando.WaitAsync();
        try
        {
            Referencia[] pendientes;
            lock (Bloqueo) pendientes = Pendientes.ToArray();
            foreach (var pendiente in pendientes)
            {
                if (!await control.RecibirAsync(pendiente.Datos, pendiente.Abierto)) continue;
                lock (Bloqueo) Pendientes.RemoveAll(x => x.Datos["notificacionId"] == pendiente.Datos["notificacionId"]);
            }
        }
        finally { Procesando.Release(); }
    }

    public override void WillPresentNotification(UNUserNotificationCenter center, UNNotification notification,
        Action<UNNotificationPresentationOptions> completionHandler)
    {
        _ = RecibirAsync(notification.Request.Content.UserInfo, abierto: false);
        completionHandler(UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.Sound);
    }

    public override void DidReceiveNotificationResponse(UNUserNotificationCenter center,
        UNNotificationResponse response, Action completionHandler)
    {
        _ = RecibirAsync(response.Notification.Request.Content.UserInfo, abierto: true);
        completionHandler();
    }

    private static Dictionary<string, string>? LeerDatos(NSDictionary? userInfo)
    {
        if (userInfo is null) return null;
        var datos = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var clave in new[] { "v", "notificacionId", "operadorId", "accesoId" })
        {
            using var llave = new NSString(clave);
            if (userInfo.ObjectForKey(llave) is NSString valor) datos[clave] = valor.ToString();
        }
        return datos;
    }
}
#endif
