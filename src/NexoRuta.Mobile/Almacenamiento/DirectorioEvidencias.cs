namespace NexoRuta.Mobile.Almacenamiento;

public static class DirectorioEvidencias
{
    public static string Crear(string nombre = "evidencias")
    {
        var ruta = Path.Combine(FileSystem.AppDataDirectory, nombre);
        Directory.CreateDirectory(ruta);
#if IOS || MACCATALYST
        using var url = Foundation.NSUrl.FromFilename(ruta);
        using var excluir = Foundation.NSNumber.FromBoolean(true);
        if (!url.SetResource(Foundation.NSUrl.IsExcludedFromBackupKey, excluir, out var error))
        {
            error?.Dispose();
            throw new IOException("No se pudo excluir el almacenamiento local del backup.");
        }
#endif
        return ruta;
    }
}
