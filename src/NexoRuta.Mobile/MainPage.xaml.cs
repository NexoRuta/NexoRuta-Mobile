using NexoRuta.Mobile.Presentacion;
using NexoRuta.Mobile.Application;
using NexoRuta.Mobile.Notificaciones;
using NexoRuta.Mobile.Core.Rutas;
using NexoRuta.Mobile.Accesibilidad;
#if IOS
using NexoRuta.Mobile.Platforms.iOS;
#endif

namespace NexoRuta.Mobile;

public partial class MainPage : ContentPage
{
    private readonly PrincipalViewModel modelo;
    private readonly ServicioAvisosReparto? avisos;
    private bool inicializada;
    private bool vistasInicializadas;
    private bool abriendoAviso;
    private bool cambiandoVista;
    private SeccionPrincipal seccionActual = SeccionPrincipal.Jornada;
    private AvisosPage? vistaAvisos;
    private CuentaPage? vistaCuenta;
    private Guid? aperturaPendiente;
    private readonly List<View> vistasPrincipales = new(3);

    public MainPage(PrincipalViewModel modelo, ControlNotificaciones control, ServicioAvisosReparto? avisos = null)
    {
        InitializeComponent();
        BindingContext = this.modelo = modelo;
        this.avisos = avisos;
        BarraNavegacion.Navegar += SeleccionarVistaAsync;
        modelo.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PrincipalViewModel.Desbloqueada) && !modelo.Desbloqueada)
                Dispatcher.Dispatch(() => MostrarVista(SeccionPrincipal.Jornada));
            if (e.PropertyName == nameof(PrincipalViewModel.PuedeOperar) && modelo.PuedeOperar)
                Dispatcher.Dispatch(async () =>
                {
                    InicializarVistas();
                    MostrarVista(SeccionPrincipal.Jornada);
#if IOS
                    await RecepcionPushIos.ProcesarPendientesAsync();
#endif
                    await AbrirPendiente();
                    await modelo.ActualizarHojaAsync();
                    try { await control.ActualizarRegistroAsync(); } catch { /* La jornada no depende del transporte push. */ }
                });
        };
        if (avisos is not null) avisos.AvisoGuardado += abierto =>
        {
            if (abierto) Dispatcher.Dispatch(async () => await AbrirPendiente());
            else Dispatcher.Dispatch(async () =>
            {
                if (Handler?.MauiContext?.Services.GetService<SincronizacionConectividad>() is { } sync)
                    await sync.IntentarAsync();
            });
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!inicializada) { inicializada = true; await modelo.InicializarAsync(); }
        else await modelo.RefrescarLocalesAsync();
        if (modelo.PuedeOperar)
        {
            InicializarVistas();
            MostrarVista(seccionActual);
        }
#if IOS
        if (modelo.PuedeOperar) await RecepcionPushIos.ProcesarPendientesAsync();
