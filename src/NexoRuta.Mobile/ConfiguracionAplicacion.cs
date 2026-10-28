using System.Reflection;
namespace NexoRuta.Mobile;

public sealed class ConfiguracionAplicacion
{
    public Uri? Api { get; }
    public string? MapboxToken { get; }
    public string BackendTarget { get; }
    public ConfiguracionAplicacion()
    {
        var valores = typeof(ConfiguracionAplicacion).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToArray();
        BackendTarget = valores.SingleOrDefault(x => x.Key == "NexoRutaBackendTarget")?.Value ?? "";
        var origen = valores.SingleOrDefault(x => x.Key == "NexoRutaApiOrigin")?.Value;
        if (BackendTarget is not ("Docker" or "GcpDemo"))
            throw new InvalidOperationException("NexoRutaBackendTarget debe ser Docker o GcpDemo.");
        if (!string.IsNullOrWhiteSpace(origen))
        {
            if (!Uri.TryCreate(origen, UriKind.Absolute, out var api) || api.Scheme != "https" || api.AbsolutePath != "/"
                || api.UserInfo.Length != 0 || api.Query.Length != 0 || api.Fragment.Length != 0)
                throw new InvalidOperationException("NexoRutaApiOrigin debe ser un origen HTTPS.");
            if (BackendTarget == "Docker" && api != new Uri("https://localhost:5843/"))
                throw new InvalidOperationException("El destino Docker usa https://localhost:5843/.");
            if (BackendTarget == "GcpDemo" && api.IsLoopback)
                throw new InvalidOperationException("El destino GcpDemo requiere el origen HTTPS público de la VM.");
            Api = api;
        }
        else throw new InvalidOperationException("No se configuró el origen HTTPS para el backend seleccionado.");
#if NEXORUTA_LOCAL_HTTPS
        if (BackendTarget != "Docker" || Api.Host != "localhost") throw new InvalidOperationException("La CA de desarrollo solo se admite para Docker en localhost.");
#endif
        MapboxToken = valores.SingleOrDefault(x => x.Key == "NexoRutaMapboxPublicToken")?.Value;
        if (!string.IsNullOrEmpty(MapboxToken) && !MapboxToken.StartsWith("pk.", StringComparison.Ordinal))
            throw new InvalidOperationException("Mapbox requiere un token público.");
    }
}
