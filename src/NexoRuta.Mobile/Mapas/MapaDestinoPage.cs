using NexoRuta.Mobile.Application.Mapas;
using NexoRuta.Mobile.Core.Jornadas;
using NexoRuta.Mobile.Core.Rutas;
namespace NexoRuta.Mobile.Mapas;

public sealed class MapaDestinoPage : ContentPage
{
    private readonly IMapaDestino mapa;
    private readonly ServicioPreparacionMapa preparacion;
    private readonly CuentaMovil cuenta;
    private readonly HojaRuta hoja;
    private readonly CancellationTokenSource cancelacion = new();
    private readonly Label estado = new();
    private Task? descargaActual;
    public MapaDestinoPage(IMapaDestino mapa, ServicioPreparacionMapa preparacion, CuentaMovil cuenta, HojaRuta hoja, ParadaRuta parada)
    {
        this.mapa = mapa; this.preparacion = preparacion; this.cuenta = cuenta; this.hoja = hoja; Title = "Destino";
        var progreso = new ProgressBar { IsVisible = false };
        var descargar = new Button { Text = "Preparar recorrido", IsEnabled = preparacion.Disponible, IsVisible = preparacion.Disponible };
        var cancelar = new Button { Text = "Cancelar descarga", IsVisible = false };
        CancellationTokenSource? intento = null;
        cancelar.Clicked += (_, _) => { cancelar.IsEnabled = false; estado.Text = "Cancelando descarga…"; intento?.Cancel(); };
        descargar.Clicked += async (_, _) =>
        {
            if (descargaActual is { IsCompleted: false }) return;
            descargaActual = Descargar(); await descargaActual;
        };
        async Task Descargar()
        {
            using var actual = CancellationTokenSource.CreateLinkedTokenSource(cancelacion.Token);
            intento = actual;
            descargar.IsEnabled = false; progreso.IsVisible = cancelar.IsVisible = cancelar.IsEnabled = true;
            estado.Text = "Preparando tu recorrido…";
            try
            {
                var lista = await preparacion.DescargarAsync(cuenta, hoja, new Progress<ProgresoMapa>(x =>
                {
                    if (!ReferenceEquals(intento, actual) || actual.IsCancellationRequested) return;
                    progreso.Progress = x.Fraccion;
                    estado.Text = $"Preparando recorrido: {x.Fraccion:P0} · {Tamano(x.Bytes)} descargados";
                }), actual.Token);
                estado.Text = $"{lista.Mensaje} Tamaño: {Tamano(lista.Bytes)}.";
            }
            catch (OperationCanceledException) { estado.Text = "Preparación cancelada. Podés volver a intentarlo."; }
            catch (Exception) { estado.Text = "No pudimos preparar el recorrido. Verificá tu conexión y el espacio del teléfono."; }
            finally { intento = null; descargar.IsEnabled = preparacion.Disponible; progreso.IsVisible = cancelar.IsVisible = false; }
        }
        var grid = new Grid { Padding = 16, RowSpacing = 12, RowDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto) } };
        grid.Add(new Label { Text = parada.Direccion, FontSize = 20, FontAttributes = FontAttributes.Bold }, 0, 0);
        grid.Add(mapa.Vista, 0, 1); grid.Add(estado, 0, 2); grid.Add(progreso, 0, 3); grid.Add(descargar, 0, 4); grid.Add(cancelar, 0, 5);
        Content = grid; mapa.Mostrar(parada);
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            var lista = await preparacion.ConsultarAsync(cuenta, hoja, cancelacion.Token);
            estado.Text = lista.Bytes > 0 ? $"{lista.Mensaje} Tamaño: {Tamano(lista.Bytes)}." : lista.Mensaje;
        }
        catch (OperationCanceledException) { }
        catch { estado.Text = "No pudimos comprobar el recorrido. La dirección sigue disponible."; }
    }
    protected override async void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
        base.OnNavigatedFrom(args); cancelacion.Cancel();
        try { if (descargaActual is not null) await descargaActual; }
        finally { mapa.Dispose(); cancelacion.Dispose(); }
    }
    private static string Tamano(long bytes) => $"{bytes / 1048576d:F1} MiB";
}
