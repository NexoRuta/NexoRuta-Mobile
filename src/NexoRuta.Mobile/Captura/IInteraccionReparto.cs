using NexoRuta.Mobile.Core.Rutas;
namespace NexoRuta.Mobile.Captura;
public interface IInteraccionReparto
{
    Task<string?> EscanearAsync();
    Task<byte[]?> FotoAsync();
    Task<byte[]?> FirmaAsync();
    Task<Guid[]?> SeleccionarRendicionAsync(IReadOnlyList<BultoRuta> bultos);
    Task<bool> ConfirmarSalidaConDiferenciasAsync(int bultosPendientes, int paradasAfectadas);
    Task<bool> ConfirmarCierreAsync(bool pruebaPendiente, int bultosSinRendir);
}
