namespace NexoRuta.Mobile.Api.Reparto;
public sealed class EvidenciaRequest
{
    public Guid EnvioId { get; init; }
    public string Tipo { get; init; } = "";
    public byte[] Contenido { get; init; } = [];
}
