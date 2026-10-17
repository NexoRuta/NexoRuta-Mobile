using NexoRuta.Mobile.Api.Identidad;

namespace NexoRuta.Mobile.Almacenamiento;

public sealed class AlmacenSesionProtegida : IAlmacenSesionMovil
{
    private const string Clave = "nexoruta.mobile.sesion.v1";

    public async Task<EstadoSesionMovil?> LeerAsync()
    {
        var json = await SecureStorage.Default.GetAsync(Clave);
        return json is null ? null : SerializadorSesionMovil.Deserializar(json);
    }

    public Task GuardarAsync(EstadoSesionMovil estado) =>
        SecureStorage.Default.SetAsync(Clave, SerializadorSesionMovil.Serializar(estado));
}
