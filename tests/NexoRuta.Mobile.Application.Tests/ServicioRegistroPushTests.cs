using NexoRuta.Mobile.Api.Identidad;
using NexoRuta.Mobile.Application.Notificaciones;
using NexoRuta.Mobile.Core.Jornadas;

namespace NexoRuta.Mobile.Application.Tests;

public sealed class ServicioRegistroPushTests
{
    private static readonly string Fid = new('a', 32);

    [Fact]
    public async Task CallbackTardioActualizaEstadoDespuesDeConfirmacionApi()
    {
        var registro = new RegistroFalso();
        var almacen = new AlmacenFidFalso();
        var instalaciones = new InstalacionesFalsas(Cuenta());
        var servicio = Crear(registro, almacen, instalaciones);
        var cambios = 0;
        servicio.EstadoCambiado += () => cambios++;

        await servicio.ActivarAsync();
        Assert.Equal("Activando las notificaciones…", servicio.Estado);
        Assert.Empty(instalaciones.Registros);

        await servicio.RegistroRecibidoAsync(Fid);

        Assert.Equal("Notificaciones activadas.", servicio.Estado);
        Assert.Equal(Fid, instalaciones.Registros.Single().Fid);
        Assert.True(cambios >= 2);
    }

    [Fact]
    public async Task ErrorDeApiNoAnunciaExitoYElFidDurableSePuedeReintentar()
    {
        var registro = new RegistroFalso();
        var almacen = new AlmacenFidFalso();
        var instalaciones = new InstalacionesFalsas(Cuenta()) { FallosRestantes = 1 };
        var servicio = Crear(registro, almacen, instalaciones);

        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.RegistroRecibidoAsync(Fid));
        Assert.Contains("No pudimos guardar", servicio.Estado);
        Assert.DoesNotContain("activadas", servicio.Estado);

        Assert.True(await servicio.ActualizarRegistroAsync());
        Assert.Equal("Notificaciones activadas.", servicio.Estado);
        Assert.Equal(2, instalaciones.Registros.Count);
    }

    [Fact]
    public async Task RespuestaDeCuentaAnteriorRegistraCuentaYFidActualesAntesDeMostrarExito()
    {
        var cuentaAnterior = Cuenta();
        var cuentaActual = Cuenta();
        var registro = new RegistroFalso();
        var almacen = new AlmacenFidFalso { Fid = Fid };
        var instalaciones = new InstalacionesFalsas(cuentaAnterior);
        var servicio = Crear(registro, almacen, instalaciones);
        instalaciones.DespuesDelSiguienteRegistro = () =>
        {
            instalaciones.Cuenta = cuentaActual;
            almacen.Fid = new string('b', 32);
        };

        Assert.True(await servicio.ActualizarRegistroAsync());

        Assert.Equal(2, instalaciones.Registros.Count);
        Assert.Equal(cuentaAnterior, instalaciones.Registros[0].Cuenta);
        Assert.Equal(Fid, instalaciones.Registros[0].Fid);
        Assert.Equal(cuentaActual, instalaciones.Registros[1].Cuenta);
        Assert.Equal(new string('b', 32), instalaciones.Registros[1].Fid);
        Assert.Equal("Notificaciones activadas.", servicio.Estado);
    }

    [Fact]
    public async Task PermisoDenegadoSeSincronizaYSeMuestraComoDenegado()
    {
        var registro = new RegistroFalso { Permiso = false };
        var almacen = new AlmacenFidFalso { Fid = Fid };
        var instalaciones = new InstalacionesFalsas(Cuenta());
        var servicio = Crear(registro, almacen, instalaciones);

        await servicio.ActivarAsync();

        Assert.False(instalaciones.Registros.Single().Permitida);
        Assert.Contains("están desactivadas", servicio.Estado);
    }

    [Fact]
    public async Task FalloDeSincronizacionNoOmiteElIntentoIndependienteDeRegistroPush()
    {
        var llamadasPush = 0;

        await TrabajoConRegistroPushIndependiente.EjecutarAsync(
            () => throw new InvalidOperationException("fallo controlado"),
            () => { llamadasPush++; return Task.CompletedTask; });

        Assert.Equal(1, llamadasPush);
    }

    private static ServicioRegistroPush Crear(RegistroFalso registro, AlmacenFidFalso almacen, InstalacionesFalsas instalaciones)
        => new(registro, almacen, instalaciones, "Android");

    private static CuentaMovil Cuenta() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

    private sealed class RegistroFalso : IRegistroPush
    {
        public string? MotivoNoDisponible { get; set; }
        public bool Permiso { get; set; } = true;
        public Task<bool> SolicitarPermisoAsync() => Task.FromResult(Permiso);
        public Task<bool> PermitidoAsync() => Task.FromResult(Permiso);
        public Task RegistrarAsync() => Task.CompletedTask;
    }

    private sealed class AlmacenFidFalso : IAlmacenFidPush
    {
        public string? Fid { get; set; }
        public Task<string?> LeerAsync() => Task.FromResult(Fid);
        public Task GuardarAsync(string fid) { Fid = fid; return Task.CompletedTask; }
    }

    private sealed class InstalacionesFalsas(CuentaMovil? cuenta) : IRegistroInstalacionPush
    {
        public CuentaMovil? Cuenta { get; set; } = cuenta;
        public int FallosRestantes { get; set; }
        public Action? DespuesDelSiguienteRegistro { get; set; }
        public List<(CuentaMovil Cuenta, string Plataforma, string Fid, bool Permitida)> Registros { get; } = [];

        public Task<CuentaMovil?> LeerCuentaActualAsync(CancellationToken ct = default)
            => Task.FromResult(Cuenta);

        public Task RegistrarAsync(CuentaMovil cuenta, string plataforma, string fid, bool permitida, CancellationToken ct = default)
        {
            Registros.Add((cuenta, plataforma, fid, permitida));
            var callback = DespuesDelSiguienteRegistro;
            DespuesDelSiguienteRegistro = null;
            callback?.Invoke();
            if (FallosRestantes > 0)
            {
                FallosRestantes--;
                throw new InvalidOperationException("fallo HTTP controlado");
            }
            return Task.CompletedTask;
        }
    }
}
