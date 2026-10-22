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
    public HojaRutaPage(HojaRuta hoja, CuentaMovil cuenta, IServiceProvider servicios)
    {
        InitializeComponent(); this.servicios = servicios;
        modelo = ActivatorUtilities.CreateInstance<HojaRutaViewModel>(servicios, hoja, cuenta); BindingContext = modelo;
        sincronizacion = servicios.GetService<SincronizacionConectividad>();
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (sincronizacion is not null) sincronizacion.Finalizada += Actualizar;
        await modelo.InicializarAsync();
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
        var parada = modelo.Hoja.Paradas.OrderBy(p => p.Orden).First();
        await Navigation.PushAsync(new Mapas.MapaDestinoPage(servicios.GetRequiredService<Mapas.IProveedorMapas>().Crear(),
            servicios.GetRequiredService<NexoRuta.Mobile.Application.Mapas.ServicioPreparacionMapa>(),
            modelo.Cuenta, modelo.Hoja, parada));
    }
    private async void AbrirParada(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is CollectionView lista) lista.SelectedItem = null;
        if (e.CurrentSelection.FirstOrDefault() is ParadaResumen elegida)
            await Navigation.PushAsync(new EntregaPage(modelo, modelo.Hoja.Paradas.Single(x => x.EnvioId == elegida.EnvioId), servicios));
    }
}
