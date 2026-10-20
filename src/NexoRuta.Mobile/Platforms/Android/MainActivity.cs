using Android.App;
using Android.Content.PM;
using Android.OS;

namespace NexoRuta.Mobile;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Window?.AddFlags(Android.Views.WindowManagerFlags.Secure);
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
