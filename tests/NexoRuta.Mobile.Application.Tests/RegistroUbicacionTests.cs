using System.Text.Json;
using NexoRuta.Mobile.Api.Identidad;
using NexoRuta.Mobile.Api.Reparto;
using NexoRuta.Mobile.Application.Ubicacion;
using NexoRuta.Mobile.Core.Jornadas;
using NexoRuta.Mobile.Storage;

namespace NexoRuta.Mobile.Application.Tests;

public sealed class RegistroUbicacionTests : IDisposable
{
    private readonly string raiz = Path.Combine(Path.GetTempPath(), "nexoruta-gps-" + Guid.NewGuid().ToString("N"));
    private readonly CuentaMovil cuenta = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
    private readonly Claves claves = new();
    private readonly RepositorioJornadasSqlite datos;
    private readonly Sesion sesion = new();
    private readonly DateTimeOffset inicio = DateTimeOffset.UtcNow.AddMinutes(-10);

    public RegistroUbicacionTests()
    {
        datos = new(raiz, claves);
        sesion.Estado = new() { Api = "https://api.example.test/", Cuenta = cuenta, Tokens = null };
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Conflicto")]
    [InlineData("Rechazada")]
    public async Task NoRegistraAntesDeQueElServidorAutoriceElInicio(string? resultado)
    {
        var jornada = await Crear(resultado);
        using var registro = new RegistroUbicacion(sesion, datos);
        Assert.False(await registro.TieneActivaAsync());
        Assert.False(await registro.RegistrarAsync(-34.9, -56.2, 10, inicio.AddMinutes(1), default));
        Assert.Single((await datos.LeerAsync(cuenta, jornada.Id))!.Operaciones);
    }

    [Fact]
    public async Task SinTokensNiRedConservaPuntosCifradosYElIntervaloTrasReabrir()
    {
        var jornada = await Crear();
        var fecha = inicio.AddMinutes(1);
        using (var registro = new RegistroUbicacion(sesion, datos))
        {
            Assert.True(await registro.TieneActivaAsync());
            Assert.True(await registro.RegistrarAsync(-34.9, -56.2, 10, fecha, default));
        }
        using var reapertura = new RepositorioJornadasSqlite(raiz, claves);
        using var reinicio = new RegistroUbicacion(sesion, reapertura);
        // GPS y red pueden entregar el mismo punto y callbacks desordenados tras reiniciar.
        foreach (var segundos in new[] { 0, -1, 29 })
            Assert.True(await reinicio.RegistrarAsync(-34.9, -56.2, 10, fecha.AddSeconds(segundos), default));
        Assert.True(await reinicio.RegistrarAsync(-34.91, -56.21, 12, fecha.AddSeconds(30), default));
        var guardada = (await reapertura.LeerAsync(cuenta, jornada.Id))!;
        var puntos = guardada.Operaciones.Where(x => x.Tipo == TipoOperacionLocal.Posicion).ToArray();
        Assert.Equal(2, puntos.Length);
        Assert.Equal(fecha, puntos[0].Datos.GetProperty("posicionEnUtc").GetDateTimeOffset());
        Assert.Equal(fecha.AddSeconds(30), puntos[1].Datos.GetProperty("posicionEnUtc").GetDateTimeOffset());
        Assert.True(guardada.TienePendientes);
        Assert.DoesNotContain(Directory.GetFiles(raiz, "*", SearchOption.AllDirectories),
            p => File.ReadAllText(p).Contains("posicionEnUtc", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PosicionAnteriorAlInicioNoSeReutilizaComoMedicionNueva()
    {
        var jornada = await Crear();
        using var registro = new RegistroUbicacion(sesion, datos);
        Assert.True(await registro.RegistrarAsync(-34.9, -56.2, 10, inicio.AddSeconds(-1), default));
        Assert.Single((await datos.LeerAsync(cuenta, jornada.Id))!.Operaciones);
        Assert.True(await registro.RegistrarAsync(-34.9, -56.2, 10, inicio, default));
        Assert.Equal(2, (await datos.LeerAsync(cuenta, jornada.Id))!.Operaciones.Count);
    }

    [Fact]
    public async Task DespuesDelCierreNoRegistraNiReanudaInclusoConUnCallbackInvalido()
    {
        var jornada = await Crear();
        await datos.ModificarAsync(cuenta, jornada.Id, j => j.Finalizar(inicio.AddMinutes(2)));
        using var registro = new RegistroUbicacion(sesion, datos);
        Assert.False(await registro.TieneActivaAsync());
        Assert.False(await registro.RegistrarAsync(-34.9, -56.2, 10, inicio.AddMinutes(3), default));
        Assert.False(await registro.RegistrarAsync(double.NaN, -56.2, 10, inicio.AddMinutes(3), default));
        Assert.Equal(2, (await datos.LeerAsync(cuenta, jornada.Id))!.Operaciones.Count);
    }

    [Fact]
    public async Task SinSesionOConOtraCuentaNoRegistraEnLaJornadaAnterior()
    {
        var jornada = await Crear();
        using var registro = new RegistroUbicacion(sesion, datos);
        sesion.Estado = null;
        Assert.False(await registro.RegistrarAsync(-34.9, -56.2, 10, inicio, default));
        sesion.Estado = new() { Api = "https://api.example.test/", Cuenta = new(Guid.NewGuid(), Guid.NewGuid(), cuenta.OperadorId) };
        Assert.False(await registro.TieneActivaAsync());
        Assert.False(await registro.RegistrarAsync(-34.9, -56.2, 10, inicio, default));
        Assert.Single((await datos.LeerAsync(cuenta, jornada.Id))!.Operaciones);
    }

    [Fact]
    public async Task DosCallbacksConcurrentesNoDuplicanElMismoPunto()
    {
        var jornada = await Crear();
        using var registro = new RegistroUbicacion(sesion, datos);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => registro.RegistrarAsync(-34.9, -56.2, 10, inicio, default)));
        Assert.Single((await datos.LeerAsync(cuenta, jornada.Id))!.Operaciones, x => x.Tipo == TipoOperacionLocal.Posicion);
    }

    [Fact]
    public async Task CoordenadasOPrecisionInvalidasNoInventanUnPunto()
    {
        var jornada = await Crear();
        using var registro = new RegistroUbicacion(sesion, datos);
        Assert.True(await registro.RegistrarAsync(double.NaN, -56.2, 10, inicio, default));
        Assert.True(await registro.RegistrarAsync(-34.9, 181, 10, inicio, default));
        Assert.True(await registro.RegistrarAsync(-34.9, -56.2, 0, inicio, default));
        Assert.Single((await datos.LeerAsync(cuenta, jornada.Id))!.Operaciones);
    }

    [Fact]
    public async Task DosJornadasAutorizadasNoPermitenAtribuirUnPuntoArbitrariamente()
    {
        var primera = await Crear(); var segunda = await Crear();
        using var registro = new RegistroUbicacion(sesion, datos);
        Assert.False(await registro.TieneActivaAsync());
        Assert.False(await registro.RegistrarAsync(-34.9, -56.2, 10, inicio, default));
        Assert.Single((await datos.LeerAsync(cuenta, primera.Id))!.Operaciones);
        Assert.Single((await datos.LeerAsync(cuenta, segunda.Id))!.Operaciones);
    }

    private async Task<JornadaLocal> Crear(string? resultado = "Aplicada")
    {
        var jornada = JornadaLocal.Iniciar(Guid.NewGuid(), Guid.NewGuid(), cuenta, inicio);
        await datos.CrearAsync(jornada);
        if (resultado is not null)
        {
            var op = jornada.Operaciones[0];
            await datos.GuardarSobreAsync(cuenta, jornada.Id, op.Id, "{}"u8.ToArray());
            await datos.ConfirmarSobreAsync(cuenta, jornada.Id, op.Id, JsonSerializer.SerializeToUtf8Bytes(
                new ConfirmacionOperacion { Id = op.Id, Secuencia = op.Secuencia, Resultado = resultado }, ContratosRepartoJson.Default.ConfirmacionOperacion));
        }
        return jornada;
    }
    private sealed class Sesion : IAlmacenSesionMovil
    {
        public EstadoSesionMovil? Estado { get; set; }
        public Task<EstadoSesionMovil?> LeerAsync() => Task.FromResult(Estado);
        public Task GuardarAsync(EstadoSesionMovil estado) { Estado = estado; return Task.CompletedTask; }
        public Task EliminarAsync() { Estado = null; return Task.CompletedTask; }
    }
    private sealed class Claves : IAlmacenClavesProtegidas
    {
        private readonly Dictionary<string, string> valores = [];
        public Task<string?> ObtenerAsync(string nombre) => Task.FromResult(valores.GetValueOrDefault(nombre));
        public Task GuardarAsync(string nombre, string valor) { valores[nombre] = valor; return Task.CompletedTask; }
    }
    public void Dispose() { datos.Dispose(); if (Directory.Exists(raiz)) Directory.Delete(raiz, true); }
}
