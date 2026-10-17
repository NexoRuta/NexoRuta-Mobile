using NexoRuta.Mobile.Core.Rutas;
namespace NexoRuta.Mobile.Captura;
public interface IInteraccionReparto
{
    Task<string?> EscanearAsync();
    Task<byte[]?> FotoAsync();
    Task<byte[]?> FirmaAsync();
    Task<Guid[]?> SeleccionarRendicionAsync(IReadOnlyList<BultoRuta> bultos);
    Task<bool> ConfirmarCierreAsync(bool pruebaPendiente);
}
