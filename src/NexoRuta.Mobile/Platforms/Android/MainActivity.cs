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
#if VALIDAR_ALMACENAMIENTO
        var fase = Intent?.GetStringExtra("nexoruta.prueba.almacenamiento");
        if (fase is "guardar" or "leer") _ = Diagnostico.PruebaAlmacenamiento.EjecutarAsync(fase);
        else if (fase is not null) _ = Diagnostico.PruebaIntegraciones.EjecutarAsync(fase);
#endif
    }
}
