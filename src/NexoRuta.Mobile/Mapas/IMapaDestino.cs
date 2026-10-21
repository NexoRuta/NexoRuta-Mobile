using NexoRuta.Mobile.Core.Rutas;
namespace NexoRuta.Mobile.Mapas;

public interface IMapaDestino : IDisposable
{
    View Vista { get; }
    void Mostrar(ParadaRuta parada);
}
