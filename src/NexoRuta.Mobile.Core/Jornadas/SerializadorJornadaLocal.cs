using System.Text.Json;
using System.Text.Json.Serialization;

namespace NexoRuta.Mobile.Core.Jornadas;

public static class SerializadorJornadaLocal
{
    public static byte[] Serializar(JornadaLocal jornada)
    {
        ArgumentNullException.ThrowIfNull(jornada);
        return JsonSerializer.SerializeToUtf8Bytes(jornada.ExportarEstado(),
            JornadaJsonContext.Default.EstadoJornadaLocal);
    }

    public static JornadaLocal Deserializar(ReadOnlySpan<byte> datos, CuentaMovil cuentaEsperada)
    {
        var estado = JsonSerializer.Deserialize(datos, JornadaJsonContext.Default.EstadoJornadaLocal)
            ?? throw new JsonException("El estado de jornada no puede ser nulo.");
        return JornadaLocal.Restaurar(estado, cuentaEsperada);
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(EstadoJornadaLocal))]
internal partial class JornadaJsonContext : JsonSerializerContext
{
}
