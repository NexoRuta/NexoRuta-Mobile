using System.Reflection;
namespace NexoRuta.Mobile;

public sealed class ConfiguracionAplicacion
{
    public Uri? Api { get; }
    public string? MapboxToken { get; }
    public ConfiguracionAplicacion()
    {
        var valores = typeof(ConfiguracionAplicacion).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToArray();
        var origen = valores.SingleOrDefault(x => x.Key == "NexoRutaApiOrigin")?.Value;
        if (!string.IsNullOrWhiteSpace(origen))
        {
            if (!Uri.TryCreate(origen, UriKind.Absolute, out var api) || api.Scheme != "https" || api.AbsolutePath != "/"
                || api.UserInfo.Length != 0 || api.Query.Length != 0 || api.Fragment.Length != 0)
                throw new InvalidOperationException("NexoRutaApiOrigin debe ser un origen HTTPS.");
            Api = api;
        }
#if NEXORUTA_LOCAL_HTTPS
        if (Api?.Host != "localhost") throw new InvalidOperationException("La CA de desarrollo solo se admite para una API en https://localhost.");
#endif
        MapboxToken = valores.SingleOrDefault(x => x.Key == "NexoRutaMapboxPublicToken")?.Value;
        if (!string.IsNullOrEmpty(MapboxToken) && !MapboxToken.StartsWith("pk.", StringComparison.Ordinal))
            throw new InvalidOperationException("Mapbox requiere un token público.");
    }
}
