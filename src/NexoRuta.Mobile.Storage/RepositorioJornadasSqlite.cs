using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NexoRuta.Mobile.Core.Jornadas;

namespace NexoRuta.Mobile.Storage;

/// <summary>Una instancia por proceso. Todas las páginas, journals y datos temporales usan SQLite3MC.</summary>
public sealed partial class RepositorioJornadasSqlite(string raiz, IAlmacenClavesProtegidas claves) : IDisposable
{
    private static readonly Lazy<bool> Motor = new(() => { SQLitePCL.Batteries_V2.Init(); return true; });
    private readonly string raiz = Path.GetFullPath(raiz);
    private readonly SemaphoreSlim acceso = new(1, 1);
    private readonly HashSet<CuentaMovil> suspendidas = [];

    public async Task CrearAsync(JornadaLocal jornada, CancellationToken ct = default)
    {
        await acceso.WaitAsync(ct);
        try
        {
            await using var db = await AbrirAsync(jornada.Cuenta, ct);
            using var tx = db.BeginTransaction();
            var datos = SerializadorJornadaLocal.Serializar(jornada);
            using var anterior = Comando(db, tx, "SELECT Estado FROM Jornadas WHERE Id = $id", ("$id", jornada.Id.ToString()));
            var existente = await anterior.ExecuteScalarAsync(ct) as byte[];
            if (existente is not null)
            {
                if (!existente.AsSpan().SequenceEqual(datos))
                    throw new InvalidOperationException("La jornada ya existe con otro estado. Usá una modificación transaccional.");
                return;
            }
            using var insert = Comando(db, tx, "INSERT INTO Jornadas(Id, Estado, Cerrada) VALUES($id, $estado, $cerrada)",
                ("$id", jornada.Id.ToString()), ("$estado", datos), ("$cerrada", jornada.EstaCerrada));
            await insert.ExecuteNonQueryAsync(ct);
            tx.Commit();
        }
        finally { acceso.Release(); }
    }

    public async Task<JornadaLocal?> LeerAsync(CuentaMovil cuenta, Guid jornadaId, CancellationToken ct = default)
    {
        await acceso.WaitAsync(ct);
        try
        {
            await using var db = await AbrirAsync(cuenta, ct);
            return await LeerAsync(db, null, cuenta, jornadaId, ct);
        }
        finally { acceso.Release(); }
    }

    public async Task<JornadaLocal> ModificarAsync(CuentaMovil cuenta, Guid jornadaId,
        Action<JornadaLocal> modificar, CancellationToken ct = default)
    {
        await acceso.WaitAsync(ct);
        try
        {
            await using var db = await AbrirAsync(cuenta, ct);
            using var tx = db.BeginTransaction();
            var jornada = await LeerAsync(db, tx, cuenta, jornadaId, ct)
                ?? throw new InvalidOperationException("La jornada no existe en este dispositivo.");
            modificar(jornada);
            using var update = Comando(db, tx, "UPDATE Jornadas SET Estado = $estado, Cerrada=$cerrada WHERE Id = $id",
                ("$id", jornadaId.ToString()), ("$estado", SerializadorJornadaLocal.Serializar(jornada)), ("$cerrada", jornada.EstaCerrada));
            await update.ExecuteNonQueryAsync(ct);
            tx.Commit();
            return jornada;
        }
        finally { acceso.Release(); }
    }

    public async Task<bool> TienePendientesAsync(CuentaMovil cuenta, CancellationToken ct = default)
    {
        await acceso.WaitAsync(ct);
        try
        {
            await using var db = await AbrirAsync(cuenta, ct);
            return await TienePendientesAsync(db, cuenta, ct);
        }
        finally { acceso.Release(); }
    }

