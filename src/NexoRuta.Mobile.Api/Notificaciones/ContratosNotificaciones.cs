using System.Text.Json.Serialization;
namespace NexoRuta.Mobile.Api.Notificaciones;

public sealed record AvisoReparto(Guid Id, Guid RutaId, Guid EnvioId, long VersionEnvio, string Tipo,
    string Motivo, DateTimeOffset CreadoEnUtc, bool Vigente, DateTimeOffset? ConfirmadoEnUtc)
{
    public void Validar()
    {
        if (Id == Guid.Empty || RutaId == Guid.Empty || EnvioId == Guid.Empty || VersionEnvio < 0
            || Tipo is not ("Reasignacion" or "Cancelacion") || string.IsNullOrWhiteSpace(Motivo)
            || Motivo.Length > 500 || CreadoEnUtc == default)
            throw new InvalidDataException("La respuesta del aviso está incompleta.");
    }
}
public sealed record RegistroInstalacion(Guid InstalacionId, string Plataforma, string FirebaseInstallationId, bool Permitida);
public sealed record ConfirmarAviso(string Tipo);
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AvisoReparto))]
[JsonSerializable(typeof(AvisoReparto[]))]
[JsonSerializable(typeof(RegistroInstalacion))]
[JsonSerializable(typeof(ConfirmarAviso))]
public partial class NotificacionesJson : JsonSerializerContext;
