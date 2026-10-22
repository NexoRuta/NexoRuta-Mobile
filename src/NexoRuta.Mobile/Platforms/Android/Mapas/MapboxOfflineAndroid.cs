using Android.Runtime;

namespace NexoRuta.Mobile.Mapas;

// El facade Java se compila contra el SDK real; solo sus métodos escalares cruzan JNI.
internal static class MapboxOfflineAndroid
{
    private const string Tipo = "uy.nexoruta.offline.MapboxOffline";
    // Los hilos de Task.Run no heredan el cargador Java de la Activity.
    private static Java.Lang.Class Clase() => Java.Lang.Class.ForName(Tipo, true,
        global::Android.App.Application.Context.ClassLoader)!;
    internal static void Configure(string token) => UnaCadena("configure", token);
    internal static void Cancel(string id) => UnaCadena("cancel", id);
    internal static void Remove(string id) => UnaCadena("remove", id);
    internal static void Forget(string id) => UnaCadena("forget", id);
    internal static string Progress(string id) => Consultar("progress", id);
    internal static string Inspect(string id, string estilo) => Consultar("inspect", id, estilo);
    internal static void Start(string id, string estilo, double latMin, double latMax, double lonMin, double lonMax, int zoomMin, int zoomMax)
    {
        using var tipo = Clase();
        var nId = JNIEnv.NewString(id); var nEstilo = JNIEnv.NewString(estilo);
        try
        {
            var metodo = JNIEnv.GetStaticMethodID(tipo.Handle, "start", "(Ljava/lang/String;Ljava/lang/String;DDDDII)V");
            JNIEnv.CallStaticVoidMethod(tipo.Handle, metodo, [new(nId), new(nEstilo), new(latMin), new(latMax), new(lonMin), new(lonMax), new(zoomMin), new(zoomMax)]);
        }
        finally { JNIEnv.DeleteLocalRef(nId); JNIEnv.DeleteLocalRef(nEstilo); }
    }
    private static void UnaCadena(string nombre, string valor)
    {
        using var tipo = Clase();
        var nValor = JNIEnv.NewString(valor);
        try
        {
            var metodo = JNIEnv.GetStaticMethodID(tipo.Handle, nombre, "(Ljava/lang/String;)V");
            JNIEnv.CallStaticVoidMethod(tipo.Handle, metodo, [new(nValor)]);
        }
        finally { JNIEnv.DeleteLocalRef(nValor); }
    }
    private static string Consultar(string nombre, string id, string? estilo = null)
    {
        using var tipo = Clase();
        var nId = JNIEnv.NewString(id); var nEstilo = estilo is null ? IntPtr.Zero : JNIEnv.NewString(estilo);
        try
        {
            var firma = estilo is null ? "(Ljava/lang/String;)Ljava/lang/String;" : "(Ljava/lang/String;Ljava/lang/String;)Ljava/lang/String;";
            var metodo = JNIEnv.GetStaticMethodID(tipo.Handle, nombre, firma);
            var resultado = JNIEnv.CallStaticObjectMethod(tipo.Handle, metodo, estilo is null ? [new(nId)] : [new(nId), new(nEstilo)]);
            return JNIEnv.GetString(resultado, JniHandleOwnership.TransferLocalRef)
                ?? throw new InvalidDataException("El mapa no devolvió su estado.");
        }
        finally { JNIEnv.DeleteLocalRef(nId); if (nEstilo != IntPtr.Zero) JNIEnv.DeleteLocalRef(nEstilo); }
    }
}