#endif
    }

    private void InicializarVistas()
    {
        if (vistasInicializadas || Handler?.MauiContext is not { } contexto) return;
        var jornada = new JornadaView(modelo, AbrirHojaAsync);
        if (avisos is not null)
            vistaAvisos = new AvisosPage(modelo.Cuenta, avisos, contexto.Services, hoja =>
                AbrirHojaAsync(hoja));
        vistaCuenta = new CuentaPage(modelo, () => DisplayAlertAsync("Cerrar sesión",
            "¿Querés salir de tu cuenta? Si hay cambios por enviar, primero deberás actualizarlos.",
            "Cerrar sesión", "Volver"));

        vistasPrincipales.Add(jornada);
        vistasPrincipales.Add(vistaAvisos ?? new ContentView { Content = new Grid(), IsVisible = false });
        vistasPrincipales.Add(vistaCuenta!);
        foreach (var vista in vistasPrincipales) VistasHost.Children.Add(vista);
        vistasPrincipales[0].IsVisible = true;
        vistasPrincipales[1].IsVisible = false;
        vistasPrincipales[2].IsVisible = false;
        vistasInicializadas = true;
        if (vistaAvisos is not null) _ = vistaAvisos.ActivarAsync();
    }

    private async Task AbrirHojaAsync(HojaRuta hoja)
    {
        var pagina = new HojaRutaPage(hoja, modelo.Cuenta, Handler!.MauiContext!.Services);
        await Navigation.PushAsync(pagina);
    }

    public void CambiarPorSwipe(bool haciaLaDerecha)
    {
        var delta = haciaLaDerecha ? 1 : -1;
        var destino = (SeccionPrincipal)Math.Clamp((int)seccionActual + delta, 0, (int)SeccionPrincipal.Cuenta);
        _ = CambiarVistaAsync(destino);
    }

    private async Task AbrirPendiente()
    {
        if (!modelo.PuedeOperar || avisos is null || abriendoAviso) return;
        abriendoAviso = true;
        try
        {
            var id = await avisos.ObtenerAperturaPendienteAsync(modelo.Cuenta);
            if (id is not null && modelo.PuedeOperar)
                await SeleccionarVistaAsync(SeccionPrincipal.Avisos, id);
        }
        catch { /* La referencia cifrada queda pendiente para un siguiente desbloqueo. */ }
        finally { abriendoAviso = false; }
    }

    private async Task SeleccionarVistaAsync(SeccionPrincipal destino) => await SeleccionarVistaAsync(destino, null);

    private async Task SeleccionarVistaAsync(SeccionPrincipal destino, Guid? apertura)
    {
        if (!modelo.PuedeOperar || (destino == SeccionPrincipal.Avisos && vistaAvisos is null)) return;
        aperturaPendiente = apertura;
        if (seccionActual == destino)
        {
            if (destino == SeccionPrincipal.Avisos && vistaAvisos is not null)
            {
                var id = aperturaPendiente;
                aperturaPendiente = null;
                await vistaAvisos.ActivarAsync(id);
            }
            return;
        }
        await CambiarVistaAsync(destino);
    }

    private async Task CambiarVistaAsync(SeccionPrincipal destino)
    {
        if (!modelo.PuedeOperar || cambiandoVista || (destino == SeccionPrincipal.Avisos && vistaAvisos is null)) return;
        var anterior = seccionActual;
        if (anterior == destino) return;

        cambiandoVista = true;
        var vistaAnterior = vistasPrincipales[(int)anterior];
        var vistaDestino = vistasPrincipales[(int)destino];
        try
        {
            VistasHost.Children.Remove(vistaDestino);
            VistasHost.Children.Add(vistaDestino);
            vistaDestino.IsVisible = true;
            if (PreferenciasMovimiento.Reducido)
            {
                vistaDestino.TranslationX = 0;
            }
            else
            {
                var direccion = destino > anterior ? 1 : -1;
                var distancia = Math.Max(VistasHost.Width, 280);
                vistaDestino.TranslationX = direccion * distancia;
                await Task.WhenAll(
                    vistaDestino.TranslateToAsync(0, 0, 180, Easing.CubicOut),
                    vistaAnterior.TranslateToAsync(-direccion * distancia, 0, 180, Easing.CubicOut));
            }
            vistaAnterior.IsVisible = false;
            vistaAnterior.TranslationX = 0;
            seccionActual = destino;
            BarraNavegacion.Seleccionar(destino);
        }
        finally { cambiandoVista = false; }

        if (destino == SeccionPrincipal.Avisos && vistaAvisos is not null)
        {
            var apertura = aperturaPendiente;
            aperturaPendiente = null;
            await vistaAvisos.ActivarAsync(apertura);
        }
    }

    private void MostrarVista(SeccionPrincipal destino)
    {
        if (!modelo.Desbloqueada) destino = SeccionPrincipal.Jornada;
        if (vistasPrincipales.Count == 3)
            for (var i = 0; i < vistasPrincipales.Count; i++)
                vistasPrincipales[i].IsVisible = i == (int)destino;
        seccionActual = destino;
        BarraNavegacion.Seleccionar(destino);
    }
}
