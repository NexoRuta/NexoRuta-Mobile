using System.Text.Json;

namespace NexoRuta.Mobile.Api.Identidad;

public static class SerializadorSesionMovil
{
    public static string Serializar(EstadoSesionMovil estado)
    {
        Validar(estado);
        return JsonSerializer.Serialize(estado, ContratosSesionJson.Default.EstadoSesionMovil);
    }

    public static EstadoSesionMovil Deserializar(string json)
    {
        try
        {
            var estado = JsonSerializer.Deserialize(json, ContratosSesionJson.Default.EstadoSesionMovil);
            Validar(estado);
            return estado!;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            throw new SesionMovilException(ErrorSesionMovil.EstadoLocalInvalido);
        }
    }

    internal static void Validar(EstadoSesionMovil? estado)
    {
        if (estado is null || estado.Version != 1 || estado.Cuenta is null
            || estado.Cuenta.UsuarioId == Guid.Empty || estado.Cuenta.AccesoId == Guid.Empty
            || estado.Cuenta.OperadorId == Guid.Empty
            || !Uri.TryCreate(estado.Api, UriKind.Absolute, out var api) || api.Scheme != Uri.UriSchemeHttps
            || estado.Tokens is not null && !estado.Tokens.EsValido)
            throw new SesionMovilException(ErrorSesionMovil.EstadoLocalInvalido);
    }
}
