using NexoRuta.Mobile.Core.Rutas;

namespace NexoRuta.Mobile.Application;

public sealed record VistaHoja(HojaRuta Original, string Estado, string FaseJornada = "", string EstadoSincronizacion = "",
    bool TieneJornada = false)
{
    public HojaRuta? Remota { get; init; }
    private HojaRuta Datos => Remota ?? Original;
    public DateOnly Fecha => Original.Fecha;
    public string Matricula => Original.Matricula;
    public string EtiquetaEstado => TextoReparto.Estado(Estado);
    public string Paradas
    {
        get
        {
            var activa = Datos.Estado != "Finalizada" ? Datos.Paradas.Count(p => p.Activa) : 0;
            var fuera = Datos.Paradas.Length - activa;
            if (activa == 0) return fuera == 0 ? "Sin paradas" : $"0 activas · {fuera} fuera de ruta";
            if (fuera == 0) return activa == 1 ? "1 parada activa" : $"{activa} paradas activas";
            return $"{activa} activas · {fuera} fuera de ruta";
        }
    }
    public string Bultos => TextoReparto.Bultos(Datos.Estado != "Finalizada"
        ? Datos.Paradas.Where(p => p.Activa).Sum(p => p.Bultos.Length) : 0);
    public string Accion => TieneJornada ? "Retomar jornada" : "Ver mi recorrido";
    public bool MostrarSincronizacion => TieneJornada && EstadoSincronizacion != "Sin operaciones pendientes";

    public static VistaHoja Crear(HojaRuta original, HojaRuta? remota, EstadoJornadaIndicador? jornada = null)
    {
        if (remota is not null && (remota.Id != original.Id || remota.RepartidorAccesoId != original.RepartidorAccesoId))
            throw new InvalidDataException("La vista remota no corresponde a esta hoja.");
        var actual = remota is not null && remota.Version >= original.Version ? remota : null;
        return new(original, actual?.Estado ?? original.Estado,
            jornada?.Fase ?? "", jornada?.EstadoSincronizacion ?? "", jornada is not null && jornada.Fase != "Sin jornada iniciada")
        { Remota = actual };
    }
}
