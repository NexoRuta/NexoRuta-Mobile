using System.Net;
using System.Text.Json;
using NexoRuta.Mobile.Api.Identidad;
using NexoRuta.Mobile.Core.Jornadas;
namespace NexoRuta.Mobile.Api.Tests;

public sealed partial class ClienteSesionMovilTests
{
    [Fact]
    public async Task OtraCuentaSoloPuedeIngresarDespuesDeCompletarLaSalida()
    {
        var almacen = ConSesion(); var otra = new CuentaMovil(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        using var cliente = Crear(almacen, (r, _) =>
        {
            var path = r.RequestUri!.AbsolutePath;
            return Task.FromResult(path.EndsWith("logout") ? new HttpResponseMessage(HttpStatusCode.NoContent)
                : path.EndsWith("login") ? Json(TokensJson()) : Json(ContextoJson(otra)));
        });
        await cliente.CerrarSesionAsync(Cuenta, null);
        Assert.Equal(otra, await cliente.IngresarAsync("otra@example.test", "clave"));
        Assert.Equal(otra, almacen.Estado!.Cuenta);
        await ErrorAsync(ErrorSesionMovil.CuentaDiferente, () => cliente.RegistrarInstalacionAsync(Cuenta,
            new(Guid.NewGuid(), "Android", new string('a', 32), true)));
    }
    [Fact]
    public async Task SalidaDesactivaSoloLaInstalacionPropiaRevocaSesionYDespuesLimpiaSecureStorage()
    {
        var almacen = ConSesion(); var instalacion = Guid.NewGuid(); var pasos = new List<string>();
        using var cliente = Crear(almacen, async (r, ct) =>
        {
            Assert.NotNull(almacen.Estado); pasos.Add(r.Method.Method);
            if (r.Method == HttpMethod.Delete)
            {
                Assert.Equal($"/api/mobile/instalaciones/{instalacion}", r.RequestUri!.AbsolutePath);
                Assert.Equal("jwt-inicial", r.Headers.Authorization!.Parameter);
            }
            else
            {
                Assert.Equal("/api/auth/mobile/logout", r.RequestUri!.AbsolutePath);
                Assert.Null(r.Headers.Authorization);
                using var body = JsonDocument.Parse(await r.Content!.ReadAsByteArrayAsync(ct));
                Assert.Equal(Refresh, body.RootElement.GetProperty("refreshToken").GetString());
            }
            return new(HttpStatusCode.NoContent);
        });
        await cliente.CerrarSesionAsync(Cuenta, instalacion);
        Assert.Equal(new[] { "DELETE", "POST" }, pasos); Assert.Null(almacen.Estado);
    }

    [Fact]
    public async Task SalidaRenuevaAntesDeBajaYRevocaElRefreshNuevo()
    {
        var almacen = ConSesion(expirado: true); var pasos = new List<string>();
        using var cliente = Crear(almacen, async (r, ct) =>
        {
            pasos.Add(r.RequestUri!.AbsolutePath);
            if (r.RequestUri.AbsolutePath.EndsWith("refresh"))
            { Assert.Null(almacen.Estado!.Tokens); return Json(TokensJson("nuevo-jwt", SiguienteRefresh)); }
            if (r.Method == HttpMethod.Delete) Assert.Equal("nuevo-jwt", r.Headers.Authorization!.Parameter);
            else
            {
                using var body = JsonDocument.Parse(await r.Content!.ReadAsByteArrayAsync(ct));
                Assert.Equal(SiguienteRefresh, body.RootElement.GetProperty("refreshToken").GetString());
            }
            return new(HttpStatusCode.NoContent);
        });
        await cliente.CerrarSesionAsync(Cuenta, Guid.NewGuid());
        Assert.EndsWith("refresh", pasos[0]); Assert.Equal(3, pasos.Count); Assert.Null(almacen.Estado);
    }

    [Fact]
    public async Task RespuestaDeRevocacionPerdidaConservaCuentaParaRepetirSalida()
    {
        var almacen = ConSesion(); var original = almacen.Estado; var salidas = 0;
        using var cliente = Crear(almacen, (r, _) =>
        {
            if (r.Method == HttpMethod.Delete) return Task.FromResult(new HttpResponseMessage(salidas == 0 ? HttpStatusCode.NoContent : HttpStatusCode.NotFound));
            if (++salidas == 1) throw new HttpRequestException("Respuesta perdida después de revocar");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        });
        var id = Guid.NewGuid();
        await Assert.ThrowsAsync<HttpRequestException>(() => cliente.CerrarSesionAsync(Cuenta, id));
        Assert.Same(original, almacen.Estado);
        await cliente.CerrarSesionAsync(Cuenta, id);
        Assert.Null(almacen.Estado); Assert.Equal(2, salidas);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task SalidaSinConfirmacionEsperadaNoBorraLaCuenta(HttpStatusCode status)
    {
        var almacen = ConSesion(); var original = almacen.Estado;
        using var cliente = Crear(almacen, (_, _) => Task.FromResult(new HttpResponseMessage(status)));
        await Assert.ThrowsAsync<SesionMovilException>(() => cliente.CerrarSesionAsync(Cuenta, null));
        Assert.Same(original, almacen.Estado);
    }

    [Fact]
    public async Task SalidaDeCuentaDistintaNoEnviaNada()
    {
        var almacen = ConSesion();
        using var cliente = Crear(almacen, (_, _) => throw new InvalidOperationException("No enviar"));
        await ErrorAsync(ErrorSesionMovil.CuentaDiferente, () => cliente.CerrarSesionAsync(new CuentaMovil(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), null));
        Assert.NotNull(almacen.Estado);
    }

    [Fact]
    public async Task RegistroEncoladoDuranteSalidaNoReactivaElDestino()
    {
        var almacen = ConSesion(); var borrando = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var continuar = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cliente = Crear(almacen, async (r, ct) =>
        {
            Assert.NotEqual(HttpMethod.Put, r.Method);
            if (r.Method == HttpMethod.Delete) { borrando.SetResult(); await continuar.Task.WaitAsync(ct); }
            return new(HttpStatusCode.NoContent);
        });
        var salida = cliente.CerrarSesionAsync(Cuenta, Guid.NewGuid());
        await borrando.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var registro = cliente.RegistrarInstalacionAsync(Cuenta, new(Guid.NewGuid(), "Android", new string('a', 32), true));
        continuar.SetResult(); await salida;
        await ErrorAsync(ErrorSesionMovil.ReingresoNecesario, () => registro);
        Assert.Null(almacen.Estado);
    }

    [Fact]
    public async Task FalloAlEliminarSesionRevocadaConservaIdentidadParaReingreso()
    {
        var almacen = ConSesion(); almacen.FallarAlEliminar = true;
        using var cliente = Crear(almacen, (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));
        await Assert.ThrowsAsync<IOException>(() => cliente.CerrarSesionAsync(Cuenta, null));
        Assert.Equal(Cuenta, almacen.Estado!.Cuenta);
        almacen.FallarAlEliminar = false;
        await cliente.CerrarSesionAsync(Cuenta, null); Assert.Null(almacen.Estado);
    }
}
