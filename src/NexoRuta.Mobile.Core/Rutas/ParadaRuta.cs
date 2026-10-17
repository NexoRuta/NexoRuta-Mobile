namespace NexoRuta.Mobile.Core.Rutas;
public sealed class ParadaRuta
{
    public Guid EnvioId { get; init; }
    public int Orden { get; init; }
    public bool Activa { get; init; }
    public string? MotivoSuspension { get; init; }
    public DateTimeOffset LlegadaEstimadaUtc { get; init; }
    public string Estado { get; init; } = "";
    public long EnvioVersion { get; init; }
    public string Destinatario { get; init; } = "";
    public string Direccion { get; init; } = "";
    public double Latitud { get; init; }
    public double Longitud { get; init; }
    public BultoRuta[] Bultos { get; init; } = [];
    public string Titulo => $"{Orden}. {Destinatario}";
    public string Resumen => $"{Bultos.Length} bulto(s) · {Estado}";
}
