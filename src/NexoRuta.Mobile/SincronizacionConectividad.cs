using NexoRuta.Mobile.Api.Identidad;
using NexoRuta.Mobile.Application;
using NexoRuta.Mobile.Core.Jornadas;
using NexoRuta.Mobile.Storage;
namespace NexoRuta.Mobile;

// Reanuda también cierres pendientes, que ya no tienen el servicio GPS activo.
public sealed class SincronizacionConectividad(ClienteSesionMovil cliente, SincronizadorJornada sincronizador,
    RepositorioJornadasSqlite datos, ServicioAvisosReparto avisos, Notificaciones.ControlNotificaciones notificaciones) : IDisposable
{
    private int enCurso;
    private bool suscrita;
    public event Action<CuentaMovil>? Finalizada;
    public void Iniciar()
    {
        if (suscrita) return;
        Connectivity.Current.ConnectivityChanged += Cambio; suscrita = true;
    }
    private async void Cambio(object? sender, ConnectivityChangedEventArgs e)
    { if (e.NetworkAccess == NetworkAccess.Internet) await IntentarAsync(); }
    public async Task IntentarAsync()
    {
        if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet || Interlocked.Exchange(ref enCurso, 1) != 0) return;
        CuentaMovil? cuenta = null;
        using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            cuenta = await cliente.LeerCuentaGuardadaAsync(limite.Token);
            if (cuenta is null) return;
            await sincronizador.SincronizarPendientesAsync(cuenta, limite.Token);
            await avisos.RegistrarRecepcionesAsync(cuenta, limite.Token);
            foreach (var jornada in await datos.LeerJornadasAsync(cuenta, limite.Token, soloAbiertas: true))
                await avisos.ActualizarRutaAsync(cuenta, jornada.RutaId, limite.Token);
            await notificaciones.ActualizarRegistroAsync(limite.Token);
        }
        catch (Exception) { /* El siguiente disparador reintenta desde el último ACK durable. */ }
        finally
        {
            Volatile.Write(ref enCurso, 0);
            if (cuenta is not null) Finalizada?.Invoke(cuenta);
        }
    }
    public void Dispose()
    { if (suscrita) Connectivity.Current.ConnectivityChanged -= Cambio; }
}
