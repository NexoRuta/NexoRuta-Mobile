using System.Text.Json.Serialization;
namespace NexoRuta.Mobile.Core.Jornadas;
public sealed class DatosOperacionMovil
{
    public Guid? BultoId { get; init; }
    public Guid[]? BultoIds { get; init; }
    public string? Nombre { get; init; }
    public string? Documento { get; init; }
    public Guid? FotoId { get; init; }
    public Guid? FirmaId { get; init; }
    public string? Motivo { get; init; }
    public double? Latitud { get; init; }
    public double? Longitud { get; init; }
    public double? PrecisionMetros { get; init; }
    public DateTimeOffset? PosicionEnUtc { get; init; }
}
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DatosOperacionMovil))]
public partial class DatosOperacionJson : JsonSerializerContext;
