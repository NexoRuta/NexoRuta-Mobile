using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using NexoRuta.Mobile.Api.Reparto;
using NexoRuta.Mobile.Core.Jornadas;
using NexoRuta.Mobile.Core.Rutas;
using NexoRuta.Mobile.Presentacion;
using NexoRuta.Mobile.Storage;

namespace NexoRuta.Mobile.Application.Tests;

public sealed partial class SincronizadorJornadaTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HistoricalLoadReceiptIsReplayedWithoutRewritingHistoryBeforeNewDelivery(bool invalidReplay)
    {
        using var f = new Escenario(); await f.Preparar(soloInicio: true, cerrar: false);
        await f.Datos.ModificarAsync(f.Cuenta, f.Jornada.Id, j => j.RegistrarCarga(f.Envio,
            Elemento($"{{\"bultoId\":\"{f.Bulto}\"}}"), DateTimeOffset.UtcNow));
        var original = (await f.Datos.LeerAsync(f.Cuenta, f.Jornada.Id))!;
        var history = new Dictionary<Guid, (byte[] Envelope, byte[] Receipt)>();
        foreach (var op in original.Operaciones)
        {
            var envelope = JsonSerializer.SerializeToUtf8Bytes(new SobreOperacion { Id = op.Id,
                Secuencia = op.Secuencia, Tipo = op.Tipo.ToString(), EnvioId = op.EnvioId,
                VersionEnvio = op.EnvioId is null ? null : 2, OcurridaEn = op.OcurridaEn, Datos = op.Datos },
                ContratosRepartoJson.Default.SobreOperacion);
            // The old DTO discarded the new server field before persisting its receipt.
            var node = JsonSerializer.SerializeToNode(new ConfirmacionOperacion { Id = op.Id,
                Secuencia = op.Secuencia, Resultado = "Aplicada", VersionRuta = op.Secuencia, VersionEnvio = op.EnvioId is null ? null : 2 },
                ContratosRepartoJson.Default.ConfirmacionOperacion)!;
            node.AsObject().Remove("versionesBulto");
            var receipt = System.Text.Encoding.UTF8.GetBytes(node.ToJsonString());
            await f.Datos.GuardarSobreAsync(f.Cuenta, f.Jornada.Id, op.Id, envelope);
            await f.Datos.ConfirmarSobreAsync(f.Cuenta, f.Jornada.Id, op.Id, receipt);
            history.Add(op.Id, (envelope, receipt));
        }
        await f.Datos.ModificarAsync(f.Cuenta, f.Jornada.Id, j => j.RegistrarBultos(f.Envio,
            JsonSerializer.SerializeToElement(new DatosOperacionMovil { ResultadosBultos = [new(f.Bulto, 0, true, null)] },
                DatosOperacionJson.Default.DatosOperacionMovil), DateTimeOffset.UtcNow));
        using var reopened = new RepositorioJornadasSqlite(f.Raiz + "/sql", f.Claves);
        var replayed = 0; var delivered = 0; byte[]? deliveryEnvelope = null;
        using var client = f.Cliente(async (request, ct) =>
        {
            var bytes = await request.Content!.ReadAsByteArrayAsync(ct);
            var op = JsonSerializer.Deserialize(bytes, ContratosRepartoJson.Default.SobreOperacion)!;
            if (op.Tipo == "CargarBulto")
            {
                replayed++; Assert.Equal(history[op.Id].Envelope, bytes);
                return Json(new ConfirmacionOperacion { Id = op.Id, Secuencia = op.Secuencia,
                    Resultado = "Aplicada", VersionRuta = op.Secuencia, VersionEnvio = 2,
                    VersionesBulto = invalidReplay ? [] : new() { [f.Bulto] = 2 } });
            }
            Assert.Equal("EntregaBultos", op.Tipo); delivered++;
            Assert.Equal(2, op.Datos.GetProperty("resultadosBultos")[0].GetProperty("version").GetInt64());
            if (deliveryEnvelope is null) { deliveryEnvelope = bytes; throw new HttpRequestException("Respuesta perdida después de aplicar"); }
            Assert.Equal(deliveryEnvelope, bytes);
            return Json(new ConfirmacionOperacion { Id = op.Id, Secuencia = op.Secuencia,
                Resultado = "Aplicada", VersionRuta = op.Secuencia, VersionesBulto = new() { [f.Bulto] = 4 } });
        });
        using var sync = new SincronizadorJornada(client, reopened, f.Archivos);
        if (invalidReplay)
        {
            var error = await Assert.ThrowsAsync<InvalidDataException>(() => sync.SincronizarAsync(f.Cuenta, f.Jornada.Id));
            Assert.Contains("acuse", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, delivered);
            Assert.True((await reopened.LeerAsync(f.Cuenta, f.Jornada.Id))!.TienePendientes);
        }
        else
        {
            await Assert.ThrowsAsync<HttpRequestException>(() => sync.SincronizarAsync(f.Cuenta, f.Jornada.Id));
            Assert.Equal(1, await sync.SincronizarAsync(f.Cuenta, f.Jornada.Id)); Assert.Equal(2, delivered);
        }
        Assert.Equal(1, replayed);
        foreach (var op in original.Operaciones)
        {
            Assert.Equal(history[op.Id].Envelope, await reopened.LeerSobreAsync(f.Cuenta, f.Jornada.Id, op.Id));
            Assert.Equal(history[op.Id].Receipt, await reopened.LeerConfirmacionAsync(f.Cuenta, f.Jornada.Id, op.Id));
        }
    }

    [Fact]
    public async Task PendingReasonAfterBothScansRegistersOnlyTheOtherParcelAsDelivered()
    {
        using var f = new Escenario(); var stored = await PrepararInterfaz(f, false);
        using var client = f.Cliente(async (r, ct) => Confirmar(JsonSerializer.Deserialize(
            await r.Content!.ReadAsByteArrayAsync(ct), ContratosRepartoJson.Default.SobreOperacion)!, f.Envio, f.Bulto));
        using var sync = new SincronizadorJornada(client, f.Datos, f.Archivos);
        await sync.SincronizarAsync(f.Cuenta, f.Jornada.Id);
        var first = stored.Paradas[0];
        var p = new ParadaRuta { EnvioId = first.EnvioId, Activa = true, Estado = first.Estado,
            Orden = first.Orden, Reglas = new() { PermiteEntregaParcial = true, Motivos = first.Reglas.Motivos }, Bultos = [first.Bultos[0], new() { Id = Guid.NewGuid(), Codigo = "SECOND" }] };
        var io = new CapturaInterfaz(); var parent = new HojaRutaViewModel(stored, f.Cuenta, f.Datos, new UnusedInteraction(), io, io);
        await parent.Refrescar();
        var vm = new EntregaViewModel(parent, p, f.Datos, new EvidenciasLocales(f.Datos, f.Archivos), io);
        await vm.InicializarAsync();
        io.Codigos.Enqueue(first.Bultos[0].Codigo); io.Codigos.Enqueue("SECOND");
        await EjecutarComando(vm.EscanearBulto); await EjecutarComando(vm.EscanearBulto);
        vm.Seleccion[0].Motivo = vm.Motivos[0];
        Assert.False(vm.Seleccion[0].Escaneado); Assert.Contains("1 de 2", vm.ResumenSeleccion);
        io.Codigos.Enqueue(first.Bultos[0].Codigo); await EjecutarComando(vm.EscanearBulto);
        Assert.True(vm.Seleccion[0].Escaneado); Assert.Null(vm.Seleccion[0].Motivo);
        vm.Seleccion[0].Motivo = vm.Motivos[0];
        await EjecutarComando(vm.Entregar);
        var op = (await f.Datos.LeerAsync(f.Cuenta, f.Jornada.Id))!.Operaciones.Last();
        var results = op.Datos.Deserialize(DatosOperacionJson.Default.DatosOperacionMovil)!.ResultadosBultos!;
        Assert.False(results[0].Entregado); Assert.Equal("AUSENTE", results[0].Motivo);
        Assert.True(results[1].Entregado); Assert.Null(results[1].Motivo);
        vm.Desconectar();
    }

    [Fact]
    public async Task RejectedDeliveryRemainsAConflictAndCanBeExplicitlyResolvedWithANewCommand()
    {
        using var f = new Escenario(); var sheet = await PrepararInterfaz(f, false);
        using var client = f.Cliente(async (r, ct) =>
        {
            var op = JsonSerializer.Deserialize(await r.Content!.ReadAsByteArrayAsync(ct), ContratosRepartoJson.Default.SobreOperacion)!;
            return op.Tipo == "EntregaBultos" && op.Datos.Deserialize(DatosOperacionJson.Default.DatosOperacionMovil)!.ResuelveOperacionId is null
                ? Json(new ConfirmacionOperacion { Id = op.Id, Secuencia = op.Secuencia,
                    Resultado = "Conflicto", Detalle = "Un bulto cambió", VersionRuta = op.Secuencia }) : Confirmar(op, f.Envio, f.Bulto);
        });
        using var sync = new SincronizadorJornada(client, f.Datos, f.Archivos); await sync.SincronizarAsync(f.Cuenta, f.Jornada.Id);
        var io = new CapturaInterfaz(); var parent = new HojaRutaViewModel(sheet, f.Cuenta, f.Datos, new UnusedInteraction(), io, io, client, sync);
        await parent.Refrescar();
        var vm = new EntregaViewModel(parent, sheet.Paradas[0], f.Datos, new EvidenciasLocales(f.Datos, f.Archivos), io);
        await vm.InicializarAsync(); vm.Seleccion[0].Escaneado = true; await EjecutarComando(vm.Entregar);
        var rejected = (await f.Datos.LeerAsync(f.Cuenta, f.Jornada.Id))!.Operaciones.Last();
        Assert.False((await f.Datos.LeerAsync(f.Cuenta, f.Jornada.Id))!.TienePendientes);
        Assert.Contains("rechaz", vm.EstadoActual, StringComparison.OrdinalIgnoreCase);
        Assert.False(parent.EntregaFisica(f.Envio));
        var before = await f.Datos.LeerSobreAsync(f.Cuenta, f.Jornada.Id, rejected.Id);
        io.ConfirmarResolucion = false;
        await EjecutarComando(vm.Entregar);
        Assert.Equal(rejected.Id, (await f.Datos.LeerAsync(f.Cuenta, f.Jornada.Id))!.Operaciones.Last().Id);
        io.ConfirmarResolucion = true;
        // The same explicit resolution command is reused for deliveries and pickups.
        Assert.True(vm.Entregar.CanExecute(null));
        await EjecutarComando(vm.Entregar);
        var resolved = (await f.Datos.LeerAsync(f.Cuenta, f.Jornada.Id))!.Operaciones.Last();
        Assert.NotEqual(rejected.Id, resolved.Id); Assert.True(resolved.Secuencia > rejected.Secuencia);
        Assert.Equal(rejected.Id, resolved.Datos.Deserialize(DatosOperacionJson.Default.DatosOperacionMovil)!.ResuelveOperacionId);
        Assert.True(parent.OperacionRechazada(rejected)); Assert.True(parent.EntregaFisica(f.Envio));
        Assert.Equal(before, await f.Datos.LeerSobreAsync(f.Cuenta, f.Jornada.Id, rejected.Id)); vm.Desconectar();
    }

    [Fact]
    public async Task AppliedLoadVersionIsProjectedAfterSqliteReopenAndUsedByNewDelivery()
    {
        using var f = new Escenario(); await f.Preparar(soloInicio: true, cerrar: false);
        await f.Datos.ModificarAsync(f.Cuenta, f.Jornada.Id, j =>
        {
            j.RegistrarCarga(f.Envio, Elemento($"{{\"bultoId\":\"{f.Bulto}\"}}"), DateTimeOffset.UtcNow);
            j.ConfirmarCarga(DateTimeOffset.UtcNow);
        });
        using var client = f.Cliente(async (r, ct) =>
        {
            var op = JsonSerializer.Deserialize(await r.Content!.ReadAsByteArrayAsync(ct), ContratosRepartoJson.Default.SobreOperacion)!;
            return op.Tipo == "CargarBulto" ? Json(new ConfirmacionOperacion { Id = op.Id, Secuencia = op.Secuencia,
                Resultado = "Aplicada", VersionRuta = op.Secuencia, VersionesBulto = new() { [f.Bulto] = 2 } }) : Confirmar(op, f.Envio, f.Bulto);
        });
        using var sync = new SincronizadorJornada(client, f.Datos, f.Archivos); await sync.SincronizarAsync(f.Cuenta, f.Jornada.Id);
        using var reopened = new RepositorioJornadasSqlite(f.Raiz + "/sql", f.Claves);
        var sheet = SerializadorHojaRuta.Deserializar((await reopened.LeerHojaAsync(f.Cuenta, f.Jornada.RutaId))!, f.Cuenta);
        Assert.Equal(0, sheet.Paradas[0].Bultos[0].Version); // immutable admission snapshot
        var io = new CapturaInterfaz(); var parent = new HojaRutaViewModel(sheet, f.Cuenta, reopened, new UnusedInteraction(), io, io);
        await parent.Refrescar(); Assert.Equal(2, parent.BultosActuales(sheet.Paradas[0])[0].Version);
        var vm = new EntregaViewModel(parent, sheet.Paradas[0], reopened, new EvidenciasLocales(reopened, f.Archivos), io);
        await vm.InicializarAsync(); Assert.Equal(2, vm.Seleccion[0].Bulto.Version);
        vm.Seleccion[0].Escaneado = true; await EjecutarComando(vm.Entregar);
        var delivery = (await reopened.LeerAsync(f.Cuenta, f.Jornada.Id))!.Operaciones.Last();
        Assert.Equal(2, delivery.Datos.Deserialize(DatosOperacionJson.Default.DatosOperacionMovil)!.ResultadosBultos![0].Version);
        vm.Desconectar();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FreshAppliedLoadWithoutAdvancedParcelVersionCannotClearThePendingCommand(bool unchanged)
    {
        using var f = new Escenario(); await f.Preparar(cerrar: false);
        using var client = f.Cliente(async (r, ct) =>
        {
            var op = JsonSerializer.Deserialize(await r.Content!.ReadAsByteArrayAsync(ct), ContratosRepartoJson.Default.SobreOperacion)!;
            return op.Tipo == "CargarBulto" ? Json(new ConfirmacionOperacion { Id = op.Id, Secuencia = op.Secuencia,
                Resultado = "Aplicada", VersionRuta = op.Secuencia, VersionesBulto = unchanged ? new() { [f.Bulto] = 0 } : [] }) : Confirmar(op, f.Envio, f.Bulto);
        });
        using var sync = new SincronizadorJornada(client, f.Datos, f.Archivos);
        await Assert.ThrowsAsync<InvalidDataException>(() => sync.SincronizarAsync(f.Cuenta, f.Jornada.Id));
        var journey = (await f.Datos.LeerAsync(f.Cuenta, f.Jornada.Id))!;
        var load = journey.Operaciones.Single(o => o.Tipo == TipoOperacionLocal.CargarBulto);
        Assert.True(journey.TienePendientes); Assert.Null(await f.Datos.LeerConfirmacionAsync(f.Cuenta, f.Jornada.Id, load.Id));
        Assert.NotNull(await f.Datos.LeerSobreAsync(f.Cuenta, f.Jornada.Id, load.Id));
    }
}
