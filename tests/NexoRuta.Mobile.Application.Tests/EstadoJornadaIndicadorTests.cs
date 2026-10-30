using NexoRuta.Mobile.Application;
using NexoRuta.Mobile.Core.Identidad;
using NexoRuta.Mobile.Core.Jornadas;

namespace NexoRuta.Mobile.Application.Tests;

public sealed class EstadoJornadaIndicadorTests
{
    private readonly CuentaMovil cuenta = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
    private static readonly DateTimeOffset Ahora = new(2026, 10, 30, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void InicioSinAcusePermanecePendienteYNoSePresentaComoJornadaActiva()
    {
        var jornada = JornadaLocal.Iniciar(Guid.NewGuid(), Guid.NewGuid(), cuenta, Ahora);

        var estado = EstadoJornadaIndicador.Crear(jornada, new Dictionary<Guid, string>());

        Assert.Equal("Inicio pendiente de sincronización", estado.Fase);
        Assert.True(estado.TieneOperacionesPendientes);
        Assert.False(estado.Iniciada);
    }

    [Fact]
    public void InicioAplicadoSeMuestraComoCargaEnDepositoHastaAcuseDeSalida()
    {
        var jornada = JornadaLocal.Iniciar(Guid.NewGuid(), Guid.NewGuid(), cuenta, Ahora);
        var inicio = Assert.Single(jornada.Operaciones);

        var estado = EstadoJornadaIndicador.Crear(jornada, new Dictionary<Guid, string> { [inicio.Id] = "Aplicada" });

        Assert.Equal("Jornada iniciada · carga en depósito", estado.Fase);
        Assert.True(estado.Iniciada);
        Assert.False(estado.RepartoEnCurso);
    }

    [Fact]
    public void SalidaLocalSinAcuseNoSePresentaComoRepartoEnCurso()
    {
        var jornada = JornadaLocal.Iniciar(Guid.NewGuid(), Guid.NewGuid(), cuenta, Ahora);
        var inicio = Assert.Single(jornada.Operaciones);
        var salida = jornada.ConfirmarCarga(Ahora.AddMinutes(1));

        var restaurada = JornadaLocal.Restaurar(jornada.ExportarEstado(), cuenta);
        var estado = EstadoJornadaIndicador.Crear(restaurada, new Dictionary<Guid, string> { [inicio.Id] = "Aplicada" });

        Assert.Equal("Jornada iniciada · carga en depósito", estado.Fase);
        Assert.True(estado.TieneOperacionesPendientes);
        Assert.NotEqual("Reparto en curso", estado.Fase);
        Assert.Contains(salida.Id, restaurada.Operaciones.Select(x => x.Id));
    }

    [Fact]
    public void SalidaRechazadaNoSePresentaComoRepartoYQuedaVisibleComoRechazo()
    {
        var jornada = JornadaLocal.Iniciar(Guid.NewGuid(), Guid.NewGuid(), cuenta, Ahora);
        var inicio = Assert.Single(jornada.Operaciones);
        var salida = jornada.ConfirmarCarga(Ahora.AddMinutes(1));
        var restaurada = JornadaLocal.Restaurar(jornada.ExportarEstado(), cuenta);

        var estado = EstadoJornadaIndicador.Crear(restaurada, new Dictionary<Guid, string>
        {
            [inicio.Id] = "Aplicada",
            [salida.Id] = "Conflicto"
        });

        Assert.Equal("Jornada iniciada · carga en depósito", estado.Fase);
        Assert.True(estado.TieneRechazos);
        Assert.False(estado.TieneOperacionesPendientes);
    }

    [Fact]
    public void AcuseTardioTrasReinicioActualizaLaFaseSinCrearOtraSalida()
    {
        var jornada = JornadaLocal.Iniciar(Guid.NewGuid(), Guid.NewGuid(), cuenta, Ahora);
        var inicio = Assert.Single(jornada.Operaciones);
        var salida = jornada.ConfirmarCarga(Ahora.AddMinutes(1));
        var restaurada = JornadaLocal.Restaurar(jornada.ExportarEstado(), cuenta);
        var antes = EstadoJornadaIndicador.Crear(restaurada,
            new Dictionary<Guid, string> { [inicio.Id] = "Aplicada" });

        var despues = EstadoJornadaIndicador.Crear(restaurada,
            new Dictionary<Guid, string> { [inicio.Id] = "Aplicada", [salida.Id] = "Aplicada" });

        Assert.Equal("Jornada iniciada · carga en depósito", antes.Fase);
        Assert.Equal("Reparto en curso", despues.Fase);
        Assert.Single(restaurada.Operaciones, o => o.Tipo == TipoOperacionLocal.ConfirmarCarga);
    }

    [Fact]
    public void SalidaAplicadaYFinAplicadoSeDistinguenDeSusComandosLocales()
    {
        var jornada = JornadaLocal.Iniciar(Guid.NewGuid(), Guid.NewGuid(), cuenta, Ahora);
        var inicio = Assert.Single(jornada.Operaciones);
        var salida = jornada.ConfirmarCarga(Ahora.AddMinutes(1));
        var reparto = EstadoJornadaIndicador.Crear(jornada, new Dictionary<Guid, string>
        {
            [inicio.Id] = "Aplicada",
            [salida.Id] = "Aplicada"
        });

        Assert.Equal("Reparto en curso", reparto.Fase);
        Assert.True(reparto.RepartoEnCurso);

        var cierre = jornada.Finalizar(Ahora.AddHours(8));
        var cierrePendiente = EstadoJornadaIndicador.Crear(jornada, new Dictionary<Guid, string>
        {
            [inicio.Id] = "Aplicada",
            [salida.Id] = "Aplicada"
        });
        Assert.Equal("Cierre pendiente de sincronización", cierrePendiente.Fase);
        Assert.False(cierrePendiente.Finalizada);
        Assert.True(cierrePendiente.TieneOperacionesPendientes);

        var finalizada = EstadoJornadaIndicador.Crear(jornada, new Dictionary<Guid, string>
        {
            [inicio.Id] = "Aplicada",
            [salida.Id] = "Aplicada",
            [cierre.Id] = "Aplicada"
        });
        Assert.Equal("Jornada finalizada", finalizada.Fase);
        Assert.True(finalizada.Finalizada);
    }
}
