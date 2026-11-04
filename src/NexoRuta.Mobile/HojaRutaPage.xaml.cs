using NexoRuta.Mobile.Core.Jornadas;
using Microsoft.Extensions.DependencyInjection;
using NexoRuta.Mobile.Core.Rutas;
using NexoRuta.Mobile.Presentacion;
namespace NexoRuta.Mobile;
public partial class HojaRutaPage : ContentPage
{
    private readonly HojaRutaViewModel modelo;
    private readonly IServiceProvider servicios;
    private readonly SincronizacionConectividad? sincronizacion;
    private bool preparada;
    public HojaRutaPage(HojaRuta hoja, CuentaMovil cuenta, IServiceProvider servicios, Func<HojaRuta, Task>? retomar = null)
    {
        InitializeComponent(); this.servicios = servicios;
        modelo = ActivatorUtilities.CreateInstance<HojaRutaViewModel>(servicios, hoja, cuenta); modelo.ConfigurarRetomar(retomar); BindingContext = modelo;
        sincronizacion = servicios.GetService<SincronizacionConectividad>();
    }
    public async Task PrepararAsync()
    {
        if (preparada) return;
        preparada = true;
        await modelo.InicializarAsync();
        await modelo.ActualizarDesdeServidorAsync();
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (sincronizacion is not null) sincronizacion.Finalizada += Actualizar;
        await PrepararAsync();
    }
    protected override void OnDisappearing()
    { if (sincronizacion is not null) sincronizacion.Finalizada -= Actualizar; base.OnDisappearing(); }
    private void Actualizar(CuentaMovil cuenta)
    {
        if (cuenta == modelo.Cuenta) Dispatcher.Dispatch(async () =>
        { if (!modelo.Ocupado) await modelo.Ejecutar(modelo.Refrescar); });
    }
    private async void PrepararMapa(object? sender, EventArgs e)
    {
        if (modelo.Ocupado) return;
        var paradas = modelo.Hoja.Paradas.Where(modelo.ParadaActiva).OrderBy(p => p.Orden).ToArray();
        await Navigation.PushAsync(new Mapas.MapaDestinoPage(servicios.GetRequiredService<Mapas.IProveedorMapas>().Crear(),
            servicios.GetRequiredService<NexoRuta.Mobile.Application.Mapas.ServicioPreparacionMapa>(),
            modelo.Cuenta, modelo.Hoja, paradas));
    }
    private async void AbrirParada(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is CollectionView lista) lista.SelectedItem = null;
        if (e.CurrentSelection.FirstOrDefault() is ParadaResumen elegida)
            await Navigation.PushAsync(new EntregaPage(modelo, modelo.Hoja.Paradas.Single(x => x.EnvioId == elegida.EnvioId), servicios));
    }
}
