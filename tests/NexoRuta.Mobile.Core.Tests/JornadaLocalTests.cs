using System.Text.Json;
using NexoRuta.Mobile.Core.Jornadas;

namespace NexoRuta.Mobile.Core.Tests;

public sealed class JornadaLocalTests
{
    private static readonly CuentaMovil Cuenta = new(Guid.Parse("019e0000-0000-7000-8000-000000000001"),
        Guid.Parse("019e0000-0000-7000-8000-000000000002"),
        Guid.Parse("019e0000-0000-7000-8000-000000000003"));
    private static readonly Guid Envio = Guid.Parse("019e0000-0000-7000-8000-000000000004");
    private static readonly DateTimeOffset Inicio = new(2026, 10, 17, 8, 0, 0, TimeSpan.FromHours(-3));

    [Fact]
    public void CierreEsperaEntregaYEvidenciaYConservaPendientesHastaConfirmacion()
    {
        var jornada = NuevaJornada();
        var entrega = jornada.RegistrarEntregaFisica(Envio, Datos(), Inicio.AddHours(1));
        var prueba = jornada.AdjuntarEvidencia(Envio, Datos(), Inicio.AddHours(2));
        var cierre = jornada.Finalizar(Inicio.AddHours(8));

        Assert.True(jornada.EstaCerrada);
        Assert.True(jornada.TienePendientes);
        Assert.Equal(new[] { 1L, 2L, 3L }, jornada.PrepararEnvio().Select(x => x.Secuencia));
        Assert.DoesNotContain(cierre, jornada.PrepararEnvio());

        jornada.ConfirmarPersistencia(jornada.Operaciones[0].Id);
        jornada.ConfirmarPersistencia(entrega.Id);
        Assert.Equal(prueba.Id, Assert.Single(jornada.PrepararEnvio()).Id);
        jornada.ConfirmarPersistencia(prueba.Id);

        Assert.Equal(cierre.Id, Assert.Single(jornada.PrepararEnvio()).Id);
        Assert.True(jornada.TienePendientes);
        jornada.ConfirmarPersistencia(cierre.Id);
        Assert.False(jornada.TienePendientes);
        Assert.Empty(jornada.PrepararEnvio());
    }

    [Fact]
    public void ConfirmarCierreAntesDelTrabajoPrevioNoDescartaNada()
    {
        var jornada = NuevaJornada();
        jornada.RegistrarEntregaFisica(Envio, Datos(), Inicio.AddHours(1));
        var cierre = jornada.Finalizar(Inicio.AddHours(8));

        Assert.Throws<InvalidOperationException>(() => jornada.ConfirmarPersistencia(cierre.Id));
        Assert.Empty(jornada.ExportarEstado().Confirmadas);
        Assert.Equal(2, jornada.PrepararEnvio().Count);
    }

    [Fact]
    public void ReintentoDeCierreYConfirmacionMantieneIdentidadFechaYSecuencia()
    {
        var jornada = NuevaJornada();
        var cierre = jornada.Finalizar(Inicio.AddHours(8));
        var repetido = jornada.Finalizar(Inicio.AddHours(9));

        Assert.Same(cierre, repetido);
        Assert.Equal(Inicio.AddHours(8), repetido.OcurridaEn);
        Assert.Equal(2, jornada.Operaciones.Count);
        jornada.ConfirmarPersistencia(jornada.Operaciones[0].Id);
        jornada.ConfirmarPersistencia(cierre.Id);
        jornada.ConfirmarPersistencia(cierre.Id);
        Assert.Equal(2, jornada.ExportarEstado().Confirmadas.Count);
    }

