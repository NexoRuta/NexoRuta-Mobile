using NexoRuta.Mobile.Core.Rutas;
namespace NexoRuta.Mobile.Captura;
public sealed class InteraccionReparto : IInteraccionReparto
{
    private static Page Actual => Microsoft.Maui.Controls.Application.Current!.Windows[0].Page!;
    public async Task<string?> EscanearAsync() { var page = new EscanerPage(); await Actual.Navigation.PushAsync(page); return await page.Resultado; }
    public async Task<byte[]?> FotoAsync() { var page = new FotoPage(); await Actual.Navigation.PushAsync(page); return await page.Resultado; }
    public async Task<byte[]?> FirmaAsync() { var page = new FirmaPage(); await Actual.Navigation.PushAsync(page); return await page.Resultado; }
    public async Task<Guid[]?> SeleccionarRendicionAsync(IReadOnlyList<BultoRuta> bultos)
    {
        var page = new RendicionPage(bultos); await Actual.Navigation.PushAsync(page); return await page.Resultado;
    }
    public Task<bool> ConfirmarCierreAsync(bool pruebaPendiente, int bultosSinRendir) => Actual.DisplayAlertAsync("Terminar jornada",
        (bultosSinRendir > 0 ? $"Hay {bultosSinRendir} bulto(s) sin entrega ni rendición. Se registrará una incidencia y conservarás su custodia hasta la recepción física en depósito.\n\n" : "")
        + (pruebaPendiente ? "Hay entregas con prueba pendiente o sin validación confirmada. El servidor evaluará su prueba al recibir el cierre, después de sincronizar los registros anteriores."
            : "Se detendrá el seguimiento de ubicación y se enviará el cierre después de sincronizar los registros anteriores."), "Terminar jornada", "Seguir trabajando");
}
