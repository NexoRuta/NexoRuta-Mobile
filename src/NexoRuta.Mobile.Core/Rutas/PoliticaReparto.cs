namespace NexoRuta.Mobile.Core.Rutas;
public sealed class PoliticaReparto
{
    public long Version { get; init; }
    public string ZonaHoraria { get; init; } = "";
    public int MaximoParadas { get; init; }
    public int MaximoIntentos { get; init; }
    public bool RequiereFirma { get; init; }
    public bool RequiereFoto { get; init; }
    public bool RequiereNombre { get; init; }
    public bool RequiereDocumento { get; init; }
    public bool RequiereFotoIntento { get; init; }
    public bool BloquearDiscrepanciasCarga { get; init; }
    public string[] Motivos { get; init; } = [];
}
