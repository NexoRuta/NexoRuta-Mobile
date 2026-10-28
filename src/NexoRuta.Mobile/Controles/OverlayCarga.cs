namespace NexoRuta.Mobile.Controles;

public sealed class OverlayCarga : Grid
{
    private readonly Grid capa;
    private readonly Border tarjeta;

    public static readonly BindableProperty IsBusyProperty = BindableProperty.Create(
        nameof(IsBusy), typeof(bool), typeof(OverlayCarga), false,
        propertyChanged: (bindable, _, _) => ((OverlayCarga)bindable).ActualizarEstado());

    public bool IsBusy { get => (bool)GetValue(IsBusyProperty); set => SetValue(IsBusyProperty, value); }

    public OverlayCarga()
    {
        capa = new Grid
        {
            IsVisible = false,
            InputTransparent = false,
            Style = Estilo("LoadingScrim"),
            ZIndex = 1000
        };

        var indicador = new ActivityIndicator
        {
            IsRunning = true,
            Style = Estilo("LoadingIndicator"),
            WidthRequest = 28,
            HeightRequest = 28,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
        tarjeta = new Border
        {
            Content = indicador,
            WidthRequest = 68,
            HeightRequest = 68,
            Padding = 18,
            Style = Estilo("LoadingCard"),
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 18 },
            Shadow = new Shadow { Brush = Colors.Black, Opacity = 0.14f, Radius = 18, Offset = new Point(0, 5) },
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            MaximumWidthRequest = 330
        };
        capa.Children.Add(tarjeta);
        Children.Add(capa);
        SemanticProperties.SetDescription(capa, "Cargando");
    }

    private static Style? Estilo(string nombre) =>
        Microsoft.Maui.Controls.Application.Current?.Resources.TryGetValue(nombre, out var recurso) == true ? recurso as Style : null;

    protected override void OnChildAdded(Element child)
    {
        base.OnChildAdded(child);
        if (child != capa) capa.ZIndex = 1000;
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        ActualizarEstado();
    }

    private void ActualizarEstado()
    {
        capa.IsVisible = IsBusy;
        if (IsBusy) capa.ZIndex = 1000;
        AplicarDesenfoque(IsBusy);
    }

    private void AplicarDesenfoque(bool activo)
    {
#if ANDROID
        if (Handler?.PlatformView is not Android.Views.ViewGroup) return;
        foreach (var hijo in Children)
        {
            if (hijo == capa || hijo.Handler?.PlatformView is not Android.Views.View vista) continue;
            if (OperatingSystem.IsAndroidVersionAtLeast(31))
            {
                var efecto = activo
                    ? Android.Graphics.RenderEffect.CreateBlurEffect(12f, 12f, Android.Graphics.Shader.TileMode.Clamp!)
                    : null;
                vista.SetRenderEffect(efecto);
            }
        }
#endif
    }
}
