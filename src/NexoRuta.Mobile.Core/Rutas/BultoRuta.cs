namespace NexoRuta.Mobile.Core.Rutas;
public sealed class BultoRuta
{
    public bool EnEstaRuta { get; init; }
    public Guid Id { get; init; }
    public string Codigo { get; init; } = "";
    public decimal PesoGramos { get; init; }
    public decimal VolumenCm3 { get; init; }
    public string Resultado { get; init; } = "Pendiente";
    public string Custodia { get; init; } = "";
    public long Version { get; init; }
}
