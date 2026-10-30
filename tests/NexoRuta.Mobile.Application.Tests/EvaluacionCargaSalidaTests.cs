using NexoRuta.Mobile.Application;
using NexoRuta.Mobile.Core.Rutas;

namespace NexoRuta.Mobile.Application.Tests;

public sealed class EvaluacionCargaSalidaTests
{
    [Fact]
    public void CuentaLosBultosPendientesYLasParadasAfectadasSinIncluirParadasSuspendidas()
    {
        var confirmado = Guid.NewGuid();
        var pendiente = Guid.NewGuid();
        var suspendido = Guid.NewGuid();
        var hoja = new HojaRuta
        {
            Id = Guid.NewGuid(),
            Paradas =
            [
                new() { EnvioId = Guid.NewGuid(), Activa = true, Bultos = [new() { Id = confirmado }, new() { Id = pendiente }] },
                new() { EnvioId = Guid.NewGuid(), Activa = true, Bultos = [new() { Id = Guid.NewGuid() }] },
                new() { EnvioId = Guid.NewGuid(), Activa = false, Bultos = [new() { Id = suspendido }] }
            ]
        };

        var evaluacion = EvaluacionCargaSalida.Crear(hoja, new HashSet<Guid> { confirmado });

        Assert.Equal(3, evaluacion.BultosTotales);
        Assert.Equal(1, evaluacion.BultosConfirmados);
        Assert.Equal(2, evaluacion.BultosPendientes);
        Assert.Equal(2, evaluacion.ParadasAfectadas);
    }

    [Fact]
    public void PoliticaEstricaBloqueaYPoliticaPermisivaRequiereConfirmacionCuandoHayDiferencias()
    {
        var hoja = new HojaRuta
        {
            Id = Guid.NewGuid(),
            Paradas = [new() { EnvioId = Guid.NewGuid(), Activa = true, Bultos = [new() { Id = Guid.NewGuid() }] }]
        };
        var evaluacion = EvaluacionCargaSalida.Crear(hoja, new HashSet<Guid>());

        Assert.False(evaluacion.PuedeIniciar(new PoliticaReparto { BloquearDiscrepanciasCarga = true }));
        Assert.True(evaluacion.PuedeIniciar(new PoliticaReparto { BloquearDiscrepanciasCarga = false }));
        Assert.True(evaluacion.RequiereConfirmacionExplicita);
    }
}
