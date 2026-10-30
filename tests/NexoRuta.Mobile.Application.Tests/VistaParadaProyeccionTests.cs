using NexoRuta.Mobile.Application;
using NexoRuta.Mobile.Core.Rutas;

namespace NexoRuta.Mobile.Application.Tests;

public sealed class VistaParadaProyeccionTests
{
    [Fact]
    public void SuspensionNoSustituyeEstadoComercialNiPierdeSuMotivo()
    {
        var parada = new ParadaRuta
        {
            EnvioId = Guid.NewGuid(), Orden = 1, Activa = false,
            MotivoSuspension = "Carga incompleta: requiere revisión en depósito.",
            Estado = "AsignadoRuta", EnvioVersion = 2,
            Bultos = [new BultoRuta { Id = Guid.NewGuid(), Codigo = "BOX-1", Custodia = "Deposito" }]
        };

        var vista = VistaParada.Obtener(parada, null, []);

        Assert.Equal("AsignadoRuta", vista.Estado);
        Assert.False(vista.Activa);
        Assert.Equal(parada.MotivoSuspension, vista.MotivoSuspension);
        Assert.Equal(2, vista.Version);
    }
}
