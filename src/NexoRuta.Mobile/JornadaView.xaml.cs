using NexoRuta.Mobile.Core.Rutas;
using NexoRuta.Mobile.Application;
using NexoRuta.Mobile.Presentacion;

namespace NexoRuta.Mobile;

public partial class JornadaView : ContentView
{
    private readonly PrincipalViewModel modelo;
    private readonly Func<HojaRuta, Task> abrirHoja;

    public JornadaView(PrincipalViewModel modelo, Func<HojaRuta, Task> abrirHoja)
    {
        InitializeComponent();
        this.modelo = modelo;
        this.abrirHoja = abrirHoja;
        BindingContext = modelo;
    }

    private async void AbrirHoja(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is CollectionView lista) lista.SelectedItem = null;
        if (e.CurrentSelection.FirstOrDefault() is VistaHoja hoja && modelo.PuedeOperar)
            await abrirHoja(hoja.Original);
    }

    private async void CambiarFecha(object? sender, DateChangedEventArgs e)
    {
        await modelo.RefrescarLocalesAsync();
        await modelo.ActualizarHojaAsync();
    }
}
