using NexoRuta.Mobile.Core.Jornadas;
using NexoRuta.Mobile.Core.Rutas;

namespace NexoRuta.Mobile.Core.Tests;

public sealed class ReglasEnvioTests
{
    [Fact]
    public void SerializacionOfflineConservaPublicacionesDistintasEnLaMismaRuta()
    {
        var cuenta = new CuentaMovil(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var primera = Reglas(Guid.NewGuid(), 1, true);
        var segunda = Reglas(Guid.NewGuid(), 2, false);
        var hoja = Hoja(cuenta, primera, segunda);
        var leida = SerializadorHojaRuta.Deserializar(SerializadorHojaRuta.Serializar(hoja), cuenta);
        Assert.Equal(primera.VersionId, leida.Paradas[0].Reglas.VersionId);
        Assert.True(leida.Paradas[0].Reglas.RequiereFoto);
        Assert.Equal(segunda.VersionId, leida.Paradas[1].Reglas.VersionId);
        Assert.False(leida.Paradas[1].Reglas.RequiereFoto);
        Assert.Equal("AUSENTE", Assert.Single(leida.Paradas[1].Reglas.Motivos).Codigo);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HojaSinReferenciaPublicadaNoSePresentaComoUtilizable(bool ausentes)
    {
        var cuenta = new CuentaMovil(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var hoja = Hoja(cuenta, ausentes ? null! : Reglas(Guid.Empty, 1, true));
        Assert.Throws<InvalidDataException>(() => SerializadorHojaRuta.Deserializar(SerializadorHojaRuta.Serializar(hoja), cuenta));
    }

    private static ReglasEnvio Reglas(Guid version, int numero, bool foto) => new()
    {
        VersionId = version, NumeroVersion = numero, MaximoIntentos = 3, IntervaloEntreIntentosMinutos = 60,
        PoliticaDevolucionAlAgotarIntentos = "DecisionOperativa", RequiereNombre = true, RequiereFoto = foto,
        Motivos = [new("AUSENTE", "Ausente")]
    };
    private static HojaRuta Hoja(CuentaMovil cuenta, params ReglasEnvio[] reglas) => new()
    {
        Id = Guid.NewGuid(), JornadaId = Guid.NewGuid(), RepartidorAccesoId = cuenta.AccesoId, VehiculoId = Guid.NewGuid(),
        Fecha = new(2026, 10, 20), Estado = "Despachada", Politica = new() { Version = 1, ZonaHoraria = "America/Montevideo" },
        Paradas = reglas.Select((r, i) => new ParadaRuta { EnvioId = Guid.NewGuid(), Orden = i + 1,
            Direccion = "Destino ficticio", Reglas = r, Bultos = [new() { Id = Guid.NewGuid(), Codigo = $"TEST-{i}" }] }).ToArray()
    };
}
