namespace NexoRuta.Mobile.Core.Rutas;
public sealed class PoliticaReparto
{
    public long Version { get; init; }
    public string ZonaHoraria { get; init; } = "";
    public int MaximoParadas { get; init; }
    public bool RequiereFotoIntento { get; init; }
    public bool BloquearDiscrepanciasCarga { get; init; }
}
