using System.Text.Json;

namespace NexoRuta.Mobile.Core.Jornadas;

public sealed class OperacionLocal
{
    public Guid Id { get; }
    public long Secuencia { get; }
    public TipoOperacionLocal Tipo { get; }
    public Guid? EnvioId { get; }
    public DateTimeOffset OcurridaEn { get; }
    public JsonElement Datos { get; }

    public OperacionLocal(Guid id, long secuencia, TipoOperacionLocal tipo,
        Guid? envioId, DateTimeOffset ocurridaEn, JsonElement datos)
    {
        if (id == Guid.Empty || secuencia < 1 || !Enum.IsDefined(tipo))
            throw new ArgumentException("La identidad, secuencia o tipo de operación no son válidos.");

        var requiereEnvio = tipo is TipoOperacionLocal.EntregaFisica or TipoOperacionLocal.EvidenciaEntrega
            or TipoOperacionLocal.CargarBulto or TipoOperacionLocal.IntentoFallido;
        if (requiereEnvio ? envioId is null || envioId == Guid.Empty : envioId is not null)
            throw new ArgumentException("El envío no corresponde al tipo de operación.");

        if (datos.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Los datos de la operación deben ser un objeto JSON.");

        Id = id;
        Secuencia = secuencia;
        Tipo = tipo;
        EnvioId = envioId;
        OcurridaEn = ocurridaEn;
        Datos = datos.Clone();
    }
}
