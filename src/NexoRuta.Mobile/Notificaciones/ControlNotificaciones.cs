using NexoRuta.Mobile.Api.Identidad;
using NexoRuta.Mobile.Application;
namespace NexoRuta.Mobile.Notificaciones;

public sealed class ControlNotificaciones(IRegistroPush registro, ServicioAvisosReparto? avisos = null, ClienteSesionMovil? cliente = null)
{
    private const string ClaveFid = "nexoruta.push.fid.v1";
    public bool PuedeActivar => registro.MotivoNoDisponible is null;
    public string Estado { get; private set; } = registro.MotivoNoDisponible ?? "Activá los avisos para recibir cambios urgentes de tu ruta.";
    public async Task ActivarAsync()
    {
        if (cliente is null) { Estado = "Primero debe configurarse el servicio de NexoRuta."; return; }
        if (registro.MotivoNoDisponible is { } motivo) { Estado = motivo; return; }
        if (!await registro.SolicitarPermisoAsync())
        {
            Estado = "Las notificaciones están desactivadas. Podés consultar las novedades en Avisos.";
            await ActualizarRegistroAsync(); return;
        }
        await registro.RegistrarAsync();
        await ActualizarRegistroAsync();
        Estado = await SecureStorage.Default.GetAsync(ClaveFid) is null
            ? "Activando las notificaciones…"
            : "Notificaciones activadas.";
    }
    public async Task RegistroRecibidoAsync(string fid)
    {
        if (fid.Length is < 20 or > 128 || fid.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_'))) return;
        await SecureStorage.Default.SetAsync(ClaveFid, fid);
        await ActualizarRegistroAsync();
    }
    public async Task ActualizarRegistroAsync(CancellationToken ct = default)
    {
        if (avisos is null || cliente is null || registro.MotivoNoDisponible is not null) return;
        var cuenta = await cliente.LeerCuentaGuardadaAsync(ct);
        var fid = await SecureStorage.Default.GetAsync(ClaveFid);
        if (cuenta is null || fid is null) return;
        await avisos.RegistrarInstalacionAsync(cuenta,
#if ANDROID
            "Android",
#else
            "iOS",
#endif
            fid, await registro.PermitidoAsync(), ct);
    }
    public void SalidaIncompleta() => Estado = "Si la salida no se completó, los avisos se reactivan al recuperar conexión o volver a ingresar.";
    public void SesionCerrada()
    {
        Estado = "Activá los avisos para recibir cambios urgentes de tu ruta.";
#if ANDROID
        var manager = (global::Android.App.NotificationManager?)global::Android.App.Application.Context.GetSystemService(global::Android.Content.Context.NotificationService);
        manager?.CancelAll();
#elif IOS
        global::UserNotifications.UNUserNotificationCenter.Current.RemoveAllDeliveredNotifications();
#endif
    }
    public async Task RecibirAsync(IReadOnlyDictionary<string, string> datos, bool abierto)
    {
        if (avisos is null) return;
        try { await avisos.GuardarReferenciaAsync(datos, abierto); }
        catch { Estado = "Hay una novedad en tu recorrido. Revisá tus avisos."; }
    }
}
