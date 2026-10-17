using System.Text.Json;
using System.Text.Json.Serialization;
using NexoRuta.Mobile.Core.Jornadas;
namespace NexoRuta.Mobile.Core.Rutas;
public static class SerializadorHojaRuta
{
    public static byte[] Serializar(HojaRuta hoja) => JsonSerializer.SerializeToUtf8Bytes(hoja, HojaRutaJson.Default.HojaRuta);
    public static HojaRuta Deserializar(ReadOnlySpan<byte> datos, CuentaMovil cuenta)
    {
        var hoja = JsonSerializer.Deserialize(datos, HojaRutaJson.Default.HojaRuta) ?? throw new InvalidDataException("La hoja está vacía.");
        hoja.Validar(cuenta); return hoja;
    }
}
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(HojaRuta))]
[JsonSerializable(typeof(HojaRuta[]))]
public partial class HojaRutaJson : JsonSerializerContext;
