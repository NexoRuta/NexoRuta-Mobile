using NexoRuta.Mobile.Core.Rutas;

namespace NexoRuta.Mobile.Application.Tests;

public sealed class CustodiaParadaSuspendidaTests
{
    [Fact]
    public void ContarActivasExcluyeLaParadaSuspendidaEnLaVistaRemota()
    {
        var activa = new ParadaRuta { EnvioId = Guid.NewGuid(), Activa = true, EnvioVersion = 1 };
        var suspendida = new ParadaRuta { EnvioId = Guid.NewGuid(), Activa = true, EnvioVersion = 1 };
        var original = new HojaRuta { Paradas = [activa, suspendida] };
        var remota = new HojaRuta
        {
            Paradas =
            [
                new() { EnvioId = activa.EnvioId, Activa = true, EnvioVersion = 2 },
                new() { EnvioId = suspendida.EnvioId, Activa = false, EnvioVersion = 2 }
            ]
        };

        Assert.Equal(1, VistaParada.ContarActivas(original, remota, []));
    }

    [Fact]
    public void ContarActivasEsCeroCuandoLaUnicaParadaFueSuspendida()
    {
        var parada = new ParadaRuta { EnvioId = Guid.NewGuid(), Activa = true, EnvioVersion = 1 };
        var original = new HojaRuta { Paradas = [parada] };
        var remota = new HojaRuta { Paradas = [new() { EnvioId = parada.EnvioId, Activa = false, EnvioVersion = 2 }] };

        Assert.Equal(0, VistaParada.ContarActivas(original, remota, []));
        Assert.False(VistaParada.PuedeIniciar(original, remota, []));
    }

    [Fact]
    public void PuedeIniciarUnaHojaDespachadaConAlMenosUnaParadaActiva()
    {
        var parada = new ParadaRuta { EnvioId = Guid.NewGuid(), Activa = true, EnvioVersion = 1 };
        var original = new HojaRuta { Estado = "Despachada", Paradas = [parada] };
        var remota = new HojaRuta { Estado = "Despachada", Paradas = [new() { EnvioId = parada.EnvioId, Activa = true, EnvioVersion = 1 }] };

        Assert.True(VistaParada.PuedeIniciar(original, remota, []));
    }

    [Fact]
    public void LaElegibilidadDeInicioRespetaLaFechaCalendarioDeLaZonaDelOperador()
    {
        var hoja = new HojaRuta
        {
            Fecha = new DateOnly(2026, 10, 31),
            Politica = new() { ZonaHoraria = "America/Montevideo" }
        };

        Assert.False(VistaParada.EsFechaInicioPermitida(hoja,
            new DateTimeOffset(2026, 10, 30, 2, 30, 0, TimeSpan.Zero)));
        Assert.True(VistaParada.EsFechaInicioPermitida(hoja,
            new DateTimeOffset(2026, 10, 31, 3, 30, 0, TimeSpan.Zero)));
    }

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
