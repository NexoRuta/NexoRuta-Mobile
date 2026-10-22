namespace NexoRuta.Mobile.Api.Identidad;
internal sealed class PerfilRepartidorResponse
{
    public Guid AccesoId { get; init; }
    public Guid UsuarioId { get; init; }
    public Guid OperadorId { get; init; }
    public string Nombre { get; init; } = "";
    public bool Activo { get; init; }
    public long Version { get; init; }
}
