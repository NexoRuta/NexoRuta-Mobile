using System.Globalization;
using NexoRuta.Mobile.Core.Mapas;
using NexoRuta.Mobile.Core.Rutas;
namespace NexoRuta.Mobile.Core.Tests;

public sealed class ZonaMapaTests
{
    [Fact]
    public void HuellaEsIndependienteDeCulturaOrdenYEstadosPeroDistingueEstiloYCobertura()
    {
        var a = new ParadaRuta { Latitud = -34.9, Longitud = -56.2 };
        var b = new ParadaRuta { Latitud = -34.8, Longitud = -56.1 };
        var hoja = new HojaRuta { Id = Guid.NewGuid(), Paradas = [a, b] };
        var anterior = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-UY"); var zona = ZonaMapa.Para(hoja, "mapbox", "estilo");
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var cambiada = new HojaRuta { Id = hoja.Id, Version = 10, Estado = "EnCurso", Paradas = [b, a] };
            Assert.Equal(zona.Huella, ZonaMapa.Para(cambiada, "mapbox", "estilo").Huella);
            Assert.NotEqual(zona.Huella, ZonaMapa.Para(cambiada, "mapbox", "otro-estilo").Huella);
            Assert.True(zona.LatitudMinima < a.Latitud); Assert.True(zona.LatitudMaxima > b.Latitud);
        }
        finally { CultureInfo.CurrentCulture = anterior; }
    }
    [Theory]
    [InlineData(double.NaN, -56)]
    [InlineData(90, -56)]
    [InlineData(-34, 181)]
    public void NoDesplazaSilenciosamenteUnDestinoFueraDelMapa(double lat, double lon) =>
        Assert.Throws<InvalidOperationException>(() => ZonaMapa.Para(new() { Id = Guid.NewGuid(),
            Paradas = [new() { Latitud = lat, Longitud = lon }] }, "mapbox", "estilo"));
}
