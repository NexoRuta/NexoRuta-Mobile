using Microsoft.Data.Sqlite;
using NexoRuta.Mobile.Core.Jornadas;
namespace NexoRuta.Mobile.Storage;

public sealed partial class RepositorioJornadasSqlite
{
    // El mismo semáforo serializa la comprobación y todos los accesos, incluidos GPS y capturas.
    public async Task<bool> SuspenderSiSinPendientesAsync(CuentaMovil cuenta, CancellationToken ct = default)
    {
        await acceso.WaitAsync(ct);
        try
        {
            await using var db = await AbrirAsync(cuenta, ct);
            if (await TienePendientesAsync(db, cuenta, ct)) return false;
            suspendidas.Add(cuenta); return true;
        }
        finally { acceso.Release(); }
    }
    public async Task ReanudarCuentaAsync(CuentaMovil cuenta, CancellationToken ct = default)
    {
        await acceso.WaitAsync(ct);
        try { suspendidas.Remove(cuenta); }
        finally { acceso.Release(); }
    }
    private static async Task<bool> TienePendientesAsync(SqliteConnection db, CuentaMovil cuenta, CancellationToken ct)
    {
        using (var capturas = Comando(db, null, "SELECT count(*) FROM Capturas WHERE OperacionId IS NULL AND Descartada=0"))
            if (Convert.ToInt64(await capturas.ExecuteScalarAsync(ct)) > 0) return true;
        using var command = Comando(db, null, "SELECT Estado FROM Jornadas");
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            if (SerializadorJornadaLocal.Deserializar((byte[])reader[0], cuenta).TienePendientes) return true;
        return false;
    }
}