    [Fact]
    public void ReintentoDeInicioConservaElComandoRechazadoYAgregaUnaUnicaSecuencia()
    {
        var jornada = NuevaJornada();
        var original = Assert.Single(jornada.Operaciones);

        Assert.Throws<InvalidOperationException>(() => jornada.ReintentarInicio(original.Id, "Aplicada", Inicio.AddHours(1)));
        jornada.ConfirmarPersistencia(original.Id);
        Assert.Throws<InvalidOperationException>(() => jornada.ReintentarInicio(original.Id, "Aplicada", Inicio.AddHours(1)));

        var reintento = jornada.ReintentarInicio(original.Id, "Conflicto", Inicio.AddHours(1));

        Assert.NotEqual(original.Id, reintento.Id);
        Assert.Equal(2, reintento.Secuencia);
        Assert.Equal(TipoOperacionLocal.InicioJornada, reintento.Tipo);
        Assert.Equal(original, jornada.Operaciones[0]);
        Assert.Equal(reintento, Assert.Single(jornada.PrepararEnvio()));
        Assert.Throws<InvalidOperationException>(() => jornada.ReintentarInicio(original.Id, "Conflicto", Inicio.AddHours(2)));

        var restaurada = JornadaLocal.Restaurar(jornada.ExportarEstado(), Cuenta);
        Assert.Equal(new[] { 1L, 2L }, restaurada.Operaciones.Select(x => x.Secuencia));
        Assert.Equal(original.Id, restaurada.Operaciones[0].Id);
        Assert.Equal(reintento.Id, restaurada.Operaciones[1].Id);
    }

