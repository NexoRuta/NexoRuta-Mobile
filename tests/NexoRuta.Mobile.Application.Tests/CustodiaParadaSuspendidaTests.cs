using NexoRuta.Mobile.Core.Rutas;

namespace NexoRuta.Mobile.Application.Tests;

public sealed class CustodiaParadaSuspendidaTests
{
    [Theory]
    [InlineData("Deposito", false, "en depósito")]
    [InlineData("Deposito", true, "devolver en rendición")]
    [InlineData("Repartidor", false, "devolver en rendición")]
    [InlineData("PendienteDeposito", false, "recepción en depósito pendiente")]
    public void SuspenderAntesDeCargarNoPideDevolverUnBultoQueSigueEnDeposito(string custodiaRemota, bool cargaLocalSinSincronizar, string esperado)
    {
        var envio = Guid.NewGuid(); var bulto = Guid.NewGuid();
        var original = new ParadaRuta { EnvioId = envio, Bultos = [new() { Id = bulto, Custodia = "Deposito" }] };
        var remota = new HojaRuta { Paradas = [new() { EnvioId = envio, Bultos = [new() { Id = bulto, Custodia = custodiaRemota }] }] };
        HashSet<Guid> cargados = cargaLocalSinSincronizar ? [bulto] : [];
        Assert.Equal("Fuera de esta ruta · " + esperado, VistaParada.DescribirSuspension(original, remota, cargados));
    }
}
