namespace NexoRuta.Mobile;

public enum SeccionPrincipal { Jornada, Avisos, Cuenta }

public sealed class BarraNavegacionRepartidor : ContentView
{
    private readonly Dictionary<SeccionPrincipal, Button> botones = [];
    private readonly ResourceDictionary recursos;
    private readonly bool temaOscuro;
    private SeccionPrincipal activa;
    public event Func<SeccionPrincipal, Task>? Navegar;

    public BarraNavegacionRepartidor() : this(SeccionPrincipal.Jornada) { }

    public BarraNavegacionRepartidor(SeccionPrincipal activa)
    {
        this.recursos = Microsoft.Maui.Controls.Application.Current!.Resources;
        this.temaOscuro = Microsoft.Maui.Controls.Application.Current.RequestedTheme == AppTheme.Dark;
        this.activa = activa;
        var grid = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
        Agregar(SeccionPrincipal.Jornada, "Jornada", "ic_route.png", 0);
        Agregar(SeccionPrincipal.Avisos, "Avisos", "ic_bell.png", 1);
        Agregar(SeccionPrincipal.Cuenta, "Cuenta", "ic_account.png", 2);
        Content = new Border
        {
            StrokeThickness = 0,
            Padding = new Thickness(8, 5, 8, 8),
            BackgroundColor = (Color)recursos[temaOscuro ? "OffBlack" : "White"],
            Content = grid
        };

        void Agregar(SeccionPrincipal seccion, string texto, string icono, int columna)
        {
            var seleccionado = seccion == activa;
            var boton = new Button
            {
                Text = texto,
                ImageSource = icono,
                ContentLayout = new Button.ButtonContentLayout(Button.ButtonContentLayout.ImagePosition.Top, 2),
                FontFamily = seleccionado ? "OpenSansSemibold" : "OpenSansRegular",
                FontSize = 11,
                TextColor = (Color)recursos[seleccionado ? "Primary" : temaOscuro ? "Gray300" : "Gray600"],
                BackgroundColor = Colors.Transparent,
                Padding = new Thickness(4, 2),
                HeightRequest = 56,
                MinimumHeightRequest = 48,
                MinimumWidthRequest = 48,
                AutomationId = $"Navegacion{texto}"
            };
            SemanticProperties.SetDescription(boton, $"{texto}{(seleccionado ? ", sección actual" : "")}");
            SemanticProperties.SetHint(boton, seleccionado ? "Sección actual" : $"Abrir {texto.ToLowerInvariant()}");
            botones.Add(seccion, boton);
            boton.Clicked += async (_, _) =>
            {
                if (Navegar is { } navegar) await navegar(seccion);
            };
            grid.Add(boton, columna, 0);
        }
    }

    public void Seleccionar(SeccionPrincipal seccion)
    {
        if (activa == seccion) return;
        activa = seccion;
        foreach (var (destino, boton) in botones)
        {
            var seleccionado = destino == seccion;
            boton.FontFamily = seleccionado ? "OpenSansSemibold" : "OpenSansRegular";
            boton.TextColor = (Color)recursos[seleccionado ? "Primary" : temaOscuro ? "Gray300" : "Gray600"];
            SemanticProperties.SetDescription(boton, $"{boton.Text}{(seleccionado ? ", sección actual" : "")}");
            SemanticProperties.SetHint(boton, seleccionado ? "Sección actual" : $"Abrir {boton.Text.ToLowerInvariant()}");
        }
    }
}