    public async Task GuardarHojaAsync(CuentaMovil cuenta, Guid rutaId, ReadOnlyMemory<byte> contenido, CancellationToken ct = default)
    {
        if (rutaId == Guid.Empty) throw new ArgumentException("La ruta es obligatoria.");
        using var json = JsonDocument.Parse(contenido);
        if (json.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("La hoja debe ser un objeto JSON.");
        await acceso.WaitAsync(ct);
        try
        {
            await using var db = await AbrirAsync(cuenta, ct);
            using var tx = db.BeginTransaction();
            using (var consultar = Comando(db, tx, "SELECT Estado FROM Jornadas"))
            await using (var reader = await consultar.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    var jornada = SerializadorJornadaLocal.Deserializar((byte[])reader[0], cuenta);
                    if (jornada.RutaId == rutaId && (jornada.TienePendientes || !jornada.EstaCerrada))
                        throw new InvalidOperationException("La hoja tiene una jornada en curso o pendientes. Reconciliá los cambios antes de reemplazarla.");
                }
            }
            using var guardar = Comando(db, tx, "INSERT INTO Hojas(Id, Datos) VALUES($id,$datos) ON CONFLICT(Id) DO UPDATE SET Datos=excluded.Datos",
                ("$id", rutaId.ToString()), ("$datos", contenido.ToArray()));
            await guardar.ExecuteNonQueryAsync(ct);
            tx.Commit();
        }
        finally { acceso.Release(); }
    }

    public async Task<byte[]?> LeerHojaAsync(CuentaMovil cuenta, Guid rutaId, CancellationToken ct = default)
    {
        await acceso.WaitAsync(ct);
        try
        {
            await using var db = await AbrirAsync(cuenta, ct);
            using var command = Comando(db, null, "SELECT Datos FROM Hojas WHERE Id=$id", ("$id", rutaId.ToString()));
            return await command.ExecuteScalarAsync(ct) as byte[];
        }
        finally { acceso.Release(); }
    }

    public async Task<IReadOnlyList<byte[]>> LeerHojasAsync(CuentaMovil cuenta, CancellationToken ct = default)
    {
        await acceso.WaitAsync(ct);
        try
        {
            await using var db = await AbrirAsync(cuenta, ct);
            using var command = Comando(db, null, "SELECT Datos FROM Hojas ORDER BY Id");
            await using var reader = await command.ExecuteReaderAsync(ct);
            var datos = new List<byte[]>();
            while (await reader.ReadAsync(ct)) datos.Add((byte[])reader[0]);
            return datos;
        }
        finally { acceso.Release(); }
    }

    private static async Task<JornadaLocal?> LeerAsync(SqliteConnection db, SqliteTransaction? tx,
        CuentaMovil cuenta, Guid id, CancellationToken ct)
    {
        using var leer = Comando(db, tx, "SELECT Estado FROM Jornadas WHERE Id=$id", ("$id", id.ToString()));
        var datos = await leer.ExecuteScalarAsync(ct) as byte[];
        return datos is null ? null : SerializadorJornadaLocal.Deserializar(datos, cuenta);
    }

