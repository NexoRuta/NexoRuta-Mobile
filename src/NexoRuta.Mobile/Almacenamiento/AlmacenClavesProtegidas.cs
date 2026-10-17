using NexoRuta.Mobile.Storage;

namespace NexoRuta.Mobile.Almacenamiento;

public sealed class AlmacenClavesProtegidas : IAlmacenClavesProtegidas
{
    public Task<string?> ObtenerAsync(string nombre) => SecureStorage.Default.GetAsync(nombre);
    public Task GuardarAsync(string nombre, string valor) => SecureStorage.Default.SetAsync(nombre, valor);
}
