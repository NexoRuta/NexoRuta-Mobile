namespace NexoRuta.Mobile.Api.Identidad;

public interface IAlmacenSesionMovil
{
    Task<EstadoSesionMovil?> LeerAsync(string api);
    // Guardar todo el estado protegido en una sola escritura; nunca Preferences ni archivos planos.
    Task GuardarAsync(EstadoSesionMovil estado);
    Task EliminarAsync(string api);
}
