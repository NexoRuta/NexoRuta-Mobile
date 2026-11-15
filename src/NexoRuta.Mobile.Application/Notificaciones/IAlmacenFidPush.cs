namespace NexoRuta.Mobile.Application.Notificaciones;

public interface IAlmacenFidPush
{
    Task<string?> LeerAsync();
    Task GuardarAsync(string fid);
}
