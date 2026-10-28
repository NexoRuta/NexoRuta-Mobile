using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;

namespace NexoRuta.Mobile;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    private float inicioToqueX;
    private float inicioToqueY;
    private bool siguiendoToque;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Platforms.Android.CanalesPush.Crear();
        RecibirApertura(Intent);
#if VALIDAR_ALMACENAMIENTO
        var fase = Intent?.GetStringExtra("nexoruta.prueba.almacenamiento");
        if (fase is "guardar" or "leer") _ = Diagnostico.PruebaAlmacenamiento.EjecutarAsync(fase);
        else if (fase is not null) _ = Diagnostico.PruebaIntegraciones.EjecutarAsync(fase);
#endif
    }
    protected override void OnNewIntent(Android.Content.Intent? intent)
    {
        base.OnNewIntent(intent);
        RecibirApertura(intent);
    }

    public override bool DispatchTouchEvent(MotionEvent? ev)
    {
        var reconocerDeslizamiento = false;
        var haciaLaDerecha = false;
        if (ev is not null)
        {
            if (ev.ActionMasked == MotionEventActions.Down)
            {
                inicioToqueX = ev.GetX();
                inicioToqueY = ev.GetY();
                siguiendoToque = true;
            }
            else if (ev.ActionMasked is MotionEventActions.Up or MotionEventActions.Cancel && siguiendoToque)
            {
                if (ev.ActionMasked == MotionEventActions.Up)
                {
                    var desplazamientoX = ev.GetX() - inicioToqueX;
                    var desplazamientoY = ev.GetY() - inicioToqueY;
                    var densidad = Resources?.DisplayMetrics?.Density ?? 1f;
                    var zonaBarraInferior = inicioToqueY >= (Resources?.DisplayMetrics?.HeightPixels ?? int.MaxValue) - 90 * densidad;
                    reconocerDeslizamiento = !zonaBarraInferior && Math.Abs(desplazamientoX) >= 70 * densidad
                        && Math.Abs(desplazamientoX) > Math.Abs(desplazamientoY) * 1.4f;
                    haciaLaDerecha = desplazamientoX < 0;
                }
                siguiendoToque = false;
            }
        }

        var resultado = base.DispatchTouchEvent(ev);
        if (reconocerDeslizamiento && PaginaPrincipalActiva() is { } pagina)
            pagina.CambiarPorSwipe(haciaLaDerecha);
        return resultado;
    }

    private static MainPage? PaginaPrincipalActiva()
    {
        var pagina = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Page;
        return pagina is NavigationPage navegacion
            ? navegacion.CurrentPage as MainPage
            : pagina as MainPage;
    }

    private static void RecibirApertura(Android.Content.Intent? intent)
    {
        if (intent is null) return;
        var datos = new Dictionary<string, string>();
        foreach (var clave in new[] { "v", "notificacionId", "operadorId", "accesoId" })
            if (intent.GetStringExtra(clave) is { } valor) datos[clave] = valor;
        if (IPlatformApplication.Current?.Services.GetService<Notificaciones.ControlNotificaciones>() is { } control)
            _ = control.RecibirAsync(datos, abierto: true);
    }
}
