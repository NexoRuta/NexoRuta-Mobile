using NexoRuta.Mobile.Api.Identidad;
using System.Security.Cryptography;
using System.Text;

namespace NexoRuta.Mobile.Almacenamiento;

public sealed class AlmacenSesionProtegida : IAlmacenSesionMovil
{
    private const string Prefijo = "nexoruta.mobile.sesion.v2.";

    public async Task<EstadoSesionMovil?> LeerAsync(string api)
    {
        var json = await SecureStorage.Default.GetAsync(Clave(api));
        return json is null ? null : SerializadorSesionMovil.Deserializar(json);
    }

    public Task GuardarAsync(EstadoSesionMovil estado) =>
        SecureStorage.Default.SetAsync(Clave(estado.Api), SerializadorSesionMovil.Serializar(estado));
    public Task EliminarAsync(string api)
    {
        SecureStorage.Default.Remove(Clave(api));
        return Task.CompletedTask;
    }

    private static string Clave(string api) => Prefijo + Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(api))).ToLowerInvariant();
}
