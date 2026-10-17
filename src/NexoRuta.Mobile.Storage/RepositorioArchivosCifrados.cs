using System.Security.Cryptography;
using System.Text;
using NexoRuta.Mobile.Core.Jornadas;

namespace NexoRuta.Mobile.Storage;

/// <summary>Una instancia por proceso. IDs inmutables; archivos y temporales siempre cifrados.</summary>
public sealed class RepositorioArchivosCifrados : IDisposable
{
    private static ReadOnlySpan<byte> Cabecera => "NRF1"u8;
    private static ReadOnlySpan<byte> MarcaClave => "NexoRuta.Archivos.v1"u8;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private const string ArchivoClave = "clave.nrf";
    private readonly string raiz;
    private readonly IAlmacenClavesProtegidas claves;
    private readonly SemaphoreSlim escritura = new(1, 1);

    public RepositorioArchivosCifrados(string directorio, IAlmacenClavesProtegidas claves)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directorio);
        this.raiz = Path.GetFullPath(directorio);
        this.claves = claves ?? throw new ArgumentNullException(nameof(claves));
    }

    public async Task GuardarAsync(CuentaMovil cuenta, Guid archivoId, ReadOnlyMemory<byte> contenido,
        CancellationToken ct = default)
    {
        Validar(cuenta, archivoId);
        await escritura.WaitAsync(ct);
        byte[]? clave = null;
        try
        {
            var directorio = DirectorioCuenta(cuenta);
            clave = await ObtenerClaveAsync(cuenta, directorio, crear: true, ct);
            var destino = Path.Combine(directorio, $"{archivoId:N}.nrf");
            if (File.Exists(destino))
            {
                var anterior = Descifrar(await File.ReadAllBytesAsync(destino, ct), clave, cuenta, archivoId);
                try
                {
                    if (!CryptographicOperations.FixedTimeEquals(anterior, contenido.Span))
                        throw new InvalidOperationException("El archivo ya existe con otro contenido. Una evidencia nueva requiere un ID nuevo.");
                    return;
                }
                finally { CryptographicOperations.ZeroMemory(anterior); }
            }

            await EscribirAtomicoAsync(destino, Cifrar(contenido.Span, clave, cuenta, archivoId), ct);
        }
        finally
        {
            if (clave is not null) CryptographicOperations.ZeroMemory(clave);
            escritura.Release();
        }
    }

    public async Task<byte[]> LeerAsync(CuentaMovil cuenta, Guid archivoId, CancellationToken ct = default)
    {
        Validar(cuenta, archivoId);
        await escritura.WaitAsync(ct);
        byte[]? clave = null;
        try
        {
            var directorio = DirectorioCuenta(cuenta);
            var ruta = Path.Combine(directorio, $"{archivoId:N}.nrf");
            if (!File.Exists(ruta)) throw new FileNotFoundException("El archivo local no existe.");
            clave = await ObtenerClaveAsync(cuenta, directorio, crear: false, ct);
            return Descifrar(await File.ReadAllBytesAsync(ruta, ct), clave, cuenta, archivoId);
        }
        finally
        {
            if (clave is not null) CryptographicOperations.ZeroMemory(clave);
            escritura.Release();
        }
    }

    private async Task<byte[]> ObtenerClaveAsync(CuentaMovil cuenta, string directorio, bool crear, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var nombre = "nexoruta.archivos.v1." + IdentidadCuenta(cuenta);
        var valor = await claves.ObtenerAsync(nombre);
        var hayDatos = Directory.Exists(directorio) && Directory.EnumerateFileSystemEntries(directorio).Any();
        byte[] clave;
        if (valor is null)
        {
            if (!crear || hayDatos) throw new ClaveLocalNoDisponibleException();
            clave = RandomNumberGenerator.GetBytes(32);
            try { await claves.GuardarAsync(nombre, Convert.ToBase64String(clave)); }
            catch { CryptographicOperations.ZeroMemory(clave); throw; }
        }
        else
        {
            try { clave = Convert.FromBase64String(valor); }
            catch (FormatException) { throw new ClaveLocalNoDisponibleException(); }
            if (clave.Length != 32)
            {
                CryptographicOperations.ZeroMemory(clave);
                throw new ClaveLocalNoDisponibleException();
            }
        }

        try
        {
            var comprobante = Path.Combine(directorio, ArchivoClave);
            if (File.Exists(comprobante))
            {
                var marca = Descifrar(await File.ReadAllBytesAsync(comprobante, ct), clave, cuenta, Guid.Empty);
                try
                {
                    if (!CryptographicOperations.FixedTimeEquals(marca, MarcaClave))
                        throw new InvalidDataException("El comprobante de la clave local no es válido.");
                }
                finally { CryptographicOperations.ZeroMemory(marca); }
            }
            else
            {
                if (!crear || hayDatos)
                    throw new InvalidDataException("El almacén local está incompleto. Sus archivos se conservan.");
                Directory.CreateDirectory(directorio);
                await EscribirAtomicoAsync(comprobante, Cifrar(MarcaClave, clave, cuenta, Guid.Empty), ct);
            }
            return clave;
        }
        catch { CryptographicOperations.ZeroMemory(clave); throw; }
    }

    private static byte[] Cifrar(ReadOnlySpan<byte> contenido, byte[] clave, CuentaMovil cuenta, Guid archivoId)
    {
        var resultado = new byte[Cabecera.Length + NonceBytes + TagBytes + contenido.Length];
        Cabecera.CopyTo(resultado);
        var nonce = resultado.AsSpan(Cabecera.Length, NonceBytes);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(clave, TagBytes);
        aes.Encrypt(nonce, contenido, resultado.AsSpan(Cabecera.Length + NonceBytes + TagBytes),
            resultado.AsSpan(Cabecera.Length + NonceBytes, TagBytes), Contexto(cuenta, archivoId));
        return resultado;
    }

    private static byte[] Descifrar(byte[] datos, byte[] clave, CuentaMovil cuenta, Guid archivoId)
    {
        var inicio = Cabecera.Length + NonceBytes + TagBytes;
        if (datos.Length < inicio || !datos.AsSpan(0, Cabecera.Length).SequenceEqual(Cabecera))
            throw new InvalidDataException("El archivo local está incompleto o usa una versión no compatible.");
        var resultado = new byte[datos.Length - inicio];
        try
        {
            using var aes = new AesGcm(clave, TagBytes);
            aes.Decrypt(datos.AsSpan(Cabecera.Length, NonceBytes), datos.AsSpan(inicio),
                datos.AsSpan(Cabecera.Length + NonceBytes, TagBytes), resultado, Contexto(cuenta, archivoId));
            return resultado;
        }
        catch (CryptographicException error)
        {
            CryptographicOperations.ZeroMemory(resultado);
            throw new InvalidDataException("No se pudo validar el archivo cifrado. Sus datos se conservan.", error);
        }
    }

    private static async Task EscribirAtomicoAsync(string destino, byte[] contenido, CancellationToken ct)
    {
        var temporal = destino + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporal, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(contenido, ct);
                await stream.FlushAsync(ct);
                stream.Flush(flushToDisk: true);
            }
            ct.ThrowIfCancellationRequested();
            File.Move(temporal, destino, overwrite: false);
        }
        finally { if (File.Exists(temporal)) File.Delete(temporal); }
    }

    private string DirectorioCuenta(CuentaMovil cuenta) => Path.Combine(raiz, IdentidadCuenta(cuenta));
    private static string IdentidadCuenta(CuentaMovil cuenta)
        => Convert.ToHexStringLower(SHA256.HashData(Contexto(cuenta, Guid.Empty)));
    private static byte[] Contexto(CuentaMovil cuenta, Guid archivoId)
        => Encoding.UTF8.GetBytes($"NRF1:{cuenta.UsuarioId:N}:{cuenta.AccesoId:N}:{cuenta.OperadorId:N}:{archivoId:N}");

    private static void Validar(CuentaMovil cuenta, Guid archivoId)
    {
        ArgumentNullException.ThrowIfNull(cuenta);
        if (archivoId == Guid.Empty) throw new ArgumentException("El ID del archivo es obligatorio.", nameof(archivoId));
    }

    public void Dispose() => escritura.Dispose();
}
