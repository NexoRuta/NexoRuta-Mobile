namespace NexoRuta.Mobile;
public partial class App : Microsoft.Maui.Controls.Application
{
    private readonly IServiceProvider servicios;
    public App(IServiceProvider servicios) { InitializeComponent(); this.servicios = servicios; }
    protected override Window CreateWindow(IActivationState? activationState)
    {
        // Application debe cargar los recursos antes de construir páginas con StaticResource.
        var principal = servicios.GetRequiredService<MainPage>();
        var modelo = servicios.GetRequiredService<Presentacion.PrincipalViewModel>();
        var navegacion = new NavigationPage(principal);
        var ventana = new Window(navegacion);
        var sincronizacion = servicios.GetService<SincronizacionConectividad>();
        sincronizacion?.Iniciar();
        ventana.Deactivated += (_, _) => navegacion.Opacity = 0;
        ventana.Activated += async (_, _) => { navegacion.Opacity = 1; if (sincronizacion is not null) await sincronizacion.IntentarAsync(); };
        ventana.Stopped += (_, _) => { modelo.Bloquear(); principal.Dispatcher.Dispatch(async () => await navegacion.PopToRootAsync(false)); };
        return ventana;
    }
}
