using System.Text.Json;
namespace NexoRuta.Mobile.Api.Reparto;
public sealed class SobreOperacion
{
    public Guid Id { get; init; }
    public long Secuencia { get; init; }
    public string Tipo { get; init; } = "";
    public Guid? EnvioId { get; init; }
    public long? VersionEnvio { get; init; }
    public DateTimeOffset OcurridaEn { get; init; }
    public JsonElement Datos { get; init; }
}
