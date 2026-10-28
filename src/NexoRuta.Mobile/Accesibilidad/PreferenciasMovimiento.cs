namespace NexoRuta.Mobile.Accesibilidad;

internal static class PreferenciasMovimiento
{
    public static bool Reducido
    {
        get
        {
#if ANDROID
            var resolver = Android.App.Application.Context.ContentResolver!;
            return new[] { "animator_duration_scale", "transition_animation_scale", "window_animation_scale" }
                .Any(nombre => Android.Provider.Settings.Global.GetFloat(resolver, nombre, 1f) <= 0f);
#elif IOS || MACCATALYST
            return UIKit.UIAccessibility.IsReduceMotionEnabled;
#else
            return false;
#endif
        }
    }
}
