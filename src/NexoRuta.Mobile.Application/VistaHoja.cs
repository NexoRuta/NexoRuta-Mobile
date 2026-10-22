using NexoRuta.Mobile.Core.Rutas;

namespace NexoRuta.Mobile.Application;

public sealed record VistaHoja(HojaRuta Original, string Estado)
{
    public DateOnly Fecha => Original.Fecha;
    public string Matricula => Original.Matricula;
    public string EtiquetaEstado => TextoReparto.Estado(Estado);
    public string Paradas => Original.Paradas.Length == 1 ? "1 parada" : $"{Original.Paradas.Length} paradas";
    public string Bultos => TextoReparto.Bultos(Original.Paradas.Sum(p => p.Bultos.Length));

    public static VistaHoja Crear(HojaRuta original, HojaRuta? remota)
    {
        if (remota is not null && (remota.Id != original.Id || remota.RepartidorAccesoId != original.RepartidorAccesoId))
            throw new InvalidDataException("La vista remota no corresponde a esta hoja.");
        return new(original, remota is not null && remota.Version >= original.Version ? remota.Estado : original.Estado);
    }
}
