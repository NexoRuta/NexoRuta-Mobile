using Microsoft.Extensions.Logging;
using NexoRuta.Mobile.Application;
using NexoRuta.Mobile.Application.Notificaciones;

namespace NexoRuta.Mobile.Notificaciones;

public sealed class ControlNotificaciones(
    ServicioRegistroPush registroPush,
    ServicioAvisosReparto? avisos = null,
    ILogger<ControlNotificaciones>? logger = null)
{
    public event Action? EstadoCambiado
    {
        add => registroPush.EstadoCambiado += value;
        remove => registroPush.EstadoCambiado -= value;
    }

    public bool PuedeActivar => registroPush.PuedeActivar;
    public string Estado => registroPush.Estado;

    public Task ActivarAsync() => registroPush.ActivarAsync();
    public Task RegistroRecibidoAsync(string fid) => registroPush.RegistroRecibidoAsync(fid);
    public Task<bool> ActualizarRegistroAsync(CancellationToken ct = default) => registroPush.ActualizarRegistroAsync(ct);
    public void SalidaIncompleta() => registroPush.SalidaIncompleta();

    public void SesionCerrada()
    {
        registroPush.SesionCerrada();
#if ANDROID
        var manager = (global::Android.App.NotificationManager?)global::Android.App.Application.Context.GetSystemService(global::Android.Content.Context.NotificationService);
        manager?.CancelAll();
#elif IOS
        global::UserNotifications.UNUserNotificationCenter.Current.RemoveAllDeliveredNotifications();
#endif
    }

    public async Task<bool> RecibirAsync(IReadOnlyDictionary<string, string> datos, bool abierto)
    {
        if (avisos is null) return false;
        try { return await avisos.GuardarReferenciaAsync(datos, abierto); }
        catch (Exception ex)
        {
            logger?.LogWarning("No se pudo guardar una referencia de aviso push. ErrorType: {ErrorType}", ex.GetType().Name);
            registroPush.AvisoPendiente();
            return false;
        }
    }
}
