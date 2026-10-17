using NexoRuta.Mobile.Core.Jornadas;
namespace NexoRuta.Mobile.Storage;

public sealed partial class RepositorioJornadasSqlite
{
    public async Task<byte[]?> LeerSobreAsync(CuentaMovil cuenta, Guid jornada, Guid operacion, CancellationToken ct = default)
    {
        await acceso.WaitAsync(ct);
        try
        {
            await using var db = await AbrirAsync(cuenta, ct);
            using var command = Comando(db, null, "SELECT Sobre FROM Comandos WHERE JornadaId=$j AND Id=$id", ("$j", jornada.ToString()), ("$id", operacion.ToString()));
            return await command.ExecuteScalarAsync(ct) as byte[];
        }
        finally { acceso.Release(); }
    }
    public async Task GuardarSobreAsync(CuentaMovil cuenta, Guid jornadaId, Guid operacionId, byte[] sobre, CancellationToken ct = default)
    {
        if (sobre.Length is 0 or > 32768) throw new ArgumentException("El comando excede su tamaño admitido.");
        await acceso.WaitAsync(ct);
        try
        {
            await using var db = await AbrirAsync(cuenta, ct); using var tx = db.BeginTransaction();
            var jornada = await LeerAsync(db, tx, cuenta, jornadaId, ct) ?? throw new InvalidOperationException("No existe la jornada.");
            var operacion = jornada.PrepararEnvio().OrderBy(x => x.Secuencia).FirstOrDefault();
            if (operacion?.Id != operacionId) throw new InvalidOperationException("El comando debe ser la primera operación pendiente.");
            using var leer = Comando(db, tx, "SELECT Sobre FROM Comandos WHERE Id=$id", ("$id", operacionId.ToString()));
            if (await leer.ExecuteScalarAsync(ct) is byte[] anterior)
            {
                if (!anterior.AsSpan().SequenceEqual(sobre)) throw new InvalidOperationException("El contenido de un reintento es inmutable.");
                return;
            }
            using var guardar = Comando(db, tx, "INSERT INTO Comandos(Id,JornadaId,Secuencia,Sobre) VALUES($id,$j,$s,$sobre)",
                ("$id", operacionId.ToString()), ("$j", jornadaId.ToString()), ("$s", operacion.Secuencia), ("$sobre", sobre));
            await guardar.ExecuteNonQueryAsync(ct); tx.Commit();
        }
        finally { acceso.Release(); }
    }
    public async Task ConfirmarSobreAsync(CuentaMovil cuenta, Guid jornadaId, Guid operacionId, byte[] respuesta, CancellationToken ct = default)
    {
        if (respuesta.Length is 0 or > 65536) throw new ArgumentException("La confirmación excede su tamaño admitido.");
        await acceso.WaitAsync(ct);
        try
        {
            await using var db = await AbrirAsync(cuenta, ct); using var tx = db.BeginTransaction();
            var jornada = await LeerAsync(db, tx, cuenta, jornadaId, ct) ?? throw new InvalidOperationException("No existe la jornada.");
            using var leer = Comando(db, tx, "SELECT Respuesta FROM Comandos WHERE Id=$id AND JornadaId=$j", ("$id", operacionId.ToString()), ("$j", jornadaId.ToString()));
            await using (var reader = await leer.ExecuteReaderAsync(ct))
            {
                if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("No existe un comando durable para esa confirmación.");
                if (!reader.IsDBNull(0) && !((byte[])reader[0]).AsSpan().SequenceEqual(respuesta)) throw new InvalidOperationException("La confirmación ya tiene otro contenido.");
            }
            var pendientes = jornada.PrepararEnvio().OrderBy(x => x.Secuencia).ToArray();
            if (pendientes.Any(x => x.Id == operacionId) && pendientes[0].Id != operacionId)
                throw new InvalidOperationException("Las confirmaciones deben conservar el orden causal.");
            jornada.ConfirmarPersistencia(operacionId);
            using var confirmar = Comando(db, tx, "UPDATE Comandos SET Respuesta=$r WHERE Id=$id; UPDATE Jornadas SET Estado=$estado WHERE Id=$j;",
                ("$r", respuesta), ("$id", operacionId.ToString()), ("$estado", SerializadorJornadaLocal.Serializar(jornada)), ("$j", jornadaId.ToString()));
            await confirmar.ExecuteNonQueryAsync(ct); tx.Commit();
        }
        finally { acceso.Release(); }
    }
    public async Task<IReadOnlyList<byte[]>> LeerConfirmacionesAsync(CuentaMovil cuenta, Guid jornadaId, CancellationToken ct = default)
    {
        await acceso.WaitAsync(ct);
        try
        {
            await using var db = await AbrirAsync(cuenta, ct);
            using var command = Comando(db, null, "SELECT Respuesta FROM Comandos WHERE JornadaId=$j AND Respuesta IS NOT NULL ORDER BY Secuencia", ("$j", jornadaId.ToString()));
            await using var reader = await command.ExecuteReaderAsync(ct); var salida = new List<byte[]>();
            while (await reader.ReadAsync(ct)) salida.Add((byte[])reader[0]); return salida;
        }
        finally { acceso.Release(); }
    }
    public async Task<byte[]?> LeerConfirmacionAsync(CuentaMovil cuenta, Guid jornada, Guid operacion, CancellationToken ct = default)
    {
        await acceso.WaitAsync(ct);
        try
        {
            await using var db = await AbrirAsync(cuenta, ct);
            using var command = Comando(db, null, "SELECT Respuesta FROM Comandos WHERE JornadaId=$j AND Id=$id", ("$j", jornada.ToString()), ("$id", operacion.ToString()));
            return await command.ExecuteScalarAsync(ct) as byte[];
        }
        finally { acceso.Release(); }
    }
    public async Task<IReadOnlyList<JornadaLocal>> LeerJornadasAsync(CuentaMovil cuenta, CancellationToken ct = default, bool soloAbiertas = false)
    {
        await acceso.WaitAsync(ct);
        try
        {
            await using var db = await AbrirAsync(cuenta, ct); using var command = Comando(db, null, soloAbiertas ? "SELECT Estado FROM Jornadas WHERE Cerrada=0" : "SELECT Estado FROM Jornadas");
            await using var reader = await command.ExecuteReaderAsync(ct); var salida = new List<JornadaLocal>();
            while (await reader.ReadAsync(ct)) salida.Add(SerializadorJornadaLocal.Deserializar((byte[])reader[0], cuenta)); return salida;
        }
        finally { acceso.Release(); }
    }
}
