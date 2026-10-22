using NexoRuta.Mobile.Core.Jornadas;
using System.Text.Json.Serialization;

namespace NexoRuta.Mobile.Api.Identidad;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class EstadoSesionMovil
{
    [JsonRequired]
    public int Version { get; init; } = 1;
    public required string Api { get; init; }
    public required CuentaMovil Cuenta { get; init; }
    // Null conserva la identidad y exige reingreso; nunca borra la jornada.
    public TokensSesionMovil? Tokens { get; init; }
}
