namespace NexoRuta.Mobile.Core.Rutas;

public sealed class ReglasEnvio
{
    public Guid VersionId { get; init; }
    public int NumeroVersion { get; init; }
    public int MaximoIntentos { get; init; }
    public int IntervaloEntreIntentosMinutos { get; init; }
    public string PoliticaDevolucionAlAgotarIntentos { get; init; } = "";
    public bool RequiereFirma { get; init; }
    public bool RequiereFoto { get; init; }
    public bool RequiereNombre { get; init; }
    public bool RequiereDocumento { get; init; }
    public MotivoNoEntrega[] Motivos { get; init; } = [];

    public bool EsValida => VersionId != Guid.Empty && NumeroVersion > 0 && MaximoIntentos is > 0 and <= 100
        && IntervaloEntreIntentosMinutos > 0
        && PoliticaDevolucionAlAgotarIntentos is "DecisionOperativa" or "IniciarAutomaticamente"
        && (RequiereFirma || RequiereFoto || RequiereNombre || RequiereDocumento)
        && Motivos is { Length: > 0 } && Motivos.All(m => m is not null
            && !string.IsNullOrWhiteSpace(m.Codigo) && !string.IsNullOrWhiteSpace(m.Descripcion))
        && Motivos.Select(m => m.Codigo).Distinct(StringComparer.Ordinal).Count() == Motivos.Length;
}
public sealed record MotivoNoEntrega(string Codigo, string Descripcion);
