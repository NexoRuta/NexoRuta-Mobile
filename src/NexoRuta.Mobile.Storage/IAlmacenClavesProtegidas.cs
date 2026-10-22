namespace NexoRuta.Mobile.Storage;

public interface IAlmacenClavesProtegidas
{
    Task<string?> ObtenerAsync(string nombre);
    Task GuardarAsync(string nombre, string valor);
}
