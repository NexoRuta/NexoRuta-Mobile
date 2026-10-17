using NexoRuta.Mobile.Core.Jornadas;
using Microsoft.Extensions.DependencyInjection;
using NexoRuta.Mobile.Core.Rutas;
using NexoRuta.Mobile.Presentacion;
namespace NexoRuta.Mobile;
public partial class HojaRutaPage : ContentPage
{
    private readonly HojaRutaViewModel modelo;
    private readonly IServiceProvider servicios;
    public HojaRutaPage(HojaRuta hoja, CuentaMovil cuenta, IServiceProvider servicios)
    {
        InitializeComponent(); this.servicios = servicios;
        modelo = ActivatorUtilities.CreateInstance<HojaRutaViewModel>(servicios, hoja, cuenta); BindingContext = modelo;
    }
    protected override async void OnAppearing() { base.OnAppearing(); await modelo.InicializarAsync(); }
    private async void AbrirParada(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is CollectionView lista) lista.SelectedItem = null;
        if (e.CurrentSelection.FirstOrDefault() is ParadaResumen elegida)
            await Navigation.PushAsync(new EntregaPage(modelo, modelo.Hoja.Paradas.Single(x => x.EnvioId == elegida.EnvioId), servicios));
    }
}
