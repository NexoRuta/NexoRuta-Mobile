using NexoRuta.Mobile.Core.Jornadas;
namespace NexoRuta.Mobile.Core.Rutas;

public sealed class HojaRuta
{
    public Guid Id { get; init; }
    public Guid JornadaId { get; init; }
    public Guid RepartidorAccesoId { get; init; }
    public Guid VehiculoId { get; init; }
    public string Matricula { get; init; } = "";
    public DateOnly Fecha { get; init; }
    public string Estado { get; init; } = "";
    public long Version { get; init; }
    public long UltimaSecuencia { get; init; }
    public PoliticaReparto Politica { get; init; } = new();
    public ParadaRuta[] Paradas { get; init; } = [];
    public void Validar(CuentaMovil cuenta)
    {
        if (Id == Guid.Empty || JornadaId == Guid.Empty || RepartidorAccesoId != cuenta.AccesoId
            || VehiculoId == Guid.Empty || Fecha == default || Version < 0 || UltimaSecuencia < 0
            || Estado is not ("Despachada" or "EnCurso" or "Finalizada") || Politica is null
            || Politica.Version < 1 || string.IsNullOrWhiteSpace(Politica.ZonaHoraria)
            || Politica.MaximoIntentos < 1 || Politica.Motivos is null || Politica.Motivos.Length == 0
            || Paradas is null || Paradas.Length == 0 || Paradas.Any(p => p is null) || Paradas.Select(x => x.EnvioId).Distinct().Count() != Paradas.Length)
            throw new InvalidDataException("La hoja no corresponde al repartidor o está incompleta.");
        foreach (var p in Paradas)
            if (p.EnvioId == Guid.Empty || p.Orden < 1 || p.EnvioVersion < 0 || !double.IsFinite(p.Latitud)
                || !double.IsFinite(p.Longitud) || p.Latitud is < -90 or > 90 || p.Longitud is < -180 or > 180
                || string.IsNullOrWhiteSpace(p.Direccion) || p.Bultos is null || p.Bultos.Length == 0
                || p.Bultos.Any(b => b is null || b.Id == Guid.Empty || string.IsNullOrWhiteSpace(b.Codigo)))
                throw new InvalidDataException("La hoja contiene una parada inválida.");
    }
}
