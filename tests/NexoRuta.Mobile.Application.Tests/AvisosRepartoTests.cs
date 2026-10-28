using System.Net;
using System.Text;
using System.Text.Json;
using NexoRuta.Mobile.Api.Identidad;
using NexoRuta.Mobile.Api.Notificaciones;
using NexoRuta.Mobile.Api.Reparto;
using NexoRuta.Mobile.Core.Jornadas;
using NexoRuta.Mobile.Core.Notificaciones;
using NexoRuta.Mobile.Core.Rutas;
using NexoRuta.Mobile.Storage;
namespace NexoRuta.Mobile.Application.Tests;

public sealed class AvisosRepartoTests
{
    [Fact]
    public async Task RecepcionSinToqueNoSeConfundeConAperturaYNoConsultaEstados()
    {
        using var f = new Escenario(); var tipos = new List<string>();
        using var cliente = f.Cliente(async (r, ct) =>
        {
            Assert.Equal(HttpMethod.Post, r.Method);
            tipos.Add(JsonSerializer.Deserialize(await r.Content!.ReadAsByteArrayAsync(ct), NotificacionesJson.Default.ConfirmarAviso)!.Tipo);
            return new(HttpStatusCode.OK);
        });
        using var servicio = new ServicioAvisosReparto(cliente, f.Datos);
        await servicio.GuardarReferenciaAsync(f.Payload, false);
        await servicio.RegistrarRecepcionesAsync(f.Cuenta);
        await servicio.RegistrarRecepcionesAsync(f.Cuenta);
        Assert.Equal(new[] { "Recibido" }, tipos);
        Assert.Null(await servicio.ObtenerAperturaPendienteAsync(f.Cuenta));
        await servicio.GuardarReferenciaAsync(f.Payload, true);
        Assert.Equal(f.Aviso.Id, await servicio.ObtenerAperturaPendienteAsync(f.Cuenta));
    }
    [Fact]
    public async Task PushDuplicadoPersisteCifradoSinRedYReabreSoloEnSuCuenta()
    {
        using var f = new Escenario();
        using var cliente = f.Cliente((_, _) => throw new InvalidOperationException("Recibir push no debe llamar a la API"));
        using (var servicio = new ServicioAvisosReparto(cliente, f.Datos))
        {
            Assert.True(await servicio.GuardarReferenciaAsync(f.Payload, false));
            Assert.Null(await servicio.ObtenerAperturaPendienteAsync(f.Cuenta));
            Assert.True(await servicio.GuardarReferenciaAsync(f.Payload, true));
            Assert.True(await servicio.GuardarReferenciaAsync(f.Payload, false));
            Assert.Equal(f.Aviso.Id, await servicio.ObtenerAperturaPendienteAsync(f.Cuenta));
            var ajeno = new Dictionary<string, string>(f.Payload) { ["accesoId"] = Guid.NewGuid().ToString() };
            Assert.False(await servicio.GuardarReferenciaAsync(ajeno, true));
        }
        using var db = new RepositorioJornadasSqlite(f.Raiz, f.Claves);
        using var reinicio = new ServicioAvisosReparto(cliente, db);
        Assert.Equal(f.Aviso.Id, await reinicio.ObtenerAperturaPendienteAsync(f.Cuenta));
        var otra = new CuentaMovil(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        Assert.Null(await reinicio.ObtenerAperturaPendienteAsync(otra));
        Assert.DoesNotContain(f.Aviso.Id.ToString(), Encoding.UTF8.GetString(await File.ReadAllBytesAsync(Directory.GetFiles(f.Raiz, "*.sqlite", SearchOption.AllDirectories)[0])));
    }

    [Theory]
    [InlineData("v", "2")]
    [InlineData("notificacionId", "no-es-un-guid")]
    [InlineData("operadorId", "00000000-0000-0000-0000-000000000000")]
    [InlineData("estado", "Entregado")]
    public void PayloadInvalidoONegocioEnPushNoSeAcepta(string clave, string valor)
    {
        using var f = new Escenario();
        var payload = new Dictionary<string, string>(f.Payload) { [clave] = valor };
        Assert.False(AvisoPush.IntentarLeer(payload, out _));
    }

    [Fact]
    public async Task ReintentoDeRegistroConservaIdentidadDeInstalacionTrasFallo()
    {
        using var f = new Escenario(); var enviados = new List<RegistroInstalacion>();
        using var cliente = f.Cliente(async (r, ct) =>
        {
            Assert.Equal(HttpMethod.Put, r.Method);
            enviados.Add(JsonSerializer.Deserialize(await r.Content!.ReadAsByteArrayAsync(ct), NotificacionesJson.Default.RegistroInstalacion)!);
            return new(enviados.Count == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.NoContent);
        });
        using (var primero = new ServicioAvisosReparto(cliente, f.Datos))
            await Assert.ThrowsAsync<SesionMovilException>(() => primero.RegistrarInstalacionAsync(f.Cuenta, "Android", new string('a', 32), true));
        using var db = new RepositorioJornadasSqlite(f.Raiz, f.Claves);
        using var reinicio = new ServicioAvisosReparto(cliente, db);
        await reinicio.RegistrarInstalacionAsync(f.Cuenta, "Android", new string('b', 32), false);
        Assert.Equal(enviados[0].InstalacionId, enviados[1].InstalacionId);
        Assert.NotEqual(enviados[0].FirebaseInstallationId, enviados[1].FirebaseInstallationId);
        Assert.False(enviados[1].Permitida);
    }

    [Fact]
    public async Task RespuestaPerdidaDeAperturaConservaAvisoYBaselineYReintentaAck()
    {
        using var f = new Escenario();
        var original = SerializadorHojaRuta.Serializar(f.Hoja("EnTransito", true, 2));
        await f.Datos.GuardarHojaAsync(f.Cuenta, f.Aviso.RutaId, original);
        var journal = JornadaLocal.Iniciar(f.Jornada, f.Aviso.RutaId, f.Cuenta, DateTimeOffset.UtcNow);
        await f.Datos.CrearAsync(journal);
        // Las vistas del servidor se mantienen separadas de la hoja original y del diario local.
        var recibidos = new List<string>(); var perder = true;
        using var cliente = f.Cliente(async (r, ct) =>
        {
            if (r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath.Contains("/rutas/"))
                return Bytes(SerializadorHojaRuta.Serializar(f.Hoja("Asignado", false, 8)));
            if (r.Method == HttpMethod.Get) return Bytes(JsonSerializer.SerializeToUtf8Bytes(f.Aviso, NotificacionesJson.Default.AvisoReparto));
            var ack = JsonSerializer.Deserialize(await r.Content!.ReadAsByteArrayAsync(ct), NotificacionesJson.Default.ConfirmarAviso)!;
            recibidos.Add(ack.Tipo);
            if (ack.Tipo == "Abierto" && perder) { perder = false; throw new HttpRequestException("Respuesta perdida"); }
            return new(HttpStatusCode.NoContent);
        });
        using var servicio = new ServicioAvisosReparto(cliente, f.Datos);
        await servicio.GuardarReferenciaAsync(f.Payload, true);
        await Assert.ThrowsAsync<HttpRequestException>(() => servicio.AbrirAsync(f.Cuenta, f.Aviso.Id));
        Assert.Equal(f.Aviso.Id, await servicio.ObtenerAperturaPendienteAsync(f.Cuenta));
        var resultado = await servicio.AbrirAsync(f.Cuenta, f.Aviso.Id);
        Assert.True(resultado.Aviso.Vigente);
        Assert.False(Assert.Single(resultado.Hoja!.Paradas).Activa);
        Assert.Equal(new[] { "Recibido", "Abierto", "Recibido", "Abierto" }, recibidos);
        Assert.Null(await servicio.ObtenerAperturaPendienteAsync(f.Cuenta));
        Assert.Equal(original, Assert.Single(await f.Datos.LeerHojasAsync(f.Cuenta)));
        Assert.Equal(journal.Operaciones.Single().Id, (await f.Datos.LeerAsync(f.Cuenta, journal.Id))!.Operaciones.Single().Id);
        Assert.Equal(8, SerializadorHojaRuta.Deserializar((await f.Datos.LeerVistaRemotaAsync(f.Cuenta, f.Aviso.RutaId))!, f.Cuenta).Version);
        await f.Datos.GuardarVistaRemotaAsync(f.Cuenta, f.Aviso.RutaId, 2, original);
        Assert.Equal(8, SerializadorHojaRuta.Deserializar((await f.Datos.LeerVistaRemotaAsync(f.Cuenta, f.Aviso.RutaId))!, f.Cuenta).Version);
    }

    [Fact]
    public async Task AvisoObsoletoNoCargaLaRutaNiOrdenaUnaDevolucion()
    {
        using var f = new Escenario(); var lecturas = 0;
        using var cliente = f.Cliente((r, _) =>
        {
            if (r.Method != HttpMethod.Get) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            lecturas++; Assert.DoesNotContain("/rutas/", r.RequestUri!.AbsolutePath);
            return Task.FromResult(Bytes(JsonSerializer.SerializeToUtf8Bytes(f.Aviso with { Vigente = false }, NotificacionesJson.Default.AvisoReparto)));
        });
        using var servicio = new ServicioAvisosReparto(cliente, f.Datos);
        var resultado = await servicio.AbrirAsync(f.Cuenta, f.Aviso.Id);
        Assert.False(resultado.Aviso.Vigente); Assert.Null(resultado.Hoja); Assert.Equal(1, lecturas);
    }

    [Fact]
    public async Task CuentaDistintaNoEnviaSolicitudNiConfirmaAviso()
    {
        using var f = new Escenario();
        using var cliente = f.Cliente((_, _) => throw new InvalidOperationException("No debe enviar credenciales de otra cuenta"));
        var otra = new CuentaMovil(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await Assert.ThrowsAsync<SesionMovilException>(() => cliente.ObtenerAvisoAsync(otra, f.Aviso.Id));
        await Assert.ThrowsAsync<SesionMovilException>(() => cliente.ConfirmarAvisoAsync(otra, f.Aviso.Id, "Confirmado"));
        await Assert.ThrowsAsync<SesionMovilException>(() => cliente.EnviarOperacionAsync(otra, f.Jornada, Encoding.UTF8.GetBytes("{}")));
        await Assert.ThrowsAsync<SesionMovilException>(() => cliente.SubirEvidenciaAsync(otra, f.Jornada, f.Aviso.EnvioId, Guid.NewGuid(), "Foto", [1]));
    }

    [Fact]
    public void VistaRemotaDetieneEntregaSinRevertirUnaConfirmacionMasNueva()
    {
        using var f = new Escenario(); var original = f.Hoja("EnTransito", true, 2).Paradas[0];
        var remoto = f.Hoja("Asignado", false, 8);
        var vista = VistaParada.Obtener(original, remoto, []);
        Assert.False(vista.Activa); Assert.Equal("Asignado", vista.Estado);
        var ack = new ConfirmacionOperacion { Resultado = "Aplicada", Secuencia = 4,
            VersionesEnvio = new() { [original.EnvioId] = 9 }, EstadosEnvio = new() { [original.EnvioId] = "Entregado" } };
        vista = VistaParada.Obtener(original, remoto, [ack]);
        Assert.Equal("Entregado", vista.Estado); Assert.Equal(9, vista.Version);
        Assert.Equal("EnTransito", original.Estado); Assert.Equal(2, original.EnvioVersion);
    }

    private static HttpResponseMessage Bytes(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    private sealed class Escenario : IDisposable
    {
        internal readonly string Raiz = Path.Combine(Path.GetTempPath(), "nexoruta-avisos-" + Guid.NewGuid().ToString("N"));
        internal readonly CuentaMovil Cuenta = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        internal readonly SincronizadorJornadaTests.Claves Claves = new();
        internal readonly Guid Jornada = Guid.NewGuid();
        internal readonly AvisoReparto Aviso = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 8, "Reasignacion", "Se entrega mañana", DateTimeOffset.UtcNow, true, null);
        internal RepositorioJornadasSqlite Datos { get; }
        internal Dictionary<string, string> Payload => new() { ["v"] = "1", ["notificacionId"] = Aviso.Id.ToString(), ["operadorId"] = Cuenta.OperadorId.ToString(), ["accesoId"] = Cuenta.AccesoId.ToString() };
        internal HojaRuta Hoja(string estado, bool activa, long version) => new()
        {
            Id = Aviso.RutaId, JornadaId = Jornada, RepartidorAccesoId = Cuenta.AccesoId, VehiculoId = Guid.NewGuid(),
            Fecha = new(2026, 10, 19), Estado = "EnCurso", Version = version,
            Politica = new() { Version = 1, ZonaHoraria = "America/Montevideo" },
            Paradas = [new() { Reglas = new() { VersionId = Guid.NewGuid(), NumeroVersion = 1, MaximoIntentos = 3,
                IntervaloEntreIntentosMinutos = 1, PoliticaDevolucionAlAgotarIntentos = "DecisionOperativa",
                RequiereFoto = true, RequiereNombre = true, Motivos = [new("AUSENTE", "Ausente")] }, EnvioId = Aviso.EnvioId, Orden = 1, EnvioVersion = version, Estado = estado, Activa = activa,
                Direccion = "Dirección de prueba", Bultos = [new() { Id = Guid.NewGuid(), Codigo = "PRUEBA" }] }]
        };
        internal Escenario() => Datos = new(Raiz, Claves);
        internal ClienteSesionMovil Cliente(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) =>
            new(new Uri("https://api.example.test/"), new Almacen(new() { Api = "https://api.example.test/", Cuenta = Cuenta,
                Tokens = new() { AccessToken = "jwt-de-prueba", TokenType = "Bearer", RefreshToken = new string('a', 43),
                    SesionId = Guid.NewGuid(), ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1), RefreshExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(30) } }), new Transporte(responder));
        public void Dispose() { Datos.Dispose(); if (Directory.Exists(Raiz)) Directory.Delete(Raiz, true); }
    }
    private sealed class Almacen(EstadoSesionMovil? estado) : IAlmacenSesionMovil
    {
        public Task EliminarAsync(string api) { estado = null; return Task.CompletedTask; }
        public Task<EstadoSesionMovil?> LeerAsync(string api) => Task.FromResult<EstadoSesionMovil?>(estado);
        public Task GuardarAsync(EstadoSesionMovil nuevo) { estado = nuevo; return Task.CompletedTask; }
    }
    private sealed class Transporte(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => responder(request, ct); }
}
