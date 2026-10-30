using System.Net;
using System.Text;
using System.Text.Json;
using NexoRuta.Mobile.Api.Identidad;
using NexoRuta.Mobile.Core.Jornadas;
using NexoRuta.Mobile.Core.Rutas;

namespace NexoRuta.Mobile.Api.Tests;

public sealed partial class ClienteSesionMovilTests
{
    private static readonly Uri Api = new("https://api.nexoruta.test/");
    private static readonly DateTimeOffset Ahora = new(2026, 10, 17, 15, 0, 0, TimeSpan.Zero);
    private static readonly CuentaMovil Cuenta = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
    private static readonly Guid Sesion = Guid.NewGuid();
    private static readonly string Refresh = new('a', 43);
    private static readonly string SiguienteRefresh = new('b', 43);

    [Theory]
    [InlineData("http://api.nexoruta.test/")]
    [InlineData("https://usuario:clave@api.nexoruta.test/")]
    [InlineData("https://api.nexoruta.test/base/")]
    [InlineData("https://api.nexoruta.test/?token=x")]
    [InlineData("https://api.nexoruta.test/#fragmento")]
    [InlineData("/relativa")]
    public void RechazaOrigenInseguroOAmbiguo(string origen) => Assert.Throws<ArgumentException>(() =>
        new ClienteSesionMovil(new Uri(origen, UriKind.RelativeOrAbsolute), new Almacen()));

    [Fact]
    public async Task IngresoObtieneIdentidadDelServidorYPersisteElParCompletoSinPassword()
    {
        var almacen = new Almacen();
        var rutas = new List<string>();
        using var cliente = Crear(almacen, async (request, ct) =>
        {
            rutas.Add(request.RequestUri!.AbsolutePath);
            Assert.Equal("https", request.RequestUri.Scheme);
            if (request.RequestUri.AbsolutePath.EndsWith("login"))
            {
                Assert.Null(request.Headers.Authorization);
                var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                Assert.Equal("repartidor@example.test", body.RootElement.GetProperty("email").GetString());
                Assert.Equal(" contraseña sin alterar ", body.RootElement.GetProperty("password").GetString());
                return Json(TokensJson());
            }
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("jwt-inicial", request.Headers.Authorization.Parameter);
            return Json(ContextoJson(Cuenta));
        });

        Assert.Equal(Cuenta, await cliente.IngresarAsync(" repartidor@example.test ", " contraseña sin alterar "));
        Assert.Equal(new[] { "/api/auth/mobile/login", "/api/usuarios/actual" }, rutas);
        var guardada = Assert.IsType<EstadoSesionMovil>(almacen.Estado);
        Assert.Equal(Cuenta, guardada.Cuenta);
        Assert.Equal(Refresh, guardada.Tokens!.RefreshToken);
        var json = SerializadorSesionMovil.Serializar(guardada);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("contraseña sin alterar", json);
        Assert.DoesNotContain("jwt-inicial", guardada.Tokens.ToString());
    }

    [Fact]
    public async Task IngresoRegistraResultadoSeguroSinExponerCredencialesNiTokens()
    {
        var diagnosticos = new List<string>();
        using var cliente = new ClienteSesionMovil(Api, new Almacen(), new Transporte((request, _) =>
            Task.FromResult(Json(request.RequestUri!.AbsolutePath.EndsWith("login")
                ? TokensJson()
                : ContextoJson(Cuenta)))), new Reloj(), diagnosticos.Add);

        await cliente.IngresarAsync("repartidor@example.test", "password-de-prueba");

        Assert.Contains("auth.login.response.status=200", diagnosticos);
        Assert.Contains("session.login.identity_matches=true", diagnosticos);
        Assert.Contains("session.login.persisted=true", diagnosticos);
        var contenido = string.Join('|', diagnosticos);
        Assert.DoesNotContain("password-de-prueba", contenido);
        Assert.DoesNotContain("jwt-inicial", contenido);
        Assert.DoesNotContain(Refresh, contenido);
    }

