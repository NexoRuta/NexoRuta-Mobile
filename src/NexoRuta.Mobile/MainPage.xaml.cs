using NexoRuta.Mobile.Core.Rutas;
using NexoRuta.Mobile.Presentacion;
namespace NexoRuta.Mobile;
public partial class MainPage : ContentPage
{
    private readonly PrincipalViewModel modelo;
    private bool inicializada;
    public MainPage(PrincipalViewModel modelo) { InitializeComponent(); BindingContext = this.modelo = modelo; }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!inicializada) { inicializada = true; await modelo.InicializarAsync(); }
    }
    private async void AbrirHoja(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is CollectionView lista) lista.SelectedItem = null;
        if (e.CurrentSelection.FirstOrDefault() is HojaRuta hoja && modelo.Desbloqueada)
            await Navigation.PushAsync(new HojaRutaPage(hoja, modelo.Cuenta, Handler!.MauiContext!.Services));
    }
}
