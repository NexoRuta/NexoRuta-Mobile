using NexoRuta.Mobile.Core.Jornadas;
using NexoRuta.Mobile.Core.Rutas;

namespace NexoRuta.Mobile.Core.Tests;

public sealed class IncidenciasCierreTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HojaConservaIncidenciaHistoricaAunqueElDepositoYaRecibioElBulto(bool recibida)
    {
        var cuenta = new CuentaMovil(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var envio = Guid.NewGuid(); var bulto = Guid.NewGuid();
        var incidencia = new IncidenciaCierre(Guid.NewGuid(), envio, bulto, "LOCAL-1", 2, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var hoja = Hoja(cuenta, envio, bulto, recibida ? "Deposito" : "Repartidor", incidencia);
        var reabierta = SerializadorHojaRuta.Deserializar(SerializadorHojaRuta.Serializar(hoja), cuenta);
        Assert.Equal(incidencia, Assert.Single(reabierta.IncidenciasCierre));
        Assert.Equal(recibida ? "Deposito" : "Repartidor", reabierta.Paradas[0].Bultos[0].Custodia);
        var ajena = Hoja(cuenta, envio, bulto, "Repartidor", incidencia with { BultoId = Guid.NewGuid() });
        Assert.Throws<InvalidDataException>(() => SerializadorHojaRuta.Deserializar(SerializadorHojaRuta.Serializar(ajena), cuenta));
    }

    private static HojaRuta Hoja(CuentaMovil cuenta, Guid envio, Guid bulto, string custodia, IncidenciaCierre incidencia) => new()
    {
        Id = Guid.NewGuid(), JornadaId = Guid.NewGuid(), RepartidorAccesoId = cuenta.AccesoId, VehiculoId = Guid.NewGuid(),
        Fecha = new(2026, 10, 20), Estado = "Finalizada", Politica = new() { Version = 1, ZonaHoraria = "America/Montevideo" },
        Paradas = [new() { Reglas = new() { VersionId = Guid.NewGuid(), NumeroVersion = 1, MaximoIntentos = 3,
                IntervaloEntreIntentosMinutos = 1, PoliticaDevolucionAlAgotarIntentos = "DecisionOperativa",
                RequiereFoto = true, RequiereNombre = true, Motivos = [new("AUSENTE", "Ausente")] }, EnvioId = envio, Orden = 1, Direccion = "Dirección ficticia", Latitud = -34.9, Longitud = -56.2,
            Bultos = [new() { Id = bulto, Codigo = "LOCAL-1", Custodia = custodia }] }], IncidenciasCierre = [incidencia]
    };
}