    [Fact]
    public async Task ReingresoDeOtraCuentaRevocaSoloSesionNuevaYConservaLaAnterior()
    {
        var almacen = ConSesion();
        var anterior = almacen.Estado;
        var otra = new CuentaMovil(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var revocaciones = 0;
        var diagnosticos = new List<string>();
        using var cliente = new ClienteSesionMovil(Api, almacen, new Transporte((request, _) =>
        {
            var ruta = request.RequestUri!.AbsolutePath;
            if (ruta.EndsWith("login")) return Task.FromResult(Json(TokensJson()));
            if (ruta.EndsWith("logout"))
            {
                revocaciones++;
                Assert.Null(request.Headers.Authorization);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }
            return Task.FromResult(Json(ContextoJson(otra)));
        }), new Reloj(), diagnosticos.Add);

        await ErrorAsync(ErrorSesionMovil.CuentaDiferente, () => cliente.IngresarAsync("otra@example.test", "clave"));
        Assert.Same(anterior, almacen.Estado);
        Assert.Equal(1, revocaciones);
        Assert.Contains("auth.login.response.status=200", diagnosticos);
        Assert.Contains("session.login.identity_matches=false", diagnosticos);
        Assert.Contains("session.login.followup_failure=SesionMovilException", diagnosticos);
    }

    [Fact]
    public async Task ReingresoConElMismoUsuarioYOperadorPeroOtroAccesoNoVinculaLaCuenta()
    {
        var almacen = ConSesion();
        var otra = new CuentaMovil(Cuenta.UsuarioId, Guid.NewGuid(), Cuenta.OperadorId);
        var diagnosticos = new List<string>();
        using var cliente = new ClienteSesionMovil(Api, almacen, new Transporte((request, _) =>
            Task.FromResult(Json(request.RequestUri!.AbsolutePath.EndsWith("login")
                ? TokensJson()
                : ContextoJson(otra)))), new Reloj(), diagnosticos.Add);

        await ErrorAsync(ErrorSesionMovil.CuentaDiferente, () => cliente.IngresarAsync("mismo@example.test", "clave"));

        Assert.Contains("session.login.user_matches=true", diagnosticos);
        Assert.Contains("session.login.access_matches=false", diagnosticos);
        Assert.Contains("session.login.operator_matches=true", diagnosticos);
    }

    [Fact]
    public async Task ReautenticacionExplicitaConIdentidadRenovadaDelMismoOperadorGuardaSesionSinMigrarDatos()
    {
        var almacen = ConSesion();
        var autenticada = new CuentaMovil(Guid.NewGuid(), Guid.NewGuid(), Cuenta.OperadorId);
        using var cliente = Crear(almacen, (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return Task.FromResult(Json(path.EndsWith("login") ? TokensJson(refresh: SiguienteRefresh)
                : path.EndsWith("perfil") ? PerfilJson(autenticada) : ContextoJson(autenticada)));
        });

        var resultado = await cliente.IngresarAsync("mismo@example.test", "clave", permitirCambioIdentidadEnMismoOperador: true);

        Assert.Equal(autenticada, resultado);
        Assert.Equal(autenticada, almacen.Estado!.Cuenta);
        Assert.Equal(SiguienteRefresh, almacen.Estado.Tokens!.RefreshToken);
    }

    [Fact]
    public async Task ReautenticacionNoPermiteCambiarDeOperadorAunqueElLlamadorLoAutorice()
    {
        var almacen = ConSesion();
        var anterior = almacen.Estado;
        var otroOperador = new CuentaMovil(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var revocaciones = 0;
        using var cliente = Crear(almacen, (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("login")) return Task.FromResult(Json(TokensJson()));
            if (path.EndsWith("logout"))
            {
                revocaciones++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }
            return Task.FromResult(Json(ContextoJson(otroOperador)));
        });

        await ErrorAsync(ErrorSesionMovil.CuentaDiferente, () => cliente.IngresarAsync(
            "otra@example.test", "clave", permitirCambioIdentidadEnMismoOperador: true));

        Assert.Same(anterior, almacen.Estado);
        Assert.Equal(1, revocaciones);
    }

    [Fact]
    public async Task ReingresoDeMismaCuentaReemplazaTokensYConservaIdentidad()
    {
        var almacen = ConSesion();
        almacen.Estado = new() { Api = Api.AbsoluteUri, Cuenta = Cuenta };
        using var cliente = Crear(almacen, (request, _) => Task.FromResult(Json(
            request.RequestUri!.AbsolutePath.EndsWith("login") ? TokensJson(refresh: SiguienteRefresh) : ContextoJson(Cuenta))));

        Assert.Equal(Cuenta, await cliente.IngresarAsync("mismo@example.test", "clave"));
        Assert.Equal(SiguienteRefresh, almacen.Estado!.Tokens!.RefreshToken);
    }

    [Fact]
    public async Task ContextoDeComercioEsRechazadoYRevocaLaSesionRecienCreada()
    {
        var almacen = new Almacen();
        var revocaciones = 0;
        using var cliente = Crear(almacen, (request, _) =>
        {
            var ruta = request.RequestUri!.AbsolutePath;
            if (ruta.EndsWith("login")) return Task.FromResult(Json(TokensJson()));
            if (ruta.EndsWith("logout"))
            {
                revocaciones++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }
            return Task.FromResult(Json(ContextoJson(Cuenta).Replace("\"tipo\":\"Operador\"", "\"tipo\":\"Comercio\"")));
        });
        await ErrorAsync(ErrorSesionMovil.RespuestaInvalida, () => cliente.IngresarAsync("u@example.test", "clave"));
        Assert.Null(almacen.Estado);
        Assert.Equal(1, revocaciones);
    }

    [Fact]
    public async Task ErrorAlGuardarIngresoRevocaSesionNuevaSinOcultarFalloDeAlmacenamiento()
    {
        var almacen = ConSesion();
        var anterior = almacen.Estado;
        almacen.FallarEnEscritura = 1;
        var revocaciones = 0;
        using var cliente = Crear(almacen, (request, _) =>
        {
            var ruta = request.RequestUri!.AbsolutePath;
            if (ruta.EndsWith("login")) return Task.FromResult(Json(TokensJson()));
            if (ruta.EndsWith("logout"))
            {
                revocaciones++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }
            return Task.FromResult(Json(ContextoJson(Cuenta)));
        });
        await Assert.ThrowsAsync<IOException>(() => cliente.IngresarAsync("u@example.test", "clave"));
        Assert.Same(anterior, almacen.Estado);
        Assert.Equal(1, revocaciones);
    }

    [Theory]
    [InlineData(401, ErrorSesionMovil.CredencialesRechazadas)]
    [InlineData(403, ErrorSesionMovil.AccesoDenegado)]
    [InlineData(429, ErrorSesionMovil.DemasiadasSolicitudes)]
    [InlineData(503, ErrorSesionMovil.ServicioNoDisponible)]
    [InlineData(302, ErrorSesionMovil.RespuestaInvalida)]
    public async Task ErroresDeIngresoNoCambianSesionNiExponenTextoDelServidor(int status, ErrorSesionMovil error)
    {
        var almacen = ConSesion();
        var anterior = almacen.Estado;
        using var cliente = Crear(almacen, (_, _) => Task.FromResult(Json("secretos-del-servidor", (HttpStatusCode)status)));
        var ex = await ErrorAsync(error, () => cliente.IngresarAsync("email@example.test", "clave"));
        Assert.DoesNotContain("secretos-del-servidor", ex.ToString());
        Assert.Same(anterior, almacen.Estado);
    }

    [Fact]
    public async Task RenovacionConcurrenteConsumeUnSoloRefreshYLosPermisosUsanSoloJwtNuevo()
    {
        var almacen = ConSesion(expirado: true);
        var renovaciones = 0;
        var lecturas = 0;
        using var cliente = Crear(almacen, async (request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("refresh"))
            {
                renovaciones++;
                Assert.Null(almacen.Estado!.Tokens);
                Assert.Equal(Cuenta, almacen.Estado.Cuenta);
                Assert.Null(request.Headers.Authorization);
                var body = await request.Content!.ReadAsStringAsync(ct);
                Assert.Contains(Refresh, body);
                await Task.Yield();
                return Json(TokensJson("jwt-nuevo", SiguienteRefresh));
            }
            lecturas++;
            Assert.Equal("jwt-nuevo", request.Headers.Authorization!.Parameter);
            return Json("[\"envios.consultar\"]");
        });

        var resultados = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => cliente.ObtenerPermisosAsync()));
        Assert.All(resultados, x => Assert.Equal("envios.consultar", Assert.Single(x)));
        Assert.Equal(1, renovaciones);
        Assert.Equal(12, lecturas);
        Assert.Equal(SiguienteRefresh, almacen.Estado!.Tokens!.RefreshToken);
        Assert.Equal(Ahora.AddDays(30), almacen.Estado.Tokens.RefreshExpiresAtUtc);
    }

    [Fact]
    public async Task Rechazo401RenuevaUnaVezYRepiteSoloLaConsultaGet()
    {
        var almacen = ConSesion();
        var rutas = new List<string>();
        using var cliente = Crear(almacen, (request, _) =>
        {
            rutas.Add(request.RequestUri!.AbsolutePath);
            if (rutas.Count == 1) return Task.FromResult(Json("", HttpStatusCode.Unauthorized));
            if (rutas.Count == 2) return Task.FromResult(Json(TokensJson("jwt-nuevo", SiguienteRefresh)));
            return Task.FromResult(Json("[]"));
        });

        Assert.Empty(await cliente.ObtenerPermisosAsync());
        Assert.Equal(new[] { "/api/usuarios/actual/permisos", "/api/auth/mobile/refresh", "/api/usuarios/actual/permisos" }, rutas);
    }

    [Fact]
    public async Task ConsultaHojaUsaFechaCalendarioSeleccionadaYNoInterpretaCeroResultadosComoFallo()
    {
        var almacen = ConSesion();
        var eventos = new List<string>();
        using var cliente = new ClienteSesionMovil(Api, almacen, new Transporte((request, _) =>
        {
            Assert.Equal("/api/mobile/rutas", request.RequestUri!.AbsolutePath);
            Assert.Equal("?fecha=2026-10-31", request.RequestUri.Query);
            Assert.Equal("jwt-inicial", request.Headers.Authorization!.Parameter);
            return Task.FromResult(Json("[]"));
        }), new Reloj(), eventos.Add);

        var hojas = await cliente.ObtenerHojasAsync(new DateOnly(2026, 10, 31));

        Assert.Empty(hojas);
        Assert.Equal(Cuenta, await cliente.ObtenerCuentaLocalAsync());
        Assert.Contains("api.mobile_routes.request.date=2026-10-31", eventos);
    }

    [Fact]
    public async Task ConsultaDeHojaExponeLaFechaHttpDelServidorParaLaElegibilidadDeInicio()
    {
        var almacen = ConSesion();
        var fechaServidor = new DateTimeOffset(2026, 10, 31, 3, 30, 0, TimeSpan.Zero);
        using var cliente = new ClienteSesionMovil(Api, almacen, new Transporte((_, _) =>
        {
            var respuesta = Json("[]");
            respuesta.Headers.Date = fechaServidor;
            return Task.FromResult(respuesta);
        }), new Reloj());

        var (hojas, recibida) = await cliente.ObtenerHojasConFechaServidorAsync(new DateOnly(2026, 10, 31));

        Assert.Empty(hojas);
        Assert.Equal(fechaServidor, recibida);
    }

    [Fact]
    public async Task ConsultaHojaRegistraRespuestaHttpSinCredencialesNiContenido()
    {
        var almacen = ConSesion();
        var eventos = new List<string>();
        using var cliente = new ClienteSesionMovil(Api, almacen, new Transporte((request, _) =>
            Task.FromResult(Json("respuesta no registrada", HttpStatusCode.Unauthorized))), new Reloj(), eventos.Add);

        await ErrorAsync(ErrorSesionMovil.ReingresoNecesario,
            () => cliente.ObtenerHojasAsync(new DateOnly(2026, 10, 31)));

        Assert.Contains("api.mobile_routes.response.status=401", eventos);
        Assert.Contains("session.refresh.response.status=401", eventos);
        var log = string.Join("\n", eventos);
        Assert.DoesNotContain("jwt-inicial", log);
        Assert.DoesNotContain(Refresh, log);
        Assert.DoesNotContain("respuesta no registrada", log);
        Assert.DoesNotContain("api.nexoruta.test", log);
    }

    [Fact]
    public async Task Segundo401ExigeReingresoSinBucleNiBorradoDeCuenta()
    {
        var almacen = ConSesion();
        var llamadas = 0;
        using var cliente = Crear(almacen, (request, _) =>
        {
            llamadas++;
            return Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("refresh")
                ? Json(TokensJson("jwt-nuevo", SiguienteRefresh)) : Json("", HttpStatusCode.Unauthorized));
        });
        await ErrorAsync(ErrorSesionMovil.ReingresoNecesario, () => cliente.ObtenerPermisosAsync());
        Assert.Equal(3, llamadas);
        Assert.Null(almacen.Estado!.Tokens);
        Assert.Equal(Cuenta, await cliente.LeerCuentaGuardadaAsync());
    }

    [Fact]
    public async Task Prohibido403NoRenuevaNiRepiteNiInvalidaToken()
    {
        var almacen = ConSesion();
        var anterior = almacen.Estado;
        var llamadas = 0;
        using var cliente = Crear(almacen, (_, _) =>
        {
            llamadas++;
            return Task.FromResult(Json("", HttpStatusCode.Forbidden));
        });
        await ErrorAsync(ErrorSesionMovil.AccesoDenegado, () => cliente.ObtenerPermisosAsync());
        Assert.Equal(1, llamadas);
        Assert.Same(anterior, almacen.Estado);
    }

    [Fact]
    public async Task RespuestaPerdidaDeRefreshNoSeReutilizaNiTrasRecrearCliente()
    {
        var almacen = ConSesion(expirado: true);
        using (var cliente = Crear(almacen, (_, _) => throw new HttpRequestException("Respuesta perdida")))
            await ErrorAsync(ErrorSesionMovil.ReingresoNecesario, () => cliente.ObtenerPermisosAsync());
        Assert.Null(almacen.Estado!.Tokens);
        Assert.Equal(Cuenta, almacen.Estado.Cuenta);
        using var reabierto = Crear(almacen, (_, _) => throw new Xunit.Sdk.XunitException("No debe reenviar el refresh"));
        await ErrorAsync(ErrorSesionMovil.ReingresoNecesario, () => reabierto.ObtenerPermisosAsync());
        Assert.Equal(Cuenta, await reabierto.LeerCuentaGuardadaAsync());
    }

    [Fact]
    public async Task CancelacionDuranteRefreshConservaCuentaYNoPermiteReusarToken()
    {
        var almacen = ConSesion(expirado: true);
        using var cancelar = new CancellationTokenSource();
        using var cliente = Crear(almacen, (_, ct) =>
        {
            cancelar.Cancel();
            ct.ThrowIfCancellationRequested();
            throw new Xunit.Sdk.XunitException("Debe cancelar");
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cliente.ObtenerPermisosAsync(cancelar.Token));
        Assert.Null(almacen.Estado!.Tokens);
        Assert.Equal(Cuenta, almacen.Estado.Cuenta);
    }

    [Fact]
    public async Task AlVencerPlazoAbsolutoNoIntentaRenovacionAunqueJwtParezcaVigente()
    {
        var almacen = ConSesion();
        var estado = almacen.Estado!;
        almacen.Estado = new() { Api = estado.Api, Cuenta = estado.Cuenta, Tokens = Tokens(Ahora.AddSeconds(-1), Ahora.AddSeconds(-1)) };
        using var cliente = Crear(almacen, (_, _) => throw new Xunit.Sdk.XunitException("No debe enviar"));
        await ErrorAsync(ErrorSesionMovil.ReingresoNecesario, () => cliente.ObtenerPermisosAsync());
        Assert.Null(almacen.Estado.Tokens);
        Assert.Equal(Cuenta, almacen.Estado.Cuenta);
    }

    [Theory]
    [InlineData(401, ErrorSesionMovil.ReingresoNecesario)]
    [InlineData(429, ErrorSesionMovil.DemasiadasSolicitudes)]
    [InlineData(503, ErrorSesionMovil.ServicioNoDisponible)]
    public async Task ErrorDeRefreshNoRepiteElSecretoNiDescartaIdentidad(int status, ErrorSesionMovil error)
    {
        var almacen = ConSesion(expirado: true);
        var llamadas = 0;
        using var cliente = Crear(almacen, (_, _) =>
        {
            llamadas++;
            return Task.FromResult(Json("no registrar", (HttpStatusCode)status));
        });
        await ErrorAsync(error, () => cliente.ObtenerPermisosAsync());
        await ErrorAsync(ErrorSesionMovil.ReingresoNecesario, () => cliente.ObtenerPermisosAsync());
        Assert.Equal(1, llamadas);
        Assert.Null(almacen.Estado!.Tokens);
        Assert.Equal(Cuenta, almacen.Estado.Cuenta);
    }

    [Fact]
    public async Task ErrorDeRedEnGetNoDescartaSesionNiReintenta()
    {
        var almacen = ConSesion();
        var anterior = almacen.Estado;
        using var cliente = Crear(almacen, (_, _) => throw new HttpRequestException("Sin conexión"));
        await Assert.ThrowsAsync<HttpRequestException>(() => cliente.ObtenerPermisosAsync());
        Assert.Same(anterior, almacen.Estado);
    }

    [Fact]
    public async Task FalloAlPersistirMarcadorImpideConsumirRefresh()
    {
        var almacen = ConSesion(expirado: true);
        var anterior = almacen.Estado;
        almacen.FallarEnEscritura = 1;
        using var cliente = Crear(almacen, (_, _) => throw new Xunit.Sdk.XunitException("No debe enviar"));
        await Assert.ThrowsAsync<IOException>(() => cliente.ObtenerPermisosAsync());
        Assert.Same(anterior, almacen.Estado);
    }

    [Fact]
    public async Task FalloAlPersistirParRenovadoNoReutilizaTokenConsumido()
    {
        var almacen = ConSesion(expirado: true);
        almacen.FallarEnEscritura = 2;
        using var cliente = Crear(almacen, (_, _) => Task.FromResult(Json(TokensJson("jwt-nuevo", SiguienteRefresh))));
        await Assert.ThrowsAsync<IOException>(() => cliente.ObtenerPermisosAsync());
        Assert.Null(almacen.Estado!.Tokens);
        await ErrorAsync(ErrorSesionMovil.ReingresoNecesario, () => cliente.ObtenerPermisosAsync());
    }

    [Theory]
    [InlineData("{\"accessToken\":\"secreto\"}")]
    [InlineData("null")]
    [InlineData("<html>secreto</html>")]
    public async Task RespuestaDeLoginInvalidaNoPersisteNiExponeCredenciales(string body)
    {
        var almacen = new Almacen();
        using var cliente = Crear(almacen, (_, _) => Task.FromResult(Json(body)));
        var ex = await ErrorAsync(ErrorSesionMovil.RespuestaInvalida, () => cliente.IngresarAsync("u@example.test", "clave"));
        Assert.Null(almacen.Estado);
        Assert.DoesNotContain("secreto", ex.ToString());
    }

    [Theory]
    [InlineData("sesion")]
    [InlineData("plazo")]
    [InlineData("refresh")]
    public async Task RenovacionNoPuedeCambiarFamiliaExtenderPlazoNiReutilizarSecreto(string cambio)
    {
        var almacen = ConSesion(expirado: true);
        var json = TokensJson("jwt-nuevo", cambio == "refresh" ? Refresh : SiguienteRefresh,
            cambio == "sesion" ? Guid.NewGuid() : Sesion, cambio == "plazo" ? Ahora.AddDays(31) : null);
        using var cliente = Crear(almacen, (_, _) => Task.FromResult(Json(json)));
        await ErrorAsync(ErrorSesionMovil.RespuestaInvalida, () => cliente.ObtenerPermisosAsync());
        Assert.Null(almacen.Estado!.Tokens);
    }

    [Fact]
    public async Task OtraApiNoPuedeRecibirCredencialesDelAlmacenExistente()
    {
        var almacen = ConSesion();
        using var cliente = new ClienteSesionMovil(new Uri("https://otro.example.test/"), almacen,
            new Transporte((_, _) => throw new Xunit.Sdk.XunitException("No debe enviar")), new Reloj());
        await ErrorAsync(ErrorSesionMovil.ReingresoNecesario, () => cliente.ObtenerPermisosAsync());
        Assert.NotNull(almacen.Estado!.Tokens);
    }

    [Fact]
    public void SerializacionSinReflexionConservaCuentaYEstadoDeReingreso()
    {
        foreach (var original in new[] { ConSesion().Estado!, new EstadoSesionMovil { Api = Api.AbsoluteUri, Cuenta = Cuenta } })
        {
            var restaurado = SerializadorSesionMovil.Deserializar(SerializadorSesionMovil.Serializar(original));
            Assert.Equal(Cuenta, restaurado.Cuenta);
            Assert.Equal(original.Tokens?.RefreshToken, restaurado.Tokens?.RefreshToken);
        }
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"version\":2}")]
    [InlineData("{\"version\":1,\"api\":\"https://api.test/\",\"cuenta\":null}")]
    public void EstadoCorruptoFallaSinIntentarReinicializar(string json) =>
        Assert.Equal(ErrorSesionMovil.EstadoLocalInvalido,
            Assert.Throws<SesionMovilException>(() => SerializadorSesionMovil.Deserializar(json)).Error);

    [Fact]
    public void VersionAusenteOCampoDesconocidoRechazanEstadoCompleto()
    {
        var json = SerializadorSesionMovil.Serializar(ConSesion().Estado!);
        Assert.Throws<SesionMovilException>(() => SerializadorSesionMovil.Deserializar(json.Replace("\"version\":1,", "")));
        Assert.Throws<SesionMovilException>(() => SerializadorSesionMovil.Deserializar("{\"desconocido\":true," + json[1..]));
    }

    [Fact]
    public async Task SesionesDeDistintosOrigenesSeMantienenAisladasAlCambiarBackend()
    {
        var almacen = ConSesion();
        var apiGcp = new Uri("https://api.gcp.nexoruta.test/");
        using var clienteGcp = new ClienteSesionMovil(apiGcp, almacen, new Transporte((request, _) =>
            Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("login")
                ? Json(TokensJson())
                : Json(ContextoJson(Cuenta)))), new Reloj());

        Assert.Null(await clienteGcp.LeerCuentaGuardadaAsync());
        Assert.Equal(Cuenta, await clienteGcp.IngresarAsync("repartidor@example.test", "clave"));

        using var clienteDocker = Crear(almacen, (_, _) => throw new InvalidOperationException("No debe llamar a la red."));
        Assert.Equal(Cuenta, await clienteDocker.LeerCuentaGuardadaAsync());
        Assert.Equal(Cuenta, await clienteGcp.LeerCuentaGuardadaAsync());
    }

    private static ClienteSesionMovil Crear(Almacen almacen, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> enviar) =>
        new(Api, almacen, new Transporte(enviar), new Reloj());

    private static Almacen ConSesion(bool expirado = false) => new()
    {
        Estado = new() { Api = Api.AbsoluteUri, Cuenta = Cuenta, Tokens = Tokens(expirado ? Ahora.AddMinutes(-1) : Ahora.AddMinutes(15)) }
    };

    private static TokensSesionMovil Tokens(DateTimeOffset expira, DateTimeOffset? fin = null) => new()
    {
        AccessToken = "jwt-inicial", TokenType = "Bearer", ExpiresAtUtc = expira,
        RefreshToken = Refresh, RefreshExpiresAtUtc = fin ?? Ahora.AddDays(30), SesionId = Sesion
    };

    private static string TokensJson(string access = "jwt-inicial", string? refresh = null, Guid? sesion = null, DateTimeOffset? fin = null) =>
        JsonSerializer.Serialize(new { accessToken = access, tokenType = "Bearer", expiresAtUtc = Ahora.AddMinutes(15),
            refreshToken = refresh ?? Refresh, refreshExpiresAtUtc = fin ?? Ahora.AddDays(30), sesionId = sesion ?? Sesion });

    private static string ContextoJson(CuentaMovil cuenta) => JsonSerializer.Serialize(new
    {
        usuarioId = cuenta.UsuarioId, accesoId = cuenta.AccesoId, operadorId = cuenta.OperadorId,
        comercioId = (Guid?)null, rolId = Guid.NewGuid(), rolCodigo = "rol-operador", tipo = "Operador",
        usuarioEmail = "u@example.test", operadorNombre = "Operador", tipoAcceso = 0
    });

    private static string PerfilJson(CuentaMovil cuenta) => JsonSerializer.Serialize(new
    {
        accesoId = cuenta.AccesoId, usuarioId = cuenta.UsuarioId, operadorId = cuenta.OperadorId,
        nombre = "Repartidor", activo = true, version = 1
    });

    private static HttpResponseMessage Json(string contenido, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(contenido, Encoding.UTF8, "application/json") };

    private static async Task<SesionMovilException> ErrorAsync(ErrorSesionMovil esperado, Func<Task> accion)
    {
        var ex = await Assert.ThrowsAsync<SesionMovilException>(accion);
        Assert.Equal(esperado, ex.Error);
        return ex;
    }

    private sealed class Transporte(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> enviar) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => enviar(request, ct);
    }

    private sealed class Almacen : IAlmacenSesionMovil
    {
        private readonly Dictionary<string, EstadoSesionMovil> estados = [];
        private EstadoSesionMovil? actual;
        public EstadoSesionMovil? Estado
        {
            get => actual;
            set
            {
                actual = value;
                if (value is not null) estados[value.Api] = value;
            }
        }
        public int FallarEnEscritura { get; set; }
        private int escrituras;
        public bool FallarAlEliminar { get; set; }
        public Task EliminarAsync(string api) { if (FallarAlEliminar) throw new IOException("SecureStorage no disponible"); estados.Remove(api); if (Estado?.Api == api) actual = null; return Task.CompletedTask; }
        public Task<EstadoSesionMovil?> LeerAsync(string api) => Task.FromResult(estados.GetValueOrDefault(api));
        public Task GuardarAsync(EstadoSesionMovil estado)
        {
            if (++escrituras == FallarEnEscritura) throw new IOException("Almacén no disponible");
            estados[estado.Api] = estado;
            actual = estado;
            return Task.CompletedTask;
        }
    }

    private sealed class Reloj : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Ahora;
    }
}
