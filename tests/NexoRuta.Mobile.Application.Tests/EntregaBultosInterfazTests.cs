using System.Net;
using NexoRuta.Mobile.Core.Identidad;
using System.Text.Json;
using System.Text.Json.Nodes;
using NexoRuta.Mobile.Api.Reparto;
using NexoRuta.Mobile.Core.Jornadas;
using NexoRuta.Mobile.Core.Rutas;
using NexoRuta.Mobile.Presentacion;
using NexoRuta.Mobile.Storage;
using NexoRuta.Mobile.Captura;

namespace NexoRuta.Mobile.Application.Tests;
public sealed partial class SincronizadorJornadaTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task H3_LaPruebaRemotaSoloSeCompletaEnLaVisitaQueLaEntrego(bool enEstaRuta)
    {
        using var f = new Escenario(); await f.Preparar(cerrar: false, soloInicio: true, pruebaPendiente: true, enEstaRuta: enEstaRuta);
        await f.Datos.ModificarAsync(f.Cuenta, f.Jornada.Id, j => j.ConfirmarCarga(DateTimeOffset.UtcNow));
        using var cliente = f.Cliente(async (r, ct) => Confirmar(JsonSerializer.Deserialize(await r.Content!.ReadAsByteArrayAsync(ct), ContratosRepartoJson.Default.SobreOperacion)!, f.Envio, f.Bulto));
        using var sync = new SincronizadorJornada(cliente, f.Datos, f.Archivos); await sync.SincronizarAsync(f.Cuenta, f.Jornada.Id);
        var hoja = SerializadorHojaRuta.Deserializar((await f.Datos.LeerHojaAsync(f.Cuenta, f.Jornada.RutaId))!, f.Cuenta);
        var io = new CapturaInterfaz(); var padre = new HojaRutaViewModel(hoja, f.Cuenta, f.Datos, new UnusedInteraction(), io, io); await padre.Refrescar();
        var pantalla = new EntregaViewModel(padre, hoja.Paradas.Single(), f.Datos, new EvidenciasLocales(f.Datos, f.Archivos), io);
        Assert.Equal(enEstaRuta, pantalla.PuedeRegistrar); Assert.Equal(enEstaRuta, pantalla.TomarFoto.CanExecute(null));
        Assert.False(pantalla.MostrarIntento);
        pantalla.Desconectar();
    }

    [Fact]
    public async Task H3_EntregaOfflinePermiteCompletarPruebaDesdeComandosTrasReabrir()
    {
        using var f = new Escenario();
        var hoja = await PrepararInterfaz(f, false);
        var offline = false;
        using var cliente = f.Cliente(async (r, ct) => {
            if (offline) throw new HttpRequestException("Offline controlado");
            return Confirmar(JsonSerializer.Deserialize(await r.Content!.ReadAsByteArrayAsync(ct), ContratosRepartoJson.Default.SobreOperacion)!, f.Envio, f.Bulto);
        });
        using var sync = new SincronizadorJornada(cliente, f.Datos, f.Archivos);
        await sync.SincronizarAsync(f.Cuenta, f.Jornada.Id); offline = true;
        var io = new CapturaInterfaz();
        var padre = new HojaRutaViewModel(hoja, f.Cuenta, f.Datos, new UnusedInteraction(), io, io, cliente, sync);
        await padre.Refrescar();
        var entrega = new EntregaViewModel(padre, hoja.Paradas[0], f.Datos, new EvidenciasLocales(f.Datos, f.Archivos), io);
        await entrega.InicializarAsync(); entrega.Seleccion.Single().Escaneado = true;
        Assert.True(entrega.Entregar.CanExecute(null)); await EjecutarComando(entrega.Entregar);
        Assert.True(entrega.PuedeRegistrar); Assert.True(entrega.TomarFoto.CanExecute(null)); Assert.False(entrega.MostrarIntento);
        entrega.Desconectar();
        using var reabierto = new RepositorioJornadasSqlite(f.Raiz + "/sql", f.Claves);
        using var archivos = new RepositorioArchivosCifrados(f.Raiz + "/evidencias", f.Claves);
        var nuevoPadre = new HojaRutaViewModel(hoja, f.Cuenta, reabierto, new UnusedInteraction(), io, io);
        await nuevoPadre.Refrescar();
        var nueva = new EntregaViewModel(nuevoPadre, hoja.Paradas[0], reabierto, new EvidenciasLocales(reabierto, archivos), io);
        await nueva.InicializarAsync();
        Assert.True(nueva.PuedeRegistrar); Assert.True(nueva.TomarFoto.CanExecute(null));
        await EjecutarComando(nueva.TomarFoto); nueva.Nombre = "Receptor offline";
        await EjecutarComando(nueva.Entregar);
        var operaciones = (await reabierto.LeerAsync(f.Cuenta, f.Jornada.Id))!.Operaciones.Where(o => o.Tipo == TipoOperacionLocal.EntregaBultos).ToArray();
        Assert.Equal(2, operaciones.Length);
        Assert.NotEqual(operaciones[0].Id, operaciones[1].Id);
        Assert.Equal(f.Bulto, operaciones[1].Datos.Deserialize(DatosOperacionJson.Default.DatosOperacionMovil)!.ResultadosBultos!.Single().BultoId);
        var foto = operaciones[1].Datos.Deserialize(DatosOperacionJson.Default.DatosOperacionMovil)!.FotoId!.Value;
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, await archivos.LeerAsync(f.Cuenta, foto));
        Assert.Single(nuevoPadre.BultosActuales(hoja.Paradas[0]), b => b.Resultado == "EntregaPendienteValidacion");
        nueva.Desconectar();
    }

    [Fact]
    public async Task H4_RetiroRechazadoNoConfirmaCustodiaNiRendicionTrasReinicio()
    {
        using var f = new Escenario(); var hoja = await PrepararInterfaz(f, true);
        using var cliente = f.Cliente(async (r, ct) => {
            if (r.Method == HttpMethod.Put) return new(HttpStatusCode.NoContent);
            var op = JsonSerializer.Deserialize(await r.Content!.ReadAsByteArrayAsync(ct), ContratosRepartoJson.Default.SobreOperacion)!;
            if (op.Tipo != "RetirarBultos") return Confirmar(op, f.Envio, f.Bulto);
            return Json(new ConfirmacionOperacion { Id = op.Id, Secuencia = op.Secuencia, Resultado = "Conflicto", Detalle = "El retiro excede la capacidad física actual del vehículo.", VersionRuta = op.Secuencia });
        });
        using var sync = new SincronizadorJornada(cliente, f.Datos, f.Archivos); await sync.SincronizarAsync(f.Cuenta, f.Jornada.Id);
        var io = new CapturaInterfaz();
        var padre = new HojaRutaViewModel(hoja, f.Cuenta, f.Datos, new UnusedInteraction(), io, io, cliente, sync); await padre.Refrescar();
        var entrega = new EntregaViewModel(padre, hoja.Paradas[0], f.Datos, new EvidenciasLocales(f.Datos, f.Archivos), io); await entrega.InicializarAsync();
        entrega.Seleccion.Single().Escaneado = true; await EjecutarComando(entrega.TomarFoto); await EjecutarComando(entrega.Firmar); await EjecutarComando(entrega.Entregar);
        Assert.Contains("rechazó", entrega.Mensaje); Assert.DoesNotContain("Retiro registrado", entrega.Mensaje);
        Assert.Empty(padre.BultosParaRendir()); Assert.False(entrega.Entregar.CanExecute(null));
        entrega.Desconectar();
        using var reabierto = new RepositorioJornadasSqlite(f.Raiz + "/sql", f.Claves);
        var recibidos = new List<byte[]>(); var aplicadas = new HashSet<Guid>(); var perderRespuesta = true;
        using var resolver = f.Cliente(async (r, ct) => {
            if (r.Method == HttpMethod.Put) return new(HttpStatusCode.NoContent);
            var bytes = await r.Content!.ReadAsByteArrayAsync(ct);
            var op = JsonSerializer.Deserialize(bytes, ContratosRepartoJson.Default.SobreOperacion)!;
            recibidos.Add(bytes); aplicadas.Add(op.Id);
            Assert.NotNull(op.Datos.Deserialize(DatosOperacionJson.Default.DatosOperacionMovil)!.ResuelveOperacionId);
            if (perderRespuesta) { perderRespuesta = false; throw new HttpRequestException("Respuesta perdida después de aplicar"); }
            return Json(new ConfirmacionOperacion { Id = op.Id, Secuencia = op.Secuencia, Resultado = "Aplicada", VersionRuta = op.Secuencia,
                VersionesEnvio = new() { [f.Envio] = 4 }, EstadosEnvio = new() { [f.Envio] = "Retirado" }, VersionesBulto = new() { [f.Bulto] = 1 } });
        });
        using var sincronizar = new SincronizadorJornada(resolver, reabierto, f.Archivos);
        var nuevo = new HojaRutaViewModel(hoja, f.Cuenta, reabierto, new UnusedInteraction(), io, io, resolver, sincronizar); await nuevo.Refrescar();
        Assert.Empty(nuevo.BultosParaRendir());
        var pantalla = new EntregaViewModel(nuevo, hoja.Paradas[0], reabierto, new EvidenciasLocales(reabierto, f.Archivos), io); await pantalla.InicializarAsync();
        Assert.False(pantalla.Entregar.CanExecute(null)); Assert.True(pantalla.ReintentarRetiro.CanExecute(null));
        Assert.Contains("capacidad", pantalla.Mensaje);
        Assert.False((await reabierto.LeerAsync(f.Cuenta, f.Jornada.Id))!.TienePendientes);
        await EjecutarComando(pantalla.ReintentarRetiro);
        Assert.Contains("pendiente", pantalla.Mensaje);
        Assert.Empty(nuevo.BultosParaRendir()); Assert.False(pantalla.ReintentarRetiro.CanExecute(null));
        Assert.True(await nuevo.IntentarSincronizar());
        Assert.Single(nuevo.BultosParaRendir()); Assert.Single(aplicadas); Assert.Equal(2, recibidos.Count); Assert.Equal(recibidos[0], recibidos[1]);
        var retiros = (await reabierto.LeerAsync(f.Cuenta, f.Jornada.Id))!.Operaciones.Where(o => o.Tipo == TipoOperacionLocal.RetirarBultos).ToArray();
        Assert.Equal(2, retiros.Length); Assert.NotEqual(retiros[0].Id, retiros[1].Id);
        Assert.True(nuevo.OperacionRechazada(retiros[0]));
        pantalla.Desconectar();
    }

    private static async Task<HojaRuta> PrepararInterfaz(Escenario f, bool retiro)
    {
        await f.Preparar(cerrar: false, soloInicio: true, esRetiro: retiro);
        await f.Datos.ModificarAsync(f.Cuenta, f.Jornada.Id, j => j.ConfirmarCarga(DateTimeOffset.UtcNow));
        return SerializadorHojaRuta.Deserializar((await f.Datos.LeerHojaAsync(f.Cuenta, f.Jornada.RutaId))!, f.Cuenta);
    }
    private static async Task EjecutarComando(ComandoAsincrono comando)
    {
        Assert.True(comando.CanExecute(null));
        await comando.EjecutarAsync().WaitAsync(TimeSpan.FromSeconds(10));
    }
    private sealed class CapturaInterfaz : NexoRuta.Mobile.Captura.IInteraccionReparto, IVerificacionLocal
    {
        public bool ConfirmarResolucion { get; set; } = true;
        public Queue<string?> Codigos { get; } = new();
        public Task<bool> ConfirmarResolucionAsync(bool retiro) => Task.FromResult(ConfirmarResolucion);
        public Task<bool> VerificarAsync(string motivo, CancellationToken ct = default) => Task.FromResult(true);
        public Task<byte[]?> FotoAsync() => Task.FromResult<byte[]?>([1, 2, 3, 4]);
        public Task<byte[]?> FirmaAsync() => Task.FromResult<byte[]?>([5, 6, 7, 8]);
        public Task<string?> EscanearAsync() => Task.FromResult(Codigos.TryDequeue(out var codigo) ? codigo : null);
        public Task<Guid[]?> SeleccionarRendicionAsync(IReadOnlyList<BultoRuta> bultos) => Task.FromResult<Guid[]?>(null);
        public Task<bool> ConfirmarSalidaConDiferenciasAsync(int bultosPendientes, int paradasAfectadas) => Task.FromResult(true);
        public Task<bool> ConfirmarCierreAsync(bool pruebaPendiente, int bultosSinRendir) => Task.FromResult(true);
    }
}
