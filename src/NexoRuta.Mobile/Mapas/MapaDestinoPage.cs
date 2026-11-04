using Microsoft.Maui.Networking;
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
    private readonly CancellationTokenSource vigilarMapa = new();
    private readonly Label estadoMapa = new();
    private readonly Label estadoOffline = new();
    private readonly Label estadoCoordenadas = new();
    private CancellationTokenSource? esperaCarga;
    private Task? descargaActual;
    private bool mapaListo;
    private bool paqueteVerificado;
    private readonly ParadaRuta[] ubicadas;
    private readonly bool tienePuntos;

    public MapaDestinoPage(IMapaDestino mapa, ServicioPreparacionMapa preparacion, CuentaMovil cuenta,
        HojaRuta hoja, IReadOnlyCollection<ParadaRuta> paradas)
    {
        this.mapa = mapa; this.preparacion = preparacion; this.cuenta = cuenta; this.hoja = hoja;
        Title = paradas.Count == 1 ? "Destino" : "Mapa del recorrido";
        estadoMapa.Text = mapa.EstadoInicial;
        mapa.EstadoCambiado += EstadoMapbox;
        ubicadas = paradas.Where(ConCoordenadas).OrderBy(p => p.Orden).ToArray();
        tienePuntos = ubicadas.Length > 0;
        estadoCoordenadas.Text = ubicadas.Length == 0
            ? paradas.Count == 0 ? "No hay paradas activas con coordenadas para mostrar. Actualizá el recorrido cuando el depósito lo corrija."
                : "Este destino no tiene coordenadas válidas; no se colocó un marcador."
            : ubicadas.Length != paradas.Count
                ? $"Se muestran {ubicadas.Length} de {paradas.Count} paradas; alguna no tiene coordenadas válidas."
                : "";
        estadoCoordenadas.IsVisible = estadoCoordenadas.Text.Length > 0;
        if (tienePuntos) mapa.Mostrar(ubicadas);
        var progreso = new ProgressBar { IsVisible = false };
        Style? estiloSecundario = null;
        if (global::Microsoft.Maui.Controls.Application.Current?.Resources.TryGetValue("SecondaryButton", out var recurso) == true)
            estiloSecundario = recurso as Style;
        var descargar = new Button { Text = "Preparar mapa sin conexión", Style = estiloSecundario as Style,
            IsEnabled = preparacion.Disponible && tienePuntos, IsVisible = preparacion.Disponible && tienePuntos };
        var cancelar = new Button { Text = "Cancelar descarga", IsVisible = false };
        CancellationTokenSource? intento = null;
            cancelar.Clicked += (_, _) => { cancelar.IsEnabled = false; estadoOffline.Text = "Cancelando descarga…"; estadoOffline.IsVisible = true; intento?.Cancel(); };
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
            estadoOffline.Text = "Preparando el mapa sin conexión…";
            estadoOffline.IsVisible = true;
            try
            {
                var lista = await preparacion.DescargarAsync(cuenta, hoja, ubicadas, new Progress<ProgresoMapa>(x =>
                {
                    if (!ReferenceEquals(intento, actual) || actual.IsCancellationRequested) return;
                    progreso.Progress = x.Fraccion;
                    estadoOffline.Text = $"Preparando mapa sin conexión: {x.Fraccion:P0} · {Tamano(x.Bytes)} descargados";
                }), actual.Token);
                estadoOffline.Text = lista.Preparada
                    ? $"Disponible sin conexión. Estilo y zona verificados · {Tamano(lista.Bytes)}."
                    : "El paquete no quedó verificado; el mapa online requiere conexión.";
            }
            catch (OperationCanceledException) { estadoOffline.Text = "Preparación cancelada. Podés volver a intentarlo."; estadoOffline.IsVisible = true; }
            catch (InvalidOperationException) { estadoOffline.Text = "La descarga sin conexión no está disponible en este equipo. El mapa online sigue disponible."; estadoOffline.IsVisible = true; }
            catch (IOException ex) { estadoOffline.Text = $"No se pudo preparar el paquete: {ex.Message} El mapa online requiere conexión."; estadoOffline.IsVisible = true; }
            catch (Exception) { estadoOffline.Text = "No se pudo verificar el paquete sin conexión. El mapa online sigue disponible con Internet."; estadoOffline.IsVisible = true; }
            finally { intento = null; descargar.IsEnabled = preparacion.Disponible && tienePuntos; progreso.IsVisible = cancelar.IsVisible = false; }
        }

        var grid = new Grid { Padding = 16, RowSpacing = 10, RowDefinitions =
        {
            new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto),
            new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto)
        } };
        var parada = paradas.Count == 1 ? paradas.First() : null;
        var encabezado = new Label
        {
            Text = parada?.Destinatario ?? "Mapa del recorrido",
            FontSize = 20,
            FontAttributes = FontAttributes.Bold
        };
        var direccion = new Label
        {
            Text = parada?.Direccion ?? (tienePuntos ? $"{ubicadas.Length} paradas con punto · {hoja.Fecha:dd/MM/yyyy}" : $"Sin paradas activas · {hoja.Fecha:dd/MM/yyyy}"),
            FontSize = 16,
            LineBreakMode = LineBreakMode.WordWrap
        };
        var lienzo = tienePuntos ? (View)mapa.Vista : new Border
        {
            StrokeThickness = 0,
            BackgroundColor = Color.FromArgb("#F1F4F8"),
            Content = new VerticalStackLayout
            {
                HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, Spacing = 8,
                Children = { new Image { Source = "ic_route.png", WidthRequest = 40, HeightRequest = 40, Opacity = 0.7 },
                    new Label { Text = paradas.Count == 0 ? "Recorrido sin paradas activas" : "Destino sin punto válido", HorizontalTextAlignment = TextAlignment.Center, FontAttributes = FontAttributes.Bold },
                    new Label { Text = "No hay un área de mapa que descargar.", HorizontalTextAlignment = TextAlignment.Center } }
            }
        };
        grid.Add(encabezado, 0, 0); grid.Add(direccion, 0, 1); grid.Add(lienzo, 0, 2);
        if (tienePuntos) grid.Add(estadoMapa, 0, 3);
        grid.Add(estadoCoordenadas, 0, 4);
        estadoOffline.IsVisible = false;
        grid.Add(estadoOffline, 0, 5); grid.Add(progreso, 0, 6); grid.Add(descargar, 0, 7); grid.Add(cancelar, 0, 8);
        Content = grid;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!tienePuntos) return;
        Connectivity.Current.ConnectivityChanged += CambioConectividad;
        if (!string.IsNullOrWhiteSpace(mapa.EstadoInicial))
        {
            if (Connectivity.Current.NetworkAccess == NetworkAccess.Internet) IniciarEsperaCarga();
            else estadoMapa.Text = "Sin conexión a Internet. El mapa online se cargará al recuperar conexión.";
        }
        estadoOffline.Text = "Comprobando el paquete sin conexión…"; estadoOffline.IsVisible = true;
        try
        {
            var local = await preparacion.ConsultarAsync(cuenta, hoja, ubicadas, cancelacion.Token);
            paqueteVerificado = local.Preparada;
            estadoOffline.Text = local.Preparada ? $"Disponible sin conexión · {Tamano(local.Bytes)}."
                : local.Mensaje;
        }
        catch (OperationCanceledException) { }
        catch (Exception) { estadoOffline.Text = "No pudimos comprobar el paquete offline; no se considera disponible."; }
    }

    protected override async void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
        base.OnNavigatedFrom(args); cancelacion.Cancel(); vigilarMapa.Cancel(); esperaCarga?.Cancel();
        Connectivity.Current.ConnectivityChanged -= CambioConectividad;
        mapa.EstadoCambiado -= EstadoMapbox;
        try { if (descargaActual is not null) await descargaActual; }
        finally
        {
            mapa.Dispose(); esperaCarga?.Dispose(); vigilarMapa.Dispose(); cancelacion.Dispose();
        }
    }

    private void EstadoMapbox(string estado) => MainThread.BeginInvokeOnMainThread(() =>
    {
        if (cancelacion.IsCancellationRequested) return;
        mapaListo = estado == "listo";
        if (mapaListo)
        {
            esperaCarga?.Cancel();
            estadoMapa.Text = Connectivity.Current.NetworkAccess == NetworkAccess.Internet ? "Mapa online disponible."
                : paqueteVerificado ? "Mapa disponible sin conexión; paquete local verificado."
                : "Mapa visible sin red, pero no hay un paquete offline verificado.";
        }
        else estadoMapa.Text = "Mapbox no pudo cargar el mapa. Revisá la conexión o la configuración; la dirección sigue disponible.";
    });

    private void CambioConectividad(object? sender, ConnectivityChangedEventArgs args) => MainThread.BeginInvokeOnMainThread(() =>
    {
        if (cancelacion.IsCancellationRequested || mapaListo) return;
        if (args.NetworkAccess == NetworkAccess.Internet) { estadoMapa.Text = mapa.EstadoInicial; IniciarEsperaCarga(); }
        else { esperaCarga?.Cancel(); estadoMapa.Text = "Sin conexión a Internet. El mapa online se cargará al recuperar conexión."; }
    });

    private void IniciarEsperaCarga()
    {
        esperaCarga?.Cancel(); esperaCarga?.Dispose();
        esperaCarga = CancellationTokenSource.CreateLinkedTokenSource(vigilarMapa.Token);
        _ = VigilarCargaAsync(esperaCarga.Token);
    }

    private async Task VigilarCargaAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(20), ct); }
        catch (OperationCanceledException) { return; }
        if (!mapaListo && Connectivity.Current.NetworkAccess == NetworkAccess.Internet) EstadoMapbox("error");
    }

    private static bool ConCoordenadas(ParadaRuta parada) => double.IsFinite(parada.Latitud)
        && parada.Latitud is >= -90 and <= 90 && double.IsFinite(parada.Longitud)
        && parada.Longitud is >= -180 and <= 180;

    private static string Tamano(long bytes) => $"{bytes / 1048576d:F1} MiB";
}
