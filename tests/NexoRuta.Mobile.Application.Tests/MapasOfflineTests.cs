using System.Text.Json;
using NexoRuta.Mobile.Application.Mapas;
using NexoRuta.Mobile.Core.Jornadas;
using NexoRuta.Mobile.Core.Mapas;
using NexoRuta.Mobile.Core.Rutas;
using NexoRuta.Mobile.Storage;

namespace NexoRuta.Mobile.Application.Tests;

public sealed class MapasOfflineTests : IDisposable
{
    private readonly string raiz = Path.Combine(Path.GetTempPath(), "nexoruta-mapas-" + Guid.NewGuid().ToString("N"));
    private readonly CuentaMovil cuenta = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
    private readonly Claves claves = new();
    private readonly ProveedorFalso proveedor = new();
    private readonly HojaRuta hoja = new() { Id = Guid.NewGuid(), Paradas = [new() { EnvioId = Guid.NewGuid(), Latitud = -34.9, Longitud = -56.2 }] };
    private readonly IProgress<ProgresoMapa> progreso = new Progress<ProgresoMapa>();

    [Fact]
    public async Task ReabrePreparacionCifradaPorCuentaYCompruebaElSdkNuevamente()
    {
        string region;
        using (var db = new RepositorioJornadasSqlite(raiz, claves))
        using (var servicio = new ServicioPreparacionMapa(db, proveedor))
        {
            var lista = await servicio.DescargarAsync(cuenta, hoja, progreso); Assert.True(lista.Preparada); Assert.Equal(1024, lista.Bytes);
            region = Assert.Single(proveedor.Regiones.Keys);
        }
        using var reabierto = new RepositorioJornadasSqlite(raiz, claves);
        using var despues = new ServicioPreparacionMapa(reabierto, proveedor);
        Assert.True((await despues.ConsultarAsync(cuenta, hoja)).Preparada);
        Assert.False((await despues.ConsultarAsync(new(Guid.NewGuid(), Guid.NewGuid(), cuenta.OperadorId), hoja)).Preparada);
        proveedor.Regiones.Remove(region);
        Assert.False((await despues.ConsultarAsync(cuenta, hoja)).Preparada);
        Assert.DoesNotContain(Directory.GetFiles(raiz, "*", SearchOption.AllDirectories), p => File.ReadAllText(p).Contains(region));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DescargaParcialOCanceladaConservaLaPreparacionAnterior(bool cancelada)
    {
        using var db = new RepositorioJornadasSqlite(raiz, claves); using var servicio = new ServicioPreparacionMapa(db, proveedor);
        await servicio.DescargarAsync(cuenta, hoja, progreso);
        var anterior = Assert.Single(proveedor.Regiones.Keys);
        proveedor.CancelarDescarga = cancelada; proveedor.CompletarDescarga = false;
        if (cancelada) await Assert.ThrowsAsync<OperationCanceledException>(() => servicio.DescargarAsync(cuenta, hoja, progreso));
        else await Assert.ThrowsAsync<IOException>(() => servicio.DescargarAsync(cuenta, hoja, progreso));
        Assert.True((await servicio.ConsultarAsync(cuenta, hoja)).Preparada);
        Assert.Equal(anterior, Assert.Single(proveedor.Regiones.Keys));
        Assert.DoesNotContain(anterior, proveedor.Eliminadas);
    }
    [Fact]
    public async Task RecuperaIntentoInterrumpidoTrasTerminarElProcesoSinBorrarElVigente()
    {
        using var db = new RepositorioJornadasSqlite(raiz, claves); using var servicio = new ServicioPreparacionMapa(db, proveedor);
        await servicio.DescargarAsync(cuenta, hoja, progreso);
        var bytes = (await db.LeerMetadatoAsync(cuenta, $"mapa.{hoja.Id:N}"))!;
        var guardado = JsonSerializer.Deserialize<EstadoMapaGuardado>(bytes)!;
        var incompleto = "nexoruta-" + Guid.NewGuid().ToString("N");
        proveedor.Regiones[incompleto] = new(false, 40);
        await db.GuardarMetadatoAsync(cuenta, $"mapa.{hoja.Id:N}", JsonSerializer.SerializeToUtf8Bytes(guardado with { Intento = incompleto }));
        using var reiniciado = new ServicioPreparacionMapa(db, proveedor);
        Assert.True((await reiniciado.ConsultarAsync(cuenta, hoja)).Preparada);
        Assert.Contains(incompleto, proveedor.Eliminadas); Assert.DoesNotContain(guardado.Vigente!.RegionId, proveedor.Eliminadas);
    }
    [Fact]
    public async Task CambioDeZonaInvalidaLaPreparacionYPublicacionNuevaRetiraSoloLaAnterior()
    {
        using var db = new RepositorioJornadasSqlite(raiz, claves); using var servicio = new ServicioPreparacionMapa(db, proveedor);
        await servicio.DescargarAsync(cuenta, hoja, progreso); var anterior = Assert.Single(proveedor.Regiones.Keys);
        var cambiada = new HojaRuta { Id = hoja.Id, Paradas = [new() { Latitud = -35, Longitud = -56.2 }] };
        Assert.False((await servicio.ConsultarAsync(cuenta, cambiada)).Preparada);
        Assert.True((await servicio.DescargarAsync(cuenta, cambiada, progreso)).Preparada);
        Assert.Contains(anterior, proveedor.Eliminadas); Assert.Single(proveedor.Regiones);
        Assert.True((await servicio.ConsultarAsync(cuenta, cambiada)).Preparada);
    }
    [Fact]
    public async Task ProveedorSinConfigurarNoPresentaUnaHojaComoPreparada()
    {
        proveedor.Disponible = false;
        using var db = new RepositorioJornadasSqlite(raiz, claves); using var servicio = new ServicioPreparacionMapa(db, proveedor);
        Assert.False((await servicio.ConsultarAsync(cuenta, hoja)).Preparada);
        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.DescargarAsync(cuenta, hoja, progreso));
        Assert.Empty(proveedor.Regiones);
    }
    [Fact]
    public async Task FalloAlLimpiarIntentoSeReintentaAlReabrirYConservaMapaAnterior()
    {
        using var db = new RepositorioJornadasSqlite(raiz, claves); using var servicio = new ServicioPreparacionMapa(db, proveedor);
        await servicio.DescargarAsync(cuenta, hoja, progreso); var anterior = Assert.Single(proveedor.Regiones.Keys);
        proveedor.CompletarDescarga = false; proveedor.FallarLimpieza = true;
        await Assert.ThrowsAsync<IOException>(() => servicio.DescargarAsync(cuenta, hoja, progreso));
        proveedor.FallarLimpieza = false;
        Assert.True((await servicio.ConsultarAsync(cuenta, hoja)).Preparada);
        Assert.Equal(anterior, Assert.Single(proveedor.Regiones.Keys));
    }
    [Fact]
    public async Task LimpiezaDeRegionRetiradaSeRecuperaSinPerderLaNuevaPreparacion()
    {
        using var db = new RepositorioJornadasSqlite(raiz, claves); using var servicio = new ServicioPreparacionMapa(db, proveedor);
        await servicio.DescargarAsync(cuenta, hoja, progreso); var anterior = Assert.Single(proveedor.Regiones.Keys);
        proveedor.FallarLimpieza = true;
        Assert.True((await servicio.DescargarAsync(cuenta, hoja, progreso)).Preparada);
        Assert.True((await servicio.ConsultarAsync(cuenta, hoja)).Preparada);
        await Assert.ThrowsAsync<IOException>(() => servicio.DescargarAsync(cuenta, hoja, progreso));
        proveedor.FallarLimpieza = false;
        using var reiniciado = new ServicioPreparacionMapa(db, proveedor);
        Assert.True((await reiniciado.ConsultarAsync(cuenta, hoja)).Preparada);
        Assert.Contains(anterior, proveedor.Eliminadas); Assert.Single(proveedor.Regiones);
    }
    [Fact]
    public async Task CancelarTrasLaDescargaAntesDelCommitNoPublicaElIntentoNuevo()
    {
        using var db = new RepositorioJornadasSqlite(raiz, claves); using var servicio = new ServicioPreparacionMapa(db, proveedor);
        await servicio.DescargarAsync(cuenta, hoja, progreso); var anterior = Assert.Single(proveedor.Regiones.Keys);
        using var cancelacion = new CancellationTokenSource(); proveedor.AlDescargar = cancelacion.Cancel;
        await Assert.ThrowsAsync<OperationCanceledException>(() => servicio.DescargarAsync(cuenta, hoja, progreso, cancelacion.Token));
        Assert.True((await servicio.ConsultarAsync(cuenta, hoja)).Preparada);
        Assert.Equal(anterior, Assert.Single(proveedor.Regiones.Keys));
    }
    private sealed class ProveedorFalso : IProveedorMapaOffline
    {
        public string Proveedor => "mapbox";
        public string Estilo => "mapbox://styles/mapbox/streets-v12";
        public bool Disponible { get; set; } = true;
        public bool CompletarDescarga { get; set; } = true;
        public bool CancelarDescarga { get; set; }
        public bool FallarLimpieza { get; set; }
        public Action? AlDescargar { get; set; }
        public Dictionary<string, RecursosMapa> Regiones { get; } = [];
        public List<string> Eliminadas { get; } = [];
        public Task<RecursosMapa> ConsultarAsync(string regionId, CancellationToken ct) => Task.FromResult(Regiones.GetValueOrDefault(regionId, new(false, 0)));
        public Task DescargarAsync(string regionId, ZonaMapa zona, IProgress<ProgresoMapa> progreso, CancellationToken ct)
        {
            Regiones[regionId] = new(CompletarDescarga, 1024);
            if (CancelarDescarga) throw new OperationCanceledException();
            progreso.Report(new(1, 1024)); AlDescargar?.Invoke(); return Task.CompletedTask;
        }
        public Task EliminarAsync(string regionId, CancellationToken ct)
        {
            if (FallarLimpieza) throw new IOException("No se pudo limpiar");
            Eliminadas.Add(regionId); Regiones.Remove(regionId); return Task.CompletedTask;
        }
    }
    private sealed class Claves : IAlmacenClavesProtegidas
    {
        private readonly Dictionary<string, string> guardadas = [];
        public Task<string?> ObtenerAsync(string nombre) => Task.FromResult(guardadas.GetValueOrDefault(nombre));
        public Task GuardarAsync(string nombre, string valor) { guardadas[nombre] = valor; return Task.CompletedTask; }
    }
    public void Dispose() { if (Directory.Exists(raiz)) Directory.Delete(raiz, true); }
}
