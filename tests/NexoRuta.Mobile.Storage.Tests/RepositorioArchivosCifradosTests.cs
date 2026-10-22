using System.Security.Cryptography;
using NexoRuta.Mobile.Core.Jornadas;

namespace NexoRuta.Mobile.Storage.Tests;

public sealed class RepositorioArchivosCifradosTests : IDisposable
{
    private readonly string raiz = Path.Combine(Path.GetTempPath(), "nexoruta-storage-tests", Guid.NewGuid().ToString("N"));
    private readonly ClavesMemoria claves = new();
    private static readonly CuentaMovil Cuenta = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
    private static readonly byte[] Contenido = "Prueba sensible: receptor, documento y fotografía de entrega"u8.ToArray();

    [Fact]
    public async Task ArchivoSobreviveOtraInstanciaSinPersistirContenidoNiIdentidadEnClaro()
    {
        var id = Guid.NewGuid();
        using (var repo = Nuevo()) await repo.GuardarAsync(Cuenta, id, Contenido);
        using (var repo = Nuevo()) Assert.Equal(Contenido, await repo.LeerAsync(Cuenta, id));
        foreach (var ruta in Directory.GetFiles(raiz, "*", SearchOption.AllDirectories))
        {
            var datos = await File.ReadAllBytesAsync(ruta);
            Assert.False(datos.AsSpan().IndexOf(Contenido) >= 0);
            Assert.False(datos.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes(Cuenta.UsuarioId.ToString())) >= 0);
        }
        Assert.Empty(Directory.GetFiles(raiz, "*.tmp", SearchOption.AllDirectories));
        Assert.Equal(1, claves.Escrituras);
    }

    [Fact]
    public async Task ReintentarMismoIdEsIdempotenteYOtroContenidoNoSobrescribe()
    {
        using var repo = Nuevo();
        var id = Guid.NewGuid();
        await repo.GuardarAsync(Cuenta, id, Contenido);
        var antes = await File.ReadAllBytesAsync(Ruta(id));
        await repo.GuardarAsync(Cuenta, id, Contenido);
        Assert.Equal(antes, await File.ReadAllBytesAsync(Ruta(id)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.GuardarAsync(Cuenta, id, new byte[] { 9 }));
        Assert.Equal(Contenido, await repo.LeerAsync(Cuenta, id));
    }

    [Fact]
    public async Task MismoContenidoUsaNonceDistintoPorArchivo()
    {
        using var repo = Nuevo();
        var primero = Guid.NewGuid();
        var segundo = Guid.NewGuid();
        await repo.GuardarAsync(Cuenta, primero, Contenido);
        await repo.GuardarAsync(Cuenta, segundo, Contenido);
        Assert.NotEqual(await File.ReadAllBytesAsync(Ruta(primero)), await File.ReadAllBytesAsync(Ruta(segundo)));
    }

    [Theory]
    [InlineData(0)] [InlineData(4)] [InlineData(16)] [InlineData(32)]
    public async Task AlterarCabeceraNonceTagOContenidoSeRechazaSinBorrar(int indice)
    {
        using var repo = Nuevo();
        var id = Guid.NewGuid();
        await repo.GuardarAsync(Cuenta, id, Contenido);
        var ruta = Ruta(id);
        var datos = await File.ReadAllBytesAsync(ruta);
        datos[indice] ^= 0x01;
        await File.WriteAllBytesAsync(ruta, datos);
        await Assert.ThrowsAsync<InvalidDataException>(() => repo.LeerAsync(Cuenta, id));
        Assert.Equal(datos, await File.ReadAllBytesAsync(ruta));
    }

    [Theory]
    [InlineData(0)] [InlineData(3)] [InlineData(20)] [InlineData(31)]
    public async Task ArchivoTruncadoSeConservaYNoSeEntregaComoEvidencia(int longitud)
    {
        using var repo = Nuevo();
        var id = Guid.NewGuid();
        await repo.GuardarAsync(Cuenta, id, Contenido);
        var datos = (await File.ReadAllBytesAsync(Ruta(id)))[..longitud];
        await File.WriteAllBytesAsync(Ruta(id), datos);
        await Assert.ThrowsAsync<InvalidDataException>(() => repo.LeerAsync(Cuenta, id));
        Assert.Equal(datos, await File.ReadAllBytesAsync(Ruta(id)));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task CambiarUsuarioAccesoUOperadorNoDaAccesoAlArchivo(int campo)
    {
        using var repo = Nuevo();
        var id = Guid.NewGuid();
        await repo.GuardarAsync(Cuenta, id, Contenido);
        var origen = Ruta(id);
        var otra = new CuentaMovil(campo == 0 ? Guid.NewGuid() : Cuenta.UsuarioId,
            campo == 1 ? Guid.NewGuid() : Cuenta.AccesoId, campo == 2 ? Guid.NewGuid() : Cuenta.OperadorId);
        await Assert.ThrowsAsync<FileNotFoundException>(() => repo.LeerAsync(otra, id));
        await repo.GuardarAsync(otra, id, new byte[] { 3 });
        var rutas = Directory.GetFiles(raiz, $"{id:N}.nrf", SearchOption.AllDirectories);
        var destino = rutas.Single(x => x != origen);
        await File.WriteAllBytesAsync(destino, await File.ReadAllBytesAsync(origen));
        await Assert.ThrowsAsync<InvalidDataException>(() => repo.LeerAsync(otra, id));
        Assert.Equal(Contenido, await repo.LeerAsync(Cuenta, id));
    }

    [Fact]
    public async Task CopiarArchivoConOtroIdNoPermiteDescifrarlo()
    {
        using var repo = Nuevo();
        var id = Guid.NewGuid();
        var otroId = Guid.NewGuid();
        await repo.GuardarAsync(Cuenta, id, Contenido);
        await repo.GuardarAsync(Cuenta, otroId, new byte[] { 8 });
        await File.WriteAllBytesAsync(Ruta(otroId), await File.ReadAllBytesAsync(Ruta(id)));
        await Assert.ThrowsAsync<InvalidDataException>(() => repo.LeerAsync(Cuenta, otroId));
    }

    [Theory]
    [InlineData(null)] [InlineData("no-base64")] [InlineData("AA==")]
    public async Task ClavePerdidaOMalformadaBloqueaLecturaYEscrituraSinGenerarOtra(string? clave)
    {
        using var repo = Nuevo();
        var id = Guid.NewGuid();
        await repo.GuardarAsync(Cuenta, id, Contenido);
        var antes = await File.ReadAllBytesAsync(Ruta(id));
        claves.Valor = clave;
        await Assert.ThrowsAsync<ClaveLocalNoDisponibleException>(() => repo.LeerAsync(Cuenta, id));
        await Assert.ThrowsAsync<ClaveLocalNoDisponibleException>(() => repo.GuardarAsync(Cuenta, Guid.NewGuid(), Contenido));
        Assert.Equal(1, claves.Escrituras);
        Assert.Equal(antes, await File.ReadAllBytesAsync(Ruta(id)));
        Assert.Equal(2, Directory.GetFiles(raiz, "*", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public async Task ClaveIncorrectaBloqueaTambienArchivosNuevos()
    {
        using var repo = Nuevo();
        var id = Guid.NewGuid();
        await repo.GuardarAsync(Cuenta, id, Contenido);
        claves.Valor = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        await Assert.ThrowsAsync<InvalidDataException>(() => repo.LeerAsync(Cuenta, id));
        await Assert.ThrowsAsync<InvalidDataException>(() => repo.GuardarAsync(Cuenta, Guid.NewGuid(), Contenido));
        Assert.Equal(2, Directory.GetFiles(raiz, "*", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public async Task FalloDelAlmacenSeguroNoEscribeArchivosNiOcultaElError()
    {
        using var repo = Nuevo();
        claves.FallarEscritura = true;
        await Assert.ThrowsAsync<IOException>(() => repo.GuardarAsync(Cuenta, Guid.NewGuid(), Contenido));
        Assert.False(Directory.Exists(raiz));
    }

    [Fact]
    public async Task ComprobanteAusenteNoRegeneraElAlmacenSobreEvidenciasExistentes()
    {
        using var repo = Nuevo();
        var id = Guid.NewGuid();
        await repo.GuardarAsync(Cuenta, id, Contenido);
        File.Delete(Directory.GetFiles(raiz, "clave.nrf", SearchOption.AllDirectories).Single());
        await Assert.ThrowsAsync<InvalidDataException>(() => repo.GuardarAsync(Cuenta, Guid.NewGuid(), Contenido));
        Assert.Single(Directory.GetFiles(raiz, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task LlamadasSimultaneasCompartenUnaClaveYConservanCadaArchivo()
    {
        using var repo = Nuevo();
        var ids = Enumerable.Range(0, 8).Select(_ => Guid.NewGuid()).ToArray();
        await Task.WhenAll(ids.Select(id => repo.GuardarAsync(Cuenta, id, Contenido)));
        foreach (var id in ids) Assert.Equal(Contenido, await repo.LeerAsync(Cuenta, id));
        Assert.Equal(1, claves.Escrituras);
    }

    [Fact]
    public async Task CancelarAntesDeGuardarNoProduceArchivosNiClaves()
    {
        using var repo = Nuevo();
        using var ct = new CancellationTokenSource();
        ct.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repo.GuardarAsync(Cuenta, Guid.NewGuid(), Contenido, ct.Token));
        Assert.False(Directory.Exists(raiz));
        Assert.Equal(0, claves.Escrituras);
    }

    private RepositorioArchivosCifrados Nuevo() => new(raiz, claves);
    private string Ruta(Guid id) => Directory.GetFiles(raiz, $"{id:N}.nrf", SearchOption.AllDirectories).Single();
    public void Dispose() { if (Directory.Exists(raiz)) Directory.Delete(raiz, recursive: true); }

    private sealed class ClavesMemoria : IAlmacenClavesProtegidas
    {
        public readonly Dictionary<string, string> Nombres = new();
        public int Escrituras { get; private set; }
        public bool FallarEscritura { get; set; }
        public string? Valor { set { foreach (var k in Nombres.Keys.ToArray()) { if (value is null) Nombres.Remove(k); else Nombres[k] = value; } } }
        public Task<string?> ObtenerAsync(string nombre) => Task.FromResult(Nombres.GetValueOrDefault(nombre));
        public Task GuardarAsync(string nombre, string valor)
        {
            if (FallarEscritura) throw new IOException("Almacén seguro no disponible.");
            Nombres[nombre] = valor;
            Escrituras++;
            return Task.CompletedTask;
        }
    }
}
