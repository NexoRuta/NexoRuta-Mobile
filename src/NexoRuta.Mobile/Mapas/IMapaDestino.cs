using NexoRuta.Mobile.Core.Rutas;
namespace NexoRuta.Mobile.Mapas;

public interface IMapaDestino : IDisposable
{
    View Vista { get; }
    bool Disponible { get; }
    void Mostrar(ParadaRuta parada);
    Task DescargarZonaAsync(HojaRuta hoja, IProgress<double> progreso, CancellationToken ct);
}
