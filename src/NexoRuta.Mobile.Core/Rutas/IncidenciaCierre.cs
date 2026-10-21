namespace NexoRuta.Mobile.Core.Rutas;

public sealed record IncidenciaCierre(Guid OperacionId, Guid EnvioId, Guid BultoId, string Codigo,
    long VersionBulto, DateTimeOffset OcurridaEnUtc, DateTimeOffset RecibidaEnUtc);
