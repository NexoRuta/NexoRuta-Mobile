#if VALIDAR_ALMACENAMIENTO && ANDROID
using System.Security.Cryptography;
using System.Text.Json;
using NexoRuta.Mobile.Almacenamiento;
using NexoRuta.Mobile.Core.Jornadas;
using NexoRuta.Mobile.Storage;

namespace NexoRuta.Mobile.Diagnostico;

// Solo se incluye con -p:ValidarAlmacenamiento=true. Sin datos de repartidores ni acceso a su almacén.
internal static class PruebaAlmacenamiento
{
    private static readonly CuentaMovil Cuenta = new(
        Guid.Parse("00000000-0000-0000-0000-000000000011"),
        Guid.Parse("00000000-0000-0000-0000-000000000012"),
        Guid.Parse("00000000-0000-0000-0000-000000000013"));
    private static readonly Guid ImagenId = Guid.Parse("00000000-0000-0000-0000-000000000014");
    private const string Tag = "NexoRuta.StorageProof";

    public static async Task EjecutarAsync(string fase)
    {
        try
        {
            var raiz = DirectorioEvidencias.Crear("prueba-almacenamiento");
            var claves = new ClavesPrueba(new AlmacenClavesProtegidas());
            using var repo = new RepositorioArchivosCifrados(raiz, claves);
            using var jornadas = new RepositorioJornadasSqlite(DirectorioEvidencias.Crear("prueba-sqlite"), claves);
            var jornadaId = Guid.Parse("00000000-0000-0000-0000-000000000015");
            await using var asset = await FileSystem.OpenAppPackageFileAsync("prueba-almacenamiento.png");
            using var buffer = new MemoryStream();
            await asset.CopyToAsync(buffer);
            var imagen = buffer.ToArray();
            if (fase == "guardar")
            {
                if (await jornadas.LeerAsync(Cuenta, jornadaId) is null)
                {
                    var jornada = JornadaLocal.Iniciar(jornadaId, Guid.Parse("00000000-0000-0000-0000-000000000016"), Cuenta,
                        new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero));
                    using var datos = JsonDocument.Parse("{\"nombre\":\"Prueba SQLite cifrada\"}");
                    jornada.RegistrarEntregaFisica(ImagenId, datos.RootElement, DateTimeOffset.UtcNow);
                    await jornadas.CrearAsync(jornada);
                }
                await repo.GuardarAsync(Cuenta, ImagenId, imagen);
                var ruta = Directory.GetFiles(raiz, $"{ImagenId:N}.nrf", SearchOption.AllDirectories).Single();
                var cifrado = await File.ReadAllBytesAsync(ruta);
                if (cifrado.AsSpan().IndexOf(imagen) >= 0) throw new InvalidDataException();
                Android.Util.Log.Info(Tag, "GUARDADO_OK: imagen cifrada; clave en SecureStorage.");
                Android.Util.Log.Info(Tag, "SQLITE_GUARDADO_OK: jornada pendiente persistida con SQLite3MC.");
            }
            else if (fase == "leer")
            {
                var jornada = await jornadas.LeerAsync(Cuenta, jornadaId);
                if (jornada is null || jornada.Operaciones.Count != 2 || !jornada.TienePendientes) throw new InvalidDataException();
                using (var sinClaveDb = new RepositorioJornadasSqlite(DirectorioEvidencias.Crear("prueba-sqlite"), new ClavesAusentes()))
                {
                    try { await sinClaveDb.LeerAsync(Cuenta, jornadaId); throw new InvalidOperationException("Debe rechazar clave ausente."); }
                    catch (ClaveLocalNoDisponibleException) { }
                }
                var original = await repo.LeerAsync(Cuenta, ImagenId);
                if (!CryptographicOperations.FixedTimeEquals(imagen, original)) throw new InvalidDataException();
                using var sinClave = new RepositorioArchivosCifrados(raiz, new ClavesAusentes());
                try
                {
                    await sinClave.GuardarAsync(Cuenta, Guid.NewGuid(), imagen);
                    throw new InvalidOperationException("Se regeneró una clave con datos pendientes.");
                }
                catch (ClaveLocalNoDisponibleException) { }
                var conservada = await repo.LeerAsync(Cuenta, ImagenId);
                if (!CryptographicOperations.FixedTimeEquals(imagen, conservada))
                    throw new InvalidDataException();
                if (Directory.GetFiles(raiz, "*.tmp", SearchOption.AllDirectories).Length != 0)
                    throw new InvalidDataException();
                Android.Util.Log.Info(Tag, "REAPERTURA_OK: imagen recuperada; clave ausente bloqueada; archivos conservados; sin temporales.");
                Android.Util.Log.Info(Tag, "SQLITE_REAPERTURA_OK: dos operaciones conservadas; clave ausente bloqueada.");
            }
            else throw new ArgumentException("Fase de prueba desconocida.");
        }
        catch (Exception error)
        {
            // El log informa únicamente el tipo de fallo; nunca contenido, claves o rutas.
            Android.Util.Log.Error(Tag, "PRUEBA_FALLO: " + error.GetType().Name);
        }
    }

    private sealed class ClavesPrueba(IAlmacenClavesProtegidas almacenamiento) : IAlmacenClavesProtegidas
    {
        public Task<string?> ObtenerAsync(string nombre) => almacenamiento.ObtenerAsync("prueba." + nombre);
        public Task GuardarAsync(string nombre, string valor) => almacenamiento.GuardarAsync("prueba." + nombre, valor);
    }
    private sealed class ClavesAusentes : IAlmacenClavesProtegidas
    {
        public Task<string?> ObtenerAsync(string nombre) => Task.FromResult<string?>(null);
        public Task GuardarAsync(string nombre, string valor) => throw new InvalidOperationException("No generar una clave nueva.");
    }
}
#endif