    private async Task<SqliteConnection> AbrirAsync(CuentaMovil cuenta, CancellationToken ct)
    {
        if (suspendidas.Contains(cuenta)) throw new InvalidOperationException("Esta cuenta está cerrando sesión. Volvé a ingresar para acceder a su trabajo guardado.");
        _ = Motor.Value;
        var identidad = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{cuenta.UsuarioId:N}:{cuenta.AccesoId:N}:{cuenta.OperadorId:N}")));
        var directorio = Path.Combine(raiz, identidad);
        var archivo = Path.Combine(directorio, "operacion.sqlite");
        var nombreClave = "nexoruta.sqlite.v1." + identidad;
        var clave = await claves.ObtenerAsync(nombreClave);
        var existenDatos = Directory.Exists(directorio) && Directory.EnumerateFileSystemEntries(directorio).Any();
        if (clave is null)
        {
            if (existenDatos) throw new ClaveLocalNoDisponibleException();
            var bytes = RandomNumberGenerator.GetBytes(32);
            try { clave = Convert.ToBase64String(bytes); await claves.GuardarAsync(nombreClave, clave); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        try
        {
            var bytes = Convert.FromBase64String(clave);
            try { if (bytes.Length != 32) throw new ClaveLocalNoDisponibleException(); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        catch (FormatException) { throw new ClaveLocalNoDisponibleException(); }
        Directory.CreateDirectory(directorio);
        var db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = archivo, Password = clave, Pooling = false, DefaultTimeout = 5
        }.ToString());
        try
        {
            await db.OpenAsync(ct);
            using var versionMotor = Comando(db, null, "SELECT sqlite3mc_version()");
            if (string.IsNullOrEmpty(await versionMotor.ExecuteScalarAsync(ct) as string))
                throw new InvalidOperationException("El motor SQLite no tiene cifrado.");
            using (var pragmas = Comando(db, null, "PRAGMA temp_store=MEMORY; PRAGMA foreign_keys=ON; PRAGMA secure_delete=ON;"))
                await pragmas.ExecuteNonQueryAsync(ct);
            using var tx = db.BeginTransaction();
            using var version = Comando(db, tx, "PRAGMA user_version");
            var numero = Convert.ToInt32(await version.ExecuteScalarAsync(ct));
            if (numero is not (0 or 1)) throw new InvalidOperationException("La base local tiene una versión no soportada. No se modificó.");
            if (numero == 0)
            {
                using var crear = Comando(db, tx, """
                    CREATE TABLE Cuenta(Identidad TEXT PRIMARY KEY NOT NULL);
                    CREATE TABLE Jornadas(Id TEXT PRIMARY KEY NOT NULL, Estado BLOB NOT NULL, Cerrada INTEGER NOT NULL);
                    CREATE TABLE Hojas(Id TEXT PRIMARY KEY NOT NULL, Datos BLOB NOT NULL);
                    CREATE TABLE Comandos(Id TEXT PRIMARY KEY NOT NULL, JornadaId TEXT NOT NULL REFERENCES Jornadas(Id),
                        Secuencia INTEGER NOT NULL, Sobre BLOB NOT NULL, Respuesta BLOB NULL, UNIQUE(JornadaId, Secuencia));
                    CREATE TABLE Capturas(Id TEXT PRIMARY KEY NOT NULL, JornadaId TEXT NOT NULL REFERENCES Jornadas(Id),
                        EnvioId TEXT NOT NULL, Tipo TEXT NOT NULL, Lista INTEGER NOT NULL DEFAULT 0, OperacionId TEXT NULL, Descartada INTEGER NOT NULL DEFAULT 0);
                    CREATE TABLE Metadatos(Clave TEXT PRIMARY KEY NOT NULL, Datos BLOB NOT NULL);
                    CREATE TABLE HojasRemotas(Id TEXT PRIMARY KEY NOT NULL, Version INTEGER NOT NULL, Datos BLOB NOT NULL);
                    INSERT INTO Cuenta(Identidad) VALUES($cuenta);
                    PRAGMA user_version=1;
                    """, ("$cuenta", identidad));
                await crear.ExecuteNonQueryAsync(ct);
            }
            using var propietario = Comando(db, tx, "SELECT Identidad FROM Cuenta");
            if ((string?)await propietario.ExecuteScalarAsync(ct) != identidad)
                throw new ClaveLocalNoDisponibleException();
            tx.Commit();
            return db;
        }
        catch
        {
            await db.DisposeAsync();
            throw;
        }
    }

    private static SqliteCommand Comando(SqliteConnection db, SqliteTransaction? tx, string sql,
        params (string Nombre, object Valor)[] parametros)
    {
        var comando = db.CreateCommand();
        comando.Transaction = tx;
        comando.CommandText = sql;
        foreach (var (nombre, valor) in parametros) comando.Parameters.AddWithValue(nombre, valor);
        return comando;
    }

    public void Dispose() => acceso.Dispose();
}
