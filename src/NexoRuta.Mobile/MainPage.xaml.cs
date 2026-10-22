using NexoRuta.Mobile.Presentacion;
using NexoRuta.Mobile.Application;
using NexoRuta.Mobile.Notificaciones;
namespace NexoRuta.Mobile;
public partial class MainPage : ContentPage
{
    private readonly PrincipalViewModel modelo;
    private bool inicializada;
    private bool abriendoAviso;
    private readonly ServicioAvisosReparto? avisos;
    public MainPage(PrincipalViewModel modelo, ControlNotificaciones control, ServicioAvisosReparto? avisos = null)
    {
        InitializeComponent(); BindingContext = this.modelo = modelo; this.avisos = avisos;
        modelo.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PrincipalViewModel.PuedeOperar) && modelo.PuedeOperar)
                Dispatcher.Dispatch(async () =>
                {
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
    }
    private async void AbrirHoja(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is CollectionView lista) lista.SelectedItem = null;
        if (e.CurrentSelection.FirstOrDefault() is VistaHoja hoja && modelo.PuedeOperar)
            await Navigation.PushAsync(new HojaRutaPage(hoja.Original, modelo.Cuenta, Handler!.MauiContext!.Services));
    }
    private async Task AbrirPendiente()
    {
        if (!modelo.PuedeOperar || avisos is null || abriendoAviso) return;
        abriendoAviso = true;
        try
        {
            var id = await avisos.ObtenerAperturaPendienteAsync(modelo.Cuenta);
            if (id is not null && modelo.PuedeOperar)
                await Navigation.PushAsync(new AvisosPage(modelo.Cuenta, avisos, Handler!.MauiContext!.Services, id));
        }
        catch { /* La referencia cifrada queda pendiente para un siguiente desbloqueo. */ }
        finally { abriendoAviso = false; }
    }
    private async void CambiarFecha(object? sender, DateChangedEventArgs e)
    {
        await modelo.RefrescarLocalesAsync();
        await modelo.ActualizarHojaAsync();
    }
    private async void AbrirCuenta(object? sender, EventArgs e)
    {
        if (modelo.PuedeOperar) await Navigation.PushAsync(new CuentaPage(modelo));
    }
    private async void AbrirAvisos(object? sender, EventArgs e)
    {
        if (modelo.PuedeOperar && avisos is not null && !abriendoAviso)
            await Navigation.PushAsync(new AvisosPage(modelo.Cuenta, avisos, Handler!.MauiContext!.Services));
    }
}
