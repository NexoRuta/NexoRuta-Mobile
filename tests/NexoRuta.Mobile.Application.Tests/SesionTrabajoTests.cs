using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NexoRuta.Mobile.Api.Identidad;
using NexoRuta.Mobile.Core.Jornadas;
using NexoRuta.Mobile.Storage;
namespace NexoRuta.Mobile.Application.Tests;

public sealed class SesionTrabajoTests
{
    [Fact]
    public async Task OperacionPendienteBloqueaSalidaSinHttpYSinCongelarElTrabajo()
    {
        using var f = new Escenario(); await f.Crear(confirmada: false);
        using var cliente = f.Cliente((_, _) => throw new InvalidOperationException("No enviar"));
        using var avisos = new ServicioAvisosReparto(cliente, f.Datos);
        var salida = new ServicioSesionTrabajo(cliente, f.Datos, avisos);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => salida.CerrarAsync(f.Cuenta));
        Assert.Contains("Sincronizá", ex.Message);
        Assert.NotNull(f.Sesion.Estado);
        await f.Datos.ModificarAsync(f.Cuenta, f.Jornada.Id, j => j.RegistrarEntregaFisica(Guid.NewGuid(), Elemento("{}"), DateTimeOffset.UtcNow));
        Assert.Equal(2, (await f.Datos.LeerAsync(f.Cuenta, f.Jornada.Id))!.Operaciones.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BorradorDeEvidenciaBloqueaSalidaYConservaElArchivo(bool completa)
    {
        using var f = new Escenario(); await f.Crear(confirmada: true);
        var captura = await f.Datos.IniciarCapturaAsync(f.Cuenta, f.Jornada.Id, Guid.NewGuid(), "Foto");
        await f.Archivos.GuardarAsync(f.Cuenta, captura.Id, "evidencia cifrada"u8.ToArray());
        if (completa) await f.Datos.MarcarCapturaListaAsync(f.Cuenta, captura.Id);
        using var cliente = f.Cliente((_, _) => throw new InvalidOperationException("No enviar"));
        using var avisos = new ServicioAvisosReparto(cliente, f.Datos);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ServicioSesionTrabajo(cliente, f.Datos, avisos).CerrarAsync(f.Cuenta));
        Assert.Equal("evidencia cifrada"u8.ToArray(), await f.Archivos.LeerAsync(f.Cuenta, captura.Id));
        Assert.Single(await f.Datos.LeerCapturasAsync(f.Cuenta, f.Jornada.Id));
        Assert.NotNull(f.Sesion.Estado);
    }

