namespace NexoRuta.Mobile.Api.Reparto;
public sealed class ConfirmacionOperacion
{
    public Guid Id { get; init; }
    public long Secuencia { get; init; }
    public string Resultado { get; init; } = "";
    public string? Detalle { get; init; }
    public long? VersionEnvio { get; init; }
    public long VersionRuta { get; init; }
    public Dictionary<Guid, long> VersionesEnvio { get; init; } = [];
    public Dictionary<Guid, string> EstadosEnvio { get; init; } = [];
}
