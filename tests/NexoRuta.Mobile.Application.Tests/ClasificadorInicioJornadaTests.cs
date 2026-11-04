using NexoRuta.Mobile.Api.Reparto;
using NexoRuta.Mobile.Application.Reparto;
using NexoRuta.Mobile.Core.Jornadas;

namespace NexoRuta.Mobile.Application.Tests;

public sealed class ClasificadorInicioJornadaTests
{
    private static readonly CuentaMovil Cuenta = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
    private static readonly DateTimeOffset Instante = DateTimeOffset.Parse("2026-10-30T12:00:00Z");

    [Fact]
    public void RechazoDefinitivoNoEsUnaJornadaActiva()
    {
        var jornada = JornadaLocal.Iniciar(Guid.NewGuid(), Guid.NewGuid(), Cuenta, Instante);
        var inicio = Assert.Single(jornada.Operaciones);
        jornada.ConfirmarPersistencia(inicio.Id);
        var acuse = new ConfirmacionOperacion { Id = inicio.Id, Secuencia = inicio.Secuencia, Resultado = "Conflicto" };

        Assert.Equal(EstadoInicioJornadaLocal.Rechazado, ClasificadorInicioJornada.Clasificar(jornada, [acuse]));
        Assert.False(jornada.EstaCerrada);
        Assert.False(jornada.TienePendientes);
        Assert.False(ClasificadorInicioJornada.BloqueaOtraJornada(jornada, EstadoInicioJornadaLocal.Rechazado, capturaPendiente: false));
    }

    [Fact]
    public void InicioAplicadoIdentificaJornadaActiva()
    {
        var jornada = JornadaLocal.Iniciar(Guid.NewGuid(), Guid.NewGuid(), Cuenta, Instante);
        var inicio = Assert.Single(jornada.Operaciones);
        jornada.ConfirmarPersistencia(inicio.Id);
        var acuse = new ConfirmacionOperacion { Id = inicio.Id, Secuencia = inicio.Secuencia, Resultado = "Aplicada" };

        Assert.Equal(EstadoInicioJornadaLocal.Aplicado, ClasificadorInicioJornada.Clasificar(jornada, [acuse]));
        Assert.True(ClasificadorInicioJornada.BloqueaOtraJornada(jornada, EstadoInicioJornadaLocal.Aplicado, capturaPendiente: false));
    }

    [Fact]
    public void SinAcuseElInicioPermaneceIncierto()
    {
        var jornada = JornadaLocal.Iniciar(Guid.NewGuid(), Guid.NewGuid(), Cuenta, Instante);

        Assert.Equal(EstadoInicioJornadaLocal.Pendiente, ClasificadorInicioJornada.Clasificar(jornada, []));
        Assert.True(jornada.TienePendientes);
        Assert.True(ClasificadorInicioJornada.BloqueaOtraJornada(jornada, EstadoInicioJornadaLocal.Pendiente, capturaPendiente: false));
    }
}
