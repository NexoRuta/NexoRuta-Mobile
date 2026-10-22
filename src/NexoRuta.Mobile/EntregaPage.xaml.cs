using NexoRuta.Mobile.Core.Rutas;
using Microsoft.Extensions.DependencyInjection;
using NexoRuta.Mobile.Mapas;
using NexoRuta.Mobile.Presentacion;
namespace NexoRuta.Mobile;
public partial class EntregaPage : ContentPage
{
    private readonly EntregaViewModel modelo;
    private readonly HojaRutaViewModel hoja;
    private readonly IServiceProvider servicios;
    private readonly ParadaRuta parada;
    private bool inicializada;
    public EntregaPage(HojaRutaViewModel hoja, ParadaRuta parada, IServiceProvider servicios)
    {
        InitializeComponent(); this.hoja = hoja; this.parada = parada; this.servicios = servicios;
        modelo = ActivatorUtilities.CreateInstance<EntregaViewModel>(servicios, hoja, parada); BindingContext = modelo;
    }
    protected override async void OnAppearing() { base.OnAppearing(); if (!inicializada) { inicializada = true; await modelo.InicializarAsync(); } }
    private async void AbrirMapa(object? sender, EventArgs e) =>
        await Navigation.PushAsync(new MapaDestinoPage(servicios.GetRequiredService<IProveedorMapas>().Crear(),
            servicios.GetRequiredService<NexoRuta.Mobile.Application.Mapas.ServicioPreparacionMapa>(), hoja.Cuenta, hoja.Hoja, parada));
    protected override void OnParentSet() { base.OnParentSet(); if (Parent is null) modelo?.Desconectar(); }
}
