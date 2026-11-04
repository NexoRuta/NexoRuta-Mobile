using NexoRuta.Mobile.Core.Rutas;
namespace NexoRuta.Mobile.Mapas;

public interface IMapaDestino : IDisposable
{
    View Vista { get; }
    string EstadoInicial { get; }
    event Action<string>? EstadoCambiado;
    void Mostrar(IReadOnlyCollection<ParadaRuta> paradas);
}
