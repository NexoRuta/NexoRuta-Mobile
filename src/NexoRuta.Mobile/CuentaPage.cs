using NexoRuta.Mobile.Presentacion;
namespace NexoRuta.Mobile;

public sealed class CuentaPage : ContentView
{
    public CuentaPage(PrincipalViewModel modelo, Func<Task<bool>> confirmarCierre)
    {
        BindingContext = modelo;
        var titulo = new Label { Text = "Tu cuenta", Style = (Style)RecursosGlobales["PageTitle"] };
        var correo = new Label { Text = string.IsNullOrWhiteSpace(modelo.Email) ? "Cuenta de repartidor" : modelo.Email, FontSize = 16 };
        var activar = new Button { Text = "Activar notificaciones", Command = modelo.ActivarAvisos, Style = (Style)RecursosGlobales["SecondaryButton"] };
        activar.SetBinding(IsVisibleProperty, nameof(modelo.PuedeActivarAvisos));
        var estado = new Label { FontSize = 14 }; estado.SetBinding(Label.TextProperty, nameof(modelo.EstadoAvisos));
        var reingresar = new Button { Text = "Volver a ingresar", Command = modelo.Reingresar, Style = (Style)RecursosGlobales["SecondaryButton"] };
        var cerrar = new Button { Text = "Cerrar sesión", Style = (Style)RecursosGlobales["DangerButton"] };
        cerrar.Clicked += async (_, _) =>
        {
            if (await confirmarCierre())
                modelo.CerrarSesion.Execute(null);
        };
        var mensaje = new Label { FontSize = 14 }; mensaje.SetBinding(Label.TextProperty, nameof(modelo.Mensaje));
        var contenido = new ScrollView { Content = new VerticalStackLayout { Padding = 24, Spacing = 20, Children =
        {
            titulo, new Border { Style = (Style)RecursosGlobales["Card"], Content = new VerticalStackLayout { Spacing = 12, Children = { new Image { Source = "ic_account.png", HeightRequest = 36, WidthRequest = 36, HorizontalOptions = LayoutOptions.Start }, correo } } },
            new Border { Style = (Style)RecursosGlobales["Card"], Content = new VerticalStackLayout { Spacing = 12, Children = { new Label { Text = "Avisos de tu ruta", FontSize = 18, FontAttributes = FontAttributes.Bold }, estado, activar } } },
            mensaje, reingresar, cerrar,
            new Label { Text = $"NexoRuta Repartidor · {AppInfo.Current.VersionString}", FontSize = 12, TextColor = Colors.SlateGray, HorizontalTextAlignment = TextAlignment.Center }
        } } };
        Content = contenido;
    }
    private static ResourceDictionary RecursosGlobales => Microsoft.Maui.Controls.Application.Current!.Resources;
}
