using NexoRuta.Mobile.Core.Rutas;

namespace NexoRuta.Mobile.Application.Tests;

public sealed class VistaHojaTests
{
    [Theory]
    [InlineData(3, "Finalizada")]
    [InlineData(1, "Despachada")]
    public void ListaMuestraElEstadoActualSinSustituirLaHojaOriginal(int version, string estadoEsperado)
    {
        var id = Guid.NewGuid(); var acceso = Guid.NewGuid();
        var original = new HojaRuta { Id = id, RepartidorAccesoId = acceso, Version = 2, Estado = "Despachada" };
        var remota = new HojaRuta { Id = id, RepartidorAccesoId = acceso, Version = version, Estado = "Finalizada" };
        var vista = VistaHoja.Crear(original, remota);
        Assert.Equal(estadoEsperado, vista.Estado);
        Assert.Same(original, vista.Original);
        Assert.Equal("Despachada", vista.Original.Estado);
    }

    [Fact]
    public void ListaSinRedConservaLaHojaYNoAceptaVistaDeOtraRuta()
    {
        var original = new HojaRuta { Id = Guid.NewGuid(), Estado = "Despachada" };
        Assert.Equal("Despachada", VistaHoja.Crear(original, null).Estado);
        Assert.Throws<InvalidDataException>(() => VistaHoja.Crear(original, new HojaRuta { Id = Guid.NewGuid() }));
    }
}
