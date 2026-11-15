using NexoRuta.Mobile.Api.Identidad;
using NexoRuta.Mobile.Application;
using NexoRuta.Mobile.Core.Jornadas;
using NexoRuta.Mobile.Storage;
using NexoRuta.Mobile.Application.Notificaciones;
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
            await TrabajoConRegistroPushIndependiente.EjecutarAsync(
                async () =>
                {
                    cuenta = await cliente.LeerCuentaGuardadaAsync(limite.Token);
                    if (cuenta is null) return;
                    await sincronizador.SincronizarPendientesAsync(cuenta, limite.Token);
                    await avisos.RegistrarRecepcionesAsync(cuenta, limite.Token);
                    foreach (var jornada in await datos.LeerJornadasAsync(cuenta, limite.Token, soloAbiertas: true))
                        await avisos.ActualizarRutaAsync(cuenta, jornada.RutaId, limite.Token);
                },
                async () =>
                {
                    if (cuenta is null) return;
                    // El registro push tiene su propio plazo y se intenta aunque falle el
                    // trabajo anterior o expire su límite de 45 segundos.
                    using var limitePush = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                    await notificaciones.ActualizarRegistroAsync(limitePush.Token);
                });
        }
        finally
        {
            Volatile.Write(ref enCurso, 0);
            if (cuenta is not null) Finalizada?.Invoke(cuenta);
        }
    }
    public void Dispose()
    { if (suscrita) Connectivity.Current.ConnectivityChanged -= Cambio; }
}