    [Fact]
    public async Task SalidaSuspendeNuevasOperacionesHastaReingresoYConservaDiarioYCifrado()
    {
        using var f = new Escenario(); await f.Crear(confirmada: true);
        var anterior = SerializadorJornadaLocal.Serializar(f.Jornada);
        var id = Guid.NewGuid(); var archivo = Guid.NewGuid();
        await f.Datos.GuardarMetadatoAsync(f.Cuenta, "instalacion-push.v1", Encoding.UTF8.GetBytes(id.ToString()));
        await f.Archivos.GuardarAsync(f.Cuenta, archivo, "prueba anterior"u8.ToArray());
        var enSalida = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var seguir = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cliente = f.Cliente(async (r, ct) =>
        {
            if (r.Method == HttpMethod.Delete)
            {
                Assert.Equal($"/api/mobile/instalaciones/{id}", r.RequestUri!.AbsolutePath);
                enSalida.SetResult(); await seguir.Task.WaitAsync(ct);
            }
            return new(HttpStatusCode.NoContent);
        });
        using var avisos = new ServicioAvisosReparto(cliente, f.Datos);
        var salir = new ServicioSesionTrabajo(cliente, f.Datos, avisos).CerrarAsync(f.Cuenta);
        await enSalida.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Datos.ModificarAsync(f.Cuenta, f.Jornada.Id,
            j => j.RegistrarPosicion(Elemento("{}"), DateTimeOffset.UtcNow)));
        seguir.SetResult(); await salir;
        Assert.Null(f.Sesion.Estado);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Datos.CrearAsync(JornadaLocal.Iniciar(Guid.NewGuid(), Guid.NewGuid(), f.Cuenta, DateTimeOffset.UtcNow)));
        Assert.Empty(await f.Datos.LeerJornadasAsync(new CuentaMovil(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid())));
        await f.Datos.ReanudarCuentaAsync(f.Cuenta);
        Assert.Equal(anterior, SerializadorJornadaLocal.Serializar((await f.Datos.LeerAsync(f.Cuenta, f.Jornada.Id))!));
        Assert.Equal("prueba anterior"u8.ToArray(), await f.Archivos.LeerAsync(f.Cuenta, archivo));
    }

    [Fact]
    public async Task RespuestaPerdidaReanudaLaCuentaSinBorrarJornada()
    {
        using var f = new Escenario(); await f.Crear(confirmada: true);
        using var cliente = f.Cliente((_, _) => throw new HttpRequestException("Se perdió la respuesta"));
        using var avisos = new ServicioAvisosReparto(cliente, f.Datos);
        await Assert.ThrowsAsync<HttpRequestException>(() => new ServicioSesionTrabajo(cliente, f.Datos, avisos).CerrarAsync(f.Cuenta));
        await f.Datos.ModificarAsync(f.Cuenta, f.Jornada.Id, j => j.RegistrarEntregaFisica(Guid.NewGuid(), Elemento("{}"), DateTimeOffset.UtcNow));
        Assert.True(await f.Datos.TienePendientesAsync(f.Cuenta)); Assert.NotNull(f.Sesion.Estado);
    }

    [Fact]
    public async Task CancelarSalidaReanudaElAlmacenAunqueElTokenEsteCancelado()
    {
        using var f = new Escenario(); await f.Crear(confirmada: true);
        using var cancelar = new CancellationTokenSource();
        using var cliente = f.Cliente((_, ct) =>
        {
            cancelar.Cancel(); ct.ThrowIfCancellationRequested();
            throw new InvalidOperationException("No continuar");
        });
        using var avisos = new ServicioAvisosReparto(cliente, f.Datos);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ServicioSesionTrabajo(cliente, f.Datos, avisos).CerrarAsync(f.Cuenta, cancelar.Token));
        Assert.NotNull(await f.Datos.LeerAsync(f.Cuenta, f.Jornada.Id)); Assert.NotNull(f.Sesion.Estado);
    }

    [Fact]
    public async Task ClavePerdidaBloqueaSalidaSinCrearOtraNiModificarLaBase()
    {
        using var f = new Escenario(); await f.Crear(confirmada: true);
        var path = Directory.GetFiles(f.Raiz, "operacion.sqlite", SearchOption.AllDirectories).Single();
        var hash = SHA256.HashData(await File.ReadAllBytesAsync(path)); f.Claves.Valores.Clear();
        using var cliente = f.Cliente((_, _) => throw new InvalidOperationException("No enviar"));
        using var avisos = new ServicioAvisosReparto(cliente, f.Datos);
        await Assert.ThrowsAsync<ClaveLocalNoDisponibleException>(() => new ServicioSesionTrabajo(cliente, f.Datos, avisos).CerrarAsync(f.Cuenta));
        Assert.Empty(f.Claves.Valores); Assert.Equal(hash, SHA256.HashData(await File.ReadAllBytesAsync(path)));
        Assert.NotNull(f.Sesion.Estado);
    }

    private static JsonElement Elemento(string json) { using var d = JsonDocument.Parse(json); return d.RootElement.Clone(); }
    private sealed class Escenario : IDisposable
    {
        internal readonly string Raiz = Path.Combine(Path.GetTempPath(), "nexoruta-salida-" + Guid.NewGuid().ToString("N"));
        internal readonly CuentaMovil Cuenta = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        internal readonly Claves Claves = new();
        internal readonly Almacen Sesion;
        internal readonly RepositorioJornadasSqlite Datos;
        internal readonly RepositorioArchivosCifrados Archivos;
        internal readonly JornadaLocal Jornada;
        internal Escenario()
        {
            Datos = new(Raiz + "/sql", Claves); Archivos = new(Raiz + "/archivos", Claves);
            Jornada = JornadaLocal.Iniciar(Guid.NewGuid(), Guid.NewGuid(), Cuenta, DateTimeOffset.UtcNow);
            Sesion = new(new() { Api = "https://api.example.test/", Cuenta = Cuenta, Tokens = new()
            { AccessToken = "jwt-de-prueba", TokenType = "Bearer", RefreshToken = new string('a', 43), SesionId = Guid.NewGuid(),
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1), RefreshExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(30) } });
        }
        internal Task Crear(bool confirmada)
        { if (confirmada) Jornada.ConfirmarPersistencia(Jornada.Operaciones.Single().Id); return Datos.CrearAsync(Jornada); }
        internal ClienteSesionMovil Cliente(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) => new(new("https://api.example.test/"), Sesion, new Transporte(responder));
        public void Dispose() { Datos.Dispose(); Archivos.Dispose(); if (Directory.Exists(Raiz)) Directory.Delete(Raiz, true); }
    }
    private sealed class Claves : IAlmacenClavesProtegidas
    {
        internal readonly Dictionary<string, string> Valores = [];
        public Task<string?> ObtenerAsync(string nombre) => Task.FromResult(Valores.GetValueOrDefault(nombre));
        public Task GuardarAsync(string nombre, string valor) { Valores[nombre] = valor; return Task.CompletedTask; }
    }
    private sealed class Almacen(EstadoSesionMovil? estado) : IAlmacenSesionMovil
    {
        internal EstadoSesionMovil? Estado => estado;
        public Task<EstadoSesionMovil?> LeerAsync(string api) => Task.FromResult(estado);
        public Task GuardarAsync(EstadoSesionMovil nuevo) { estado = nuevo; return Task.CompletedTask; }
        public Task EliminarAsync(string api) { estado = null; return Task.CompletedTask; }
    }
    private sealed class Transporte(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => responder(request, ct); }
}
