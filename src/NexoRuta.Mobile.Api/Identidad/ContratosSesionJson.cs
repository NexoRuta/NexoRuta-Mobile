using System.Text.Json.Serialization;
using NexoRuta.Mobile.Core.Jornadas;

namespace NexoRuta.Mobile.Api.Identidad;

internal sealed class IngresoRequest
{
    public required string Email { get; init; }
    public required string Password { get; init; }
}

internal sealed class RenovacionRequest
{
    public required string RefreshToken { get; init; }
}

internal sealed class UsuarioActualResponse
{
    public Guid UsuarioId { get; init; }
    public Guid? AccesoId { get; init; }
    public Guid? OperadorId { get; init; }
    public Guid? ComercioId { get; init; }
    public Guid? RolId { get; init; }
    public string? Tipo { get; init; }

    public CuentaMovil ObtenerCuenta()
    {
        if (UsuarioId == Guid.Empty || AccesoId is null || AccesoId == Guid.Empty
            || OperadorId is null || OperadorId == Guid.Empty || ComercioId is not null
            || RolId is null || RolId == Guid.Empty || Tipo != "Operador")
            throw new SesionMovilException(ErrorSesionMovil.RespuestaInvalida);
        return new(UsuarioId, AccesoId.Value, OperadorId.Value);
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(IngresoRequest))]
[JsonSerializable(typeof(RenovacionRequest))]
[JsonSerializable(typeof(TokensSesionMovil))]
[JsonSerializable(typeof(EstadoSesionMovil))]
[JsonSerializable(typeof(UsuarioActualResponse))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(PerfilRepartidorResponse))]
internal partial class ContratosSesionJson : JsonSerializerContext;
