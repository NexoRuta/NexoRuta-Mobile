namespace NexoRuta.Mobile.Application.Notificaciones;

public interface IRegistroPush
{
    string? MotivoNoDisponible { get; }
    Task<bool> SolicitarPermisoAsync();
    Task<bool> PermitidoAsync();
    Task RegistrarAsync();
}