    [Fact]
    public void PruebaCompletadaAntesDelCierreSobreviveReinicioYSincronizacionTardia()
    {
        var jornada = NuevaJornada();
        jornada.RegistrarEntregaFisica(Envio, Datos(), Inicio.AddHours(1));
        var prueba = jornada.AdjuntarEvidencia(Envio, Datos(), Inicio.AddHours(7));
        jornada.Finalizar(Inicio.AddHours(8));
        jornada.ConfirmarPersistencia(jornada.Operaciones[0].Id);

        var restaurada = SerializadorJornadaLocal.Deserializar(
            SerializadorJornadaLocal.Serializar(jornada), Cuenta);

        Assert.True(restaurada.EstaCerrada);
        Assert.True(restaurada.TienePendientes);
        Assert.Equal(jornada.Operaciones.Select(x => x.Id), restaurada.Operaciones.Select(x => x.Id));
        Assert.Equal(prueba.OcurridaEn, restaurada.Operaciones[2].OcurridaEn);
        Assert.Equal(TimeSpan.FromHours(-3), restaurada.Operaciones[2].OcurridaEn.Offset);
        Assert.Equal(new[] { 2L, 3L }, restaurada.PrepararEnvio().Select(x => x.Secuencia));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoInsertaEntregaNiPruebaDespuesDelCierre(bool esPrueba)
    {
        var jornada = NuevaJornada();
        jornada.Finalizar(Inicio.AddHours(8));

        Assert.Throws<InvalidOperationException>(() =>
        {
            if (esPrueba)
                jornada.AdjuntarEvidencia(Envio, Datos(), Inicio.AddHours(7));
            else
                jornada.RegistrarEntregaFisica(Envio, Datos(), Inicio.AddHours(7));
        });
        Assert.Equal(2, jornada.Operaciones.Count);
    }

    [Fact]
    public void SecuenciaCausalNoDependeDeCambiosDelRelojDelTelefono()
    {
        var jornada = NuevaJornada();
        var entrega = jornada.RegistrarEntregaFisica(Envio, Datos(), Inicio.AddMinutes(-5));
        var cierre = jornada.Finalizar(Inicio.AddMinutes(-10));

        Assert.Equal(2, entrega.Secuencia);
        Assert.Equal(3, cierre.Secuencia);
        Assert.Equal(Inicio.AddMinutes(-5), entrega.OcurridaEn);
        Assert.Equal(new[] { 1L, 2L }, jornada.PrepararEnvio().Select(x => x.Secuencia));
    }

    [Fact]
    public void CopiaDatosParaQueSobrevivanALaLiberacionDelDocumentoOriginal()
    {
        var jornada = NuevaJornada();
        OperacionLocal entrega;
        using (var documento = JsonDocument.Parse("{\"pruebaCompleta\":false}"))
            entrega = jornada.RegistrarEntregaFisica(Envio, documento.RootElement, Inicio.AddHours(1));

        Assert.False(entrega.Datos.GetProperty("pruebaCompleta").GetBoolean());
        Assert.NotEmpty(SerializadorJornadaLocal.Serializar(jornada));
    }

    [Fact]
    public void ConfirmacionAjenaNoLimpiaOperacionesPendientes()
    {
        var jornada = NuevaJornada();
        Assert.Throws<ArgumentException>(() => jornada.ConfirmarPersistencia(Guid.CreateVersion7()));
        Assert.True(jornada.TienePendientes);
        Assert.Empty(jornada.ExportarEstado().Confirmadas);
    }

    [Fact]
    public void ExportarEstadoNoExponeLasColeccionesInternas()
    {
        var jornada = NuevaJornada();
        var estado = jornada.ExportarEstado();
        ((OperacionLocal[])estado.Operaciones)[0] = null!;

        Assert.NotNull(jornada.Operaciones[0]);
        Assert.Single(jornada.PrepararEnvio());
    }

    [Theory]
    [InlineData("usuario")]
    [InlineData("acceso")]
    [InlineData("operador")]
    public void NoRestauraDatosDeOtraCuenta(string campo)
    {
        var otra = new CuentaMovil(campo == "usuario" ? Guid.CreateVersion7() : Cuenta.UsuarioId,
            campo == "acceso" ? Guid.CreateVersion7() : Cuenta.AccesoId,
            campo == "operador" ? Guid.CreateVersion7() : Cuenta.OperadorId);

        Assert.Throws<InvalidDataException>(() => SerializadorJornadaLocal.Deserializar(
            SerializadorJornadaLocal.Serializar(NuevaJornada()), otra));
    }

    [Theory]
    [InlineData("version")]
    [InlineData("sinInicio")]
    [InlineData("secuencia")]
    [InlineData("idRepetido")]
    [InlineData("cierreIntermedio")]
    [InlineData("confirmacionAjena")]
    [InlineData("confirmacionRepetida")]
    [InlineData("cierreConfirmadoAntes")]
    public void RechazaEstadoIncompatibleOCorruptoSinRecuperarloSilenciosamente(string caso)
    {
        var jornada = NuevaJornada();
        var entrega = jornada.RegistrarEntregaFisica(Envio, Datos(), Inicio.AddHours(1));
        var cierre = jornada.Finalizar(Inicio.AddHours(8));
        var estado = jornada.ExportarEstado();
        var inicio = estado.Operaciones[0];
        estado = caso switch
        {
            "version" => estado with { Version = 2 },
            "sinInicio" => estado with { Operaciones = [entrega, cierre] },
            "secuencia" => estado with { Operaciones = [inicio, NuevaOperacion(entrega.Id, 7), cierre] },
            "idRepetido" => estado with { Operaciones = [inicio, NuevaOperacion(inicio.Id, 2), cierre] },
            "cierreIntermedio" => estado with { Operaciones = [inicio, cierre, entrega] },
            "confirmacionAjena" => estado with { Confirmadas = [Guid.CreateVersion7()] },
            "confirmacionRepetida" => estado with { Confirmadas = [inicio.Id, inicio.Id] },
            "cierreConfirmadoAntes" => estado with { Confirmadas = [cierre.Id] },
            _ => throw new InvalidOperationException()
        };

        Assert.Throws<InvalidDataException>(() => JornadaLocal.Restaurar(estado, Cuenta));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"version\":1")]
    [InlineData("{\"version\":1,\"campoDesconocido\":true}")]
    public void NoAceptaJsonNuloTruncadoOConCamposDesconocidos(string json)
        => Assert.Throws<JsonException>(() => SerializadorJornadaLocal.Deserializar(
            System.Text.Encoding.UTF8.GetBytes(json), Cuenta));

    [Fact]
    public void CoreNoDependeDeMauiNiDeLasCapasDelBackend()
    {
        var dependencias = typeof(JornadaLocal).Assembly.GetReferencedAssemblies().Select(x => x.Name!).ToArray();
        Assert.DoesNotContain(dependencias, x => x.StartsWith("Microsoft.Maui", StringComparison.Ordinal)
            || x.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
            || x.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
            || x is "NexoRuta.Domain" or "NexoRuta.Application" or "NexoRuta.Infrastructure" or "NexoRuta.Api");
    }

    private static JornadaLocal NuevaJornada()
        => JornadaLocal.Iniciar(Guid.CreateVersion7(), Guid.CreateVersion7(), Cuenta, Inicio);

    private static JsonElement Datos()
    {
        using var documento = JsonDocument.Parse("{\"pruebaCompleta\":false}");
        return documento.RootElement.Clone();
    }

    private static OperacionLocal NuevaOperacion(Guid id, long secuencia)
        => new(id, secuencia, TipoOperacionLocal.EntregaFisica, Envio, Inicio.AddHours(1), Datos());
}
