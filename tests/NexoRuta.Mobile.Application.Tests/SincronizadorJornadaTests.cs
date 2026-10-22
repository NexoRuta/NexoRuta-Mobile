using System.Net;
using System.Text;
using System.Text.Json;
using NexoRuta.Mobile.Api.Identidad;
using NexoRuta.Mobile.Api.Reparto;
using NexoRuta.Mobile.Core.Jornadas;
using NexoRuta.Mobile.Core.Rutas;
using NexoRuta.Mobile.Storage;
namespace NexoRuta.Mobile.Application.Tests;

public sealed class SincronizadorJornadaTests
{
    [Fact]
    public async Task ReconexionProcesaMasDeUnLoteYElCierreSinNecesitarGpsActivo()
    {
        using var f = new Escenario(); await f.Preparar(cerrar: false);
        await f.Datos.ModificarAsync(f.Cuenta, f.Jornada.Id, j =>
        {
            for (var i = 0; i < 105; i++)
                j.RegistrarPosicion(Elemento("{\"latitud\":-34.9,\"longitud\":-56.2,\"precisionMetros\":10}"), DateTimeOffset.UtcNow);
            j.Finalizar(DateTimeOffset.UtcNow);
        });
        var secuencias = new List<long>();
        using var cliente = f.Cliente(async (r, ct) =>
        {
            var comando = JsonSerializer.Deserialize(await r.Content!.ReadAsByteArrayAsync(ct), ContratosRepartoJson.Default.SobreOperacion)!;
            secuencias.Add(comando.Secuencia); return Confirmar(comando, f.Envio);
        });
        using var sync = new SincronizadorJornada(cliente, f.Datos, f.Archivos);
        Assert.Equal(110, await sync.SincronizarPendientesAsync(f.Cuenta));
        Assert.Equal(Enumerable.Range(1, 110).Select(i => (long)i), secuencias);
        Assert.False((await f.Datos.LeerAsync(f.Cuenta, f.Jornada.Id))!.TienePendientes);
        Assert.Equal(0, await sync.SincronizarPendientesAsync(f.Cuenta));
        Assert.Equal(110, secuencias.Count);
    }
    [Fact]
    public async Task RespuestaPerdidaReenviaLosMismosBytesTrasReabrirSinAdelantarElCierre()
    {
        using var f = new Escenario(); await f.Preparar();
        var recibidos = new Dictionary<Guid, byte[]>(); var secuencias = new List<long>(); var perder = true;
        using var cliente = f.Cliente(async (request, ct) =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal(HttpMethod.Post, request.Method);
            var bytes = await request.Content!.ReadAsByteArrayAsync(ct);
            var comando = JsonSerializer.Deserialize(bytes, ContratosRepartoJson.Default.SobreOperacion)!;
            if (recibidos.TryGetValue(comando.Id, out var previo)) Assert.Equal(previo, bytes);
            else { recibidos.Add(comando.Id, bytes); secuencias.Add(comando.Secuencia); }
            if (comando.Tipo == "EntregaFisica")
            {
                Assert.Equal(3, comando.VersionEnvio);
                if (perder) { perder = false; throw new HttpRequestException("Respuesta perdida tras el commit simulado"); }
            }
            return Confirmar(comando, f.Envio);
        });
        using (var sync = new SincronizadorJornada(cliente, f.Datos, f.Archivos))
            await Assert.ThrowsAsync<HttpRequestException>(() => sync.SincronizarAsync(f.Cuenta, f.Jornada.Id));
        Assert.True((await f.Datos.LeerAsync(f.Cuenta, f.Jornada.Id))!.TienePendientes);
        Assert.DoesNotContain(5, secuencias);
        using var reapertura = new RepositorioJornadasSqlite(f.Raiz + "/sql", f.Claves);
        using var reinicio = new SincronizadorJornada(cliente, reapertura, f.Archivos);
        Assert.Equal(2, await reinicio.SincronizarAsync(f.Cuenta, f.Jornada.Id));
        Assert.Equal(new long[] { 1, 2, 3, 4, 5 }, secuencias);
        Assert.False((await reapertura.LeerAsync(f.Cuenta, f.Jornada.Id))!.TienePendientes);
        Assert.Equal(5, (await reapertura.LeerConfirmacionesAsync(f.Cuenta, f.Jornada.Id)).Count);
    }

    [Fact]
    public async Task ConflictoSeConfirmaComoHechoDurableSinPromoverSuVersionRemota()
    {
        using var f = new Escenario(); await f.Preparar();
        using var cliente = f.Cliente(async (request, ct) =>
        {
            var comando = JsonSerializer.Deserialize(await request.Content!.ReadAsByteArrayAsync(ct), ContratosRepartoJson.Default.SobreOperacion)!;
            if (comando.Tipo != "EntregaFisica") return Confirmar(comando, f.Envio);
            return Json(new ConfirmacionOperacion { Id = comando.Id, Secuencia = comando.Secuencia, Resultado = "Conflicto",
                VersionEnvio = 99, VersionRuta = 9, Detalle = "Reasignado en el servidor" });
        });
        using var sync = new SincronizadorJornada(cliente, f.Datos, f.Archivos);
        Assert.Equal(5, await sync.SincronizarAsync(f.Cuenta, f.Jornada.Id));
        var confirmaciones = await f.Datos.LeerConfirmacionesAsync(f.Cuenta, f.Jornada.Id);
        var conflicto = JsonSerializer.Deserialize(confirmaciones[3], ContratosRepartoJson.Default.ConfirmacionOperacion)!;
        Assert.Equal("Conflicto", conflicto.Resultado); Assert.Empty(conflicto.VersionesEnvio);
        Assert.False((await f.Datos.LeerAsync(f.Cuenta, f.Jornada.Id))!.TienePendientes);
        Assert.Equal(5, (await f.Datos.LeerAsync(f.Cuenta, f.Jornada.Id))!.Operaciones.Count);
    }

    [Fact]
    public async Task ConfirmacionAjenaNoLimpiaPendientesNiElSobreCongelado()
    {
        using var f = new Escenario(); await f.Preparar();
        using var cliente = f.Cliente((request, ct) => Task.FromResult(Json(new ConfirmacionOperacion
            { Id = Guid.NewGuid(), Secuencia = 1, Resultado = "Aplicada", VersionRuta = 1 })));
        using var sync = new SincronizadorJornada(cliente, f.Datos, f.Archivos);
        await Assert.ThrowsAsync<InvalidDataException>(() => sync.SincronizarAsync(f.Cuenta, f.Jornada.Id));
        Assert.Empty(await f.Datos.LeerConfirmacionesAsync(f.Cuenta, f.Jornada.Id));
        Assert.NotNull(await f.Datos.LeerSobreAsync(f.Cuenta, f.Jornada.Id, f.Jornada.Operaciones[0].Id));
        Assert.True((await f.Datos.LeerAsync(f.Cuenta, f.Jornada.Id))!.TienePendientes);
    }

    [Fact]
    public async Task EvidenciaSeSubeAntesDelHechoYSuFalloConservaElArchivoYLaOperacion()
    {
        using var f = new Escenario(); await f.Preparar(conFoto: true);
        var envios = new List<string>(); var fallar = true;
        using var cliente = f.Cliente(async (request, ct) =>
        {
            if (request.Method == HttpMethod.Put)
            {
                envios.Add("archivo");
                var json = JsonDocument.Parse(await request.Content!.ReadAsByteArrayAsync(ct));
                Assert.Equal(f.Foto, json.RootElement.GetProperty("contenido").GetBytesFromBase64());
                if (fallar) { fallar = false; return new(HttpStatusCode.ServiceUnavailable); }
                return new(HttpStatusCode.NoContent);
            }
            var op = JsonSerializer.Deserialize(await request.Content!.ReadAsByteArrayAsync(ct), ContratosRepartoJson.Default.SobreOperacion)!;
            envios.Add(op.Tipo); return Confirmar(op, f.Envio);
        });
        using var sync = new SincronizadorJornada(cliente, f.Datos, f.Archivos);
        await Assert.ThrowsAsync<SesionMovilException>(() => sync.SincronizarAsync(f.Cuenta, f.Jornada.Id));
        Assert.DoesNotContain("EntregaFisica", envios);
        Assert.Equal(f.Foto, await f.Archivos.LeerAsync(f.Cuenta, f.FotoId));
        Assert.Equal(2, await sync.SincronizarAsync(f.Cuenta, f.Jornada.Id));
        Assert.True(envios.IndexOf("archivo") < envios.IndexOf("EntregaFisica"));
    }

    [Fact]
    public async Task DosIntentosConcurrentesNoRepitenOperacionesYaConfirmadas()
    {
        using var f = new Escenario(); await f.Preparar(); var envios = 0;
        using var cliente = f.Cliente(async (request, ct) =>
        {
            Interlocked.Increment(ref envios);
            var op = JsonSerializer.Deserialize(await request.Content!.ReadAsByteArrayAsync(ct), ContratosRepartoJson.Default.SobreOperacion)!;
            return Confirmar(op, f.Envio);
        });
        using var sync = new SincronizadorJornada(cliente, f.Datos, f.Archivos);
        var resultados = await Task.WhenAll(sync.SincronizarAsync(f.Cuenta, f.Jornada.Id), sync.SincronizarAsync(f.Cuenta, f.Jornada.Id));
        Assert.Equal(5, resultados.Sum()); Assert.Equal(5, envios);
    }

    [Fact]
    public async Task ArchivoGuardadoAntesDeInterrupcionSeRecuperaYSeAsociaAtomicamenteAlEvento()
    {
        using var f = new Escenario(); await f.Preparar(cerrar: false);
        var captura = await f.Datos.IniciarCapturaAsync(f.Cuenta, f.Jornada.Id, f.Envio, "Foto");
        await f.Archivos.GuardarAsync(f.Cuenta, captura.Id, f.Foto);
        var servicio = new EvidenciasLocales(f.Datos, f.Archivos);
        await servicio.RecuperarAsync(f.Cuenta, f.Jornada.Id);
        Assert.True(Assert.Single(await f.Datos.LeerCapturasAsync(f.Cuenta, f.Jornada.Id)).Lista);
        var prueba = Elemento($"{{\"fotoId\":\"{captura.Id}\"}}");
        await f.Datos.RegistrarConCapturasAsync(f.Cuenta, f.Jornada.Id, [captura.Id],
            j => j.AdjuntarEvidencia(f.Envio, prueba, DateTimeOffset.UtcNow));
        var asociada = Assert.Single(await f.Datos.LeerCapturasAsync(f.Cuenta, f.Jornada.Id));
        Assert.Equal((await f.Datos.LeerAsync(f.Cuenta, f.Jornada.Id))!.Operaciones[^1].Id, asociada.OperacionId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Datos.DescartarCapturaAsync(f.Cuenta, captura.Id));
    }

    [Fact]
    public async Task CapturaIncompletaPermanecePendienteYUnaAsociacionInvalidaNoAgregaEvento()
    {
        using var f = new Escenario(); await f.Preparar(cerrar: false);
        var captura = await f.Datos.IniciarCapturaAsync(f.Cuenta, f.Jornada.Id, f.Envio, "Foto");
        await new EvidenciasLocales(f.Datos, f.Archivos).RecuperarAsync(f.Cuenta, f.Jornada.Id);
        Assert.False(Assert.Single(await f.Datos.LeerCapturasAsync(f.Cuenta, f.Jornada.Id)).Lista);
        var antes = (await f.Datos.LeerAsync(f.Cuenta, f.Jornada.Id))!.Operaciones.Count;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Datos.RegistrarConCapturasAsync(f.Cuenta, f.Jornada.Id, [captura.Id],
            j => j.AdjuntarEvidencia(f.Envio, Elemento("{}"), DateTimeOffset.UtcNow)));
        Assert.Equal(antes, (await f.Datos.LeerAsync(f.Cuenta, f.Jornada.Id))!.Operaciones.Count);
        await f.Datos.DescartarCapturaAsync(f.Cuenta, captura.Id);
        Assert.Empty(await f.Datos.LeerCapturasAsync(f.Cuenta, f.Jornada.Id));
    }

    private static HttpResponseMessage Confirmar(SobreOperacion op, Guid envio) => Json(new ConfirmacionOperacion
    {
        Id = op.Id, Secuencia = op.Secuencia, Resultado = "Aplicada", VersionRuta = op.Secuencia,
        VersionEnvio = op.EnvioId is null ? null : op.Tipo == "EntregaFisica" ? 4 : 2,
        VersionesEnvio = op.Tipo switch { "ConfirmarCarga" => new() { [envio] = 3 }, "EntregaFisica" => new() { [envio] = 4 }, _ => [] }
    });
    private static HttpResponseMessage Json(ConfirmacionOperacion respuesta) => new(HttpStatusCode.OK)
        { Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(respuesta, ContratosRepartoJson.Default.ConfirmacionOperacion)) };
    private sealed class Escenario : IDisposable
    {
        internal readonly string Raiz = Path.Combine(Path.GetTempPath(), "nexoruta-sync-tests-" + Guid.NewGuid().ToString("N"));
        internal readonly CuentaMovil Cuenta = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        internal readonly Guid Envio = Guid.NewGuid(), Bulto = Guid.NewGuid(), FotoId = Guid.NewGuid();
        internal readonly byte[] Foto = [137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3, 4, 5, 6, 7, 8];
        internal readonly Claves Claves = new();
        internal RepositorioJornadasSqlite Datos { get; }
        internal RepositorioArchivosCifrados Archivos { get; }
        internal JornadaLocal Jornada { get; }
        public Escenario()
        {
            Datos = new(Raiz + "/sql", Claves); Archivos = new(Raiz + "/evidencias", Claves);
            Jornada = JornadaLocal.Iniciar(Guid.NewGuid(), Guid.NewGuid(), Cuenta, DateTimeOffset.UtcNow);
        }
        internal async Task Preparar(bool conFoto = false, bool cerrar = true)
        {
            Jornada.RegistrarCarga(Envio, Elemento($"{{\"bultoId\":\"{Bulto}\"}}"), DateTimeOffset.UtcNow);
            Jornada.ConfirmarCarga(DateTimeOffset.UtcNow);
            if (conFoto) await Archivos.GuardarAsync(Cuenta, FotoId, Foto);
            Jornada.RegistrarEntregaFisica(Envio, Elemento(conFoto ? $"{{\"fotoId\":\"{FotoId}\"}}" : "{}"), DateTimeOffset.UtcNow);
            if (cerrar) Jornada.Finalizar(DateTimeOffset.UtcNow);
            var hoja = new HojaRuta { Id = Jornada.RutaId, JornadaId = Jornada.Id, RepartidorAccesoId = Cuenta.AccesoId,
                VehiculoId = Guid.NewGuid(), Fecha = DateOnly.FromDateTime(DateTime.Today), Estado = "Despachada", Version = 1,
                Politica = new() { Version = 1, ZonaHoraria = "America/Montevideo" },
                Paradas = [new() { Reglas = new() { VersionId = Guid.NewGuid(), NumeroVersion = 1, MaximoIntentos = 3,
                IntervaloEntreIntentosMinutos = 1, PoliticaDevolucionAlAgotarIntentos = "DecisionOperativa",
                RequiereFoto = true, RequiereNombre = true, Motivos = [new("AUSENTE", "Ausente")] }, EnvioId = Envio, Orden = 1, Activa = true, EnvioVersion = 2, Direccion = "Dirección de prueba",
                    Bultos = [new() { Id = Bulto, Codigo = "TEST" }] }] };
            await Datos.GuardarHojaAsync(Cuenta, hoja.Id, SerializadorHojaRuta.Serializar(hoja));
            await Datos.CrearAsync(Jornada);
        }
        internal ClienteSesionMovil Cliente(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) =>
            new(new Uri("https://api.example.test/"), new Almacen(new() { Api = "https://api.example.test/", Cuenta = Cuenta,
                Tokens = new() { AccessToken = "jwt-de-prueba", TokenType = "Bearer", RefreshToken = new string('a', 43),
                    SesionId = Guid.NewGuid(), ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1), RefreshExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(30) } }), new Transporte(responder));
        public void Dispose() { Datos.Dispose(); Archivos.Dispose(); if (Directory.Exists(Raiz)) Directory.Delete(Raiz, true); }
    }
    private static JsonElement Elemento(string json) { using var documento = JsonDocument.Parse(json); return documento.RootElement.Clone(); }
    internal sealed class Claves : IAlmacenClavesProtegidas
    {
        private readonly Dictionary<string, string> valores = [];
        public Task<string?> ObtenerAsync(string nombre) => Task.FromResult(valores.GetValueOrDefault(nombre));
        public Task GuardarAsync(string nombre, string valor) { valores[nombre] = valor; return Task.CompletedTask; }
    }
    private sealed class Almacen(EstadoSesionMovil? estado) : IAlmacenSesionMovil
    {
        public Task EliminarAsync() { estado = null; return Task.CompletedTask; }
        public Task<EstadoSesionMovil?> LeerAsync() => Task.FromResult<EstadoSesionMovil?>(estado);
        public Task GuardarAsync(EstadoSesionMovil nuevo) { estado = nuevo; return Task.CompletedTask; }
    }
    private sealed class Transporte(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => responder(request, ct); }
}
