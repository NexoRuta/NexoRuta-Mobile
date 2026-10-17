using System.Text.Json.Serialization;
namespace NexoRuta.Mobile.Api.Reparto;
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SobreOperacion))]
[JsonSerializable(typeof(ConfirmacionOperacion))]
[JsonSerializable(typeof(EvidenciaRequest))]
public partial class ContratosRepartoJson : JsonSerializerContext;
