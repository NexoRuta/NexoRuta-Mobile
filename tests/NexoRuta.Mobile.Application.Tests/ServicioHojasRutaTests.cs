using System.Net;
using System.Text;
using System.Text.Json;
using NexoRuta.Mobile.Api.Identidad;
using NexoRuta.Mobile.Core.Jornadas;
using NexoRuta.Mobile.Core.Rutas;
using NexoRuta.Mobile.Storage;
namespace NexoRuta.Mobile.Application.Tests;

public sealed class ServicioHojasRutaTests : IDisposable
{
    private readonly string raiz = Path.Combine(Path.GetTempPath(), "nexoruta-hojas-" + Guid.NewGuid().ToString("N"));
    private readonly CuentaMovil cuenta = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
    private readonly RepositorioJornadasSqlite datos;
    private readonly Almacen sesion;
    public ServicioHojasRutaTests()
    {
        datos = new(raiz, new Claves());
        sesion = new(new() { Api = "https://api.example.test/", Cuenta = cuenta, Tokens = new()
        { AccessToken = "jwt-prueba", TokenType = "Bearer", RefreshToken = new string('a', 43), SesionId = Guid.NewGuid(),
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1), RefreshExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(30) } });
    }
    [Fact]
    public async Task ActualizarUnaJornadaConTrabajoPendienteConservaSuBaselineYDiario()
    {
        var original = Hoja(Guid.NewGuid(), Guid.NewGuid(), 1);
        await datos.GuardarHojaAsync(cuenta, original.Id, SerializadorHojaRuta.Serializar(original));
        var jornada = JornadaLocal.Iniciar(original.JornadaId, original.Id, cuenta, DateTimeOffset.UtcNow);
        await datos.CrearAsync(jornada);
        var remota = Hoja(original.Id, original.JornadaId, 4);
        using var cliente = Cliente((_, _) => Task.FromResult(Respuesta([remota])));
        await new ServicioHojasRuta(cliente, datos).ActualizarAsync(cuenta, original.Fecha);
        Assert.Equal(SerializadorHojaRuta.Serializar(original), await datos.LeerHojaAsync(cuenta, original.Id));
        Assert.Equal(SerializadorHojaRuta.Serializar(remota), await datos.LeerVistaRemotaAsync(cuenta, original.Id));
        Assert.True((await datos.LeerAsync(cuenta, jornada.Id))!.TienePendientes);
    }
    [Fact]
    public async Task FalloDeConexionNoBorraLaHojaDisponible()
    {
        var original = Hoja(Guid.NewGuid(), Guid.NewGuid(), 1); var bytes = SerializadorHojaRuta.Serializar(original);
        await datos.GuardarHojaAsync(cuenta, original.Id, bytes);
        using var cliente = Cliente((_, _) => throw new HttpRequestException("API inaccesible"));
        await Assert.ThrowsAsync<HttpRequestException>(() => new ServicioHojasRuta(cliente, datos).ActualizarAsync(cuenta, original.Fecha));
        Assert.Equal(bytes, await datos.LeerHojaAsync(cuenta, original.Id));
    }
    [Fact]
    public async Task RespuestaConHojaAjenaNoGuardaNiLaParteValida()
    {
        var valida = Hoja(Guid.NewGuid(), Guid.NewGuid(), 1);
        var ajena = new HojaRuta { Id = Guid.NewGuid(), RepartidorAccesoId = Guid.NewGuid() };
        using var cliente = Cliente((_, _) => Task.FromResult(Respuesta([valida, ajena])));
        await Assert.ThrowsAsync<InvalidDataException>(() => new ServicioHojasRuta(cliente, datos).ActualizarAsync(cuenta, valida.Fecha));
        Assert.Empty(await datos.LeerHojasAsync(cuenta));
    }
    [Fact]
    public async Task CuentaEsperadaDistintaImpideInclusoLaConsultaHttp()
    {
        using var cliente = Cliente((_, _) => throw new Exception("No debe consultar"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ServicioHojasRuta(cliente, datos)
            .ActualizarAsync(new(Guid.NewGuid(), Guid.NewGuid(), cuenta.OperadorId), DateOnly.FromDateTime(DateTime.Today)));
    }
    [Fact]
    public async Task RecibeYConservaHojaNuevaSinExigirDescargaManualDelRepartidor()
    {
        var hoja = Hoja(Guid.NewGuid(), Guid.NewGuid(), 1);
        using var cliente = Cliente((_, _) => Task.FromResult(Respuesta([hoja])));
        await new ServicioHojasRuta(cliente, datos).ActualizarAsync(cuenta, hoja.Fecha);
        Assert.Single(await datos.LeerHojasAsync(cuenta));
        Assert.Equal(SerializadorHojaRuta.Serializar(hoja), await datos.LeerVistaRemotaAsync(cuenta, hoja.Id));
    }
    private HojaRuta Hoja(Guid id, Guid jornada, int version) => new()
    {
        Id = id, JornadaId = jornada, VehiculoId = Guid.NewGuid(), RepartidorAccesoId = cuenta.AccesoId,
        Fecha = DateOnly.FromDateTime(DateTime.Today), Estado = "Despachada", Version = version,
        Politica = new() { Version = 1, ZonaHoraria = "America/Montevideo" },
        Paradas = [new() { EnvioId = Guid.NewGuid(), Orden = 1, Activa = true, Direccion = "Destino ficticio", Latitud = -34.9, Longitud = -56.2,
            Reglas = new() { VersionId = Guid.NewGuid(), NumeroVersion = 1, MaximoIntentos = 3, IntervaloEntreIntentosMinutos = 30,
                PoliticaDevolucionAlAgotarIntentos = "DecisionOperativa", RequiereNombre = true, Motivos = [new("AUSENTE", "Ausente")] },
            Bultos = [new() { Id = Guid.NewGuid(), Codigo = "PRUEBA" }] }]
    };
    private static HttpResponseMessage Respuesta(HojaRuta[] hojas) => new(HttpStatusCode.OK)
    { Content = new StringContent(JsonSerializer.Serialize(hojas, HojaRutaJson.Default.HojaRutaArray), Encoding.UTF8, "application/json") };
    private ClienteSesionMovil Cliente(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) =>
        new(new("https://api.example.test/"), sesion, new Transporte(responder));
    private sealed class Transporte(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => responder(request, ct); }
    private sealed class Almacen(EstadoSesionMovil estado) : IAlmacenSesionMovil
    {
        public Task<EstadoSesionMovil?> LeerAsync(string api) => Task.FromResult<EstadoSesionMovil?>(estado);
        public Task GuardarAsync(EstadoSesionMovil nuevo) { estado = nuevo; return Task.CompletedTask; }
        public Task EliminarAsync(string api) => throw new NotSupportedException();
    }
    private sealed class Claves : IAlmacenClavesProtegidas
    {
        private readonly Dictionary<string, string> valores = [];
        public Task<string?> ObtenerAsync(string nombre) => Task.FromResult(valores.GetValueOrDefault(nombre));
        public Task GuardarAsync(string nombre, string valor) { valores[nombre] = valor; return Task.CompletedTask; }
    }
    public void Dispose() { datos.Dispose(); if (Directory.Exists(raiz)) Directory.Delete(raiz, true); }
}
