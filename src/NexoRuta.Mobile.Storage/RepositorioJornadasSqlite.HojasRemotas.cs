using NexoRuta.Mobile.Core.Jornadas;
namespace NexoRuta.Mobile.Storage;

public sealed partial class RepositorioJornadasSqlite
{
    // Vista de consulta separada del baseline con el que se congelan los comandos pendientes.
    public async Task GuardarVistaRemotaAsync(CuentaMovil cuenta, Guid rutaId, long version, byte[] contenido, CancellationToken ct = default)
    {
        if (rutaId == Guid.Empty || version < 0 || contenido.Length is 0 or > 4194304) throw new ArgumentException("La vista remota no es válida.");
        await acceso.WaitAsync(ct);
        try
        {
            await using var db = await AbrirAsync(cuenta, ct);
            using var comando = Comando(db, null, "INSERT INTO HojasRemotas(Id,Version,Datos) VALUES($id,$v,$d) ON CONFLICT(Id) DO UPDATE SET Version=excluded.Version,Datos=excluded.Datos WHERE excluded.Version >= HojasRemotas.Version",
                ("$id", rutaId.ToString()), ("$v", version), ("$d", contenido));
            await comando.ExecuteNonQueryAsync(ct);
        }
        finally { acceso.Release(); }
    }
    public async Task<byte[]?> LeerVistaRemotaAsync(CuentaMovil cuenta, Guid rutaId, CancellationToken ct = default)
    {
        await acceso.WaitAsync(ct);
        try
        {
            await using var db = await AbrirAsync(cuenta, ct);
            using var comando = Comando(db, null, "SELECT Datos FROM HojasRemotas WHERE Id=$id", ("$id", rutaId.ToString()));
            return await comando.ExecuteScalarAsync(ct) as byte[];
        }
        finally { acceso.Release(); }
    }
}
