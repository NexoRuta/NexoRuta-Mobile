using NexoRuta.Mobile.Core.Rutas;
namespace NexoRuta.Mobile.Mapas;
public sealed class MapaDestinoPage : ContentPage
{
    private readonly IMapaDestino mapa;
    private readonly CancellationTokenSource cancelacion = new();
    public MapaDestinoPage(IMapaDestino mapa, HojaRuta hoja, ParadaRuta parada)
    {
        this.mapa = mapa; Title = "Destino";
        var estado = new Label { Text = parada.Resumen };
        var progreso = new ProgressBar { IsVisible = false };
        var descargar = new Button { Text = "Descargar zona para usar sin conexión", IsEnabled = mapa.Disponible };
        descargar.Clicked += async (_, _) =>
        {
            descargar.IsEnabled = false; progreso.IsVisible = true; estado.Text = "Descargando mapa de la zona…";
            try
            {
                await mapa.DescargarZonaAsync(hoja, new Progress<double>(x => progreso.Progress = x), cancelacion.Token);
                estado.Text = "Descarga completada. El mapa de la zona está disponible sin conexión.";
            }
            catch (OperationCanceledException) { estado.Text = "La descarga se interrumpió."; }
            catch (Exception) { estado.Text = "No se completó la descarga. Verificá conexión y espacio disponible."; }
            finally { descargar.IsEnabled = mapa.Disponible; progreso.IsVisible = false; }
        };
        var grid = new Grid { Padding = 16, RowSpacing = 12, RowDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto) } };
        grid.Add(new Label { Text = parada.Direccion, FontSize = 20, FontAttributes = FontAttributes.Bold }, 0, 0);
        grid.Add(mapa.Vista, 0, 1); grid.Add(estado, 0, 2); grid.Add(progreso, 0, 3); grid.Add(descargar, 0, 4);
        Content = grid; mapa.Mostrar(parada);
    }
    protected override void OnDisappearing()
    {
        base.OnDisappearing(); cancelacion.Cancel();
    }
}
