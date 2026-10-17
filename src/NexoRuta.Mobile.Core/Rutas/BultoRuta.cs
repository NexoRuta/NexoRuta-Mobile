namespace NexoRuta.Mobile.Core.Rutas;
public sealed class BultoRuta
{
    public Guid Id { get; init; }
    public string Codigo { get; init; } = "";
    public decimal PesoGramos { get; init; }
    public decimal VolumenCm3 { get; init; }
    public string Custodia { get; init; } = "";
    public long Version { get; init; }
}
