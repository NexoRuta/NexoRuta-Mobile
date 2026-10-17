using NexoRuta.Mobile.Core.Jornadas;
namespace NexoRuta.Mobile.Storage;
public sealed partial class RepositorioJornadasSqlite
{
    public async Task<CapturaLocal> IniciarCapturaAsync(CuentaMovil cuenta, Guid jornada, Guid envio, string tipo, CancellationToken ct = default)
    {
        if (envio == Guid.Empty || tipo is not ("Foto" or "Firma")) throw new ArgumentException("La captura requiere envío y tipo.");
        await acceso.WaitAsync(ct);
        try
        {
            await using var db = await AbrirAsync(cuenta, ct); using var tx = db.BeginTransaction();
            var local = await LeerAsync(db, tx, cuenta, jornada, ct) ?? throw new InvalidOperationException("No existe la jornada.");
            if (local.EstaCerrada) throw new InvalidOperationException("La jornada está cerrada.");
            var captura = new CapturaLocal(Guid.CreateVersion7(), jornada, envio, tipo, false, null);
            using var command = Comando(db, tx, "INSERT INTO Capturas(Id,JornadaId,EnvioId,Tipo) VALUES($id,$j,$e,$t)",
                ("$id", captura.Id.ToString()), ("$j", jornada.ToString()), ("$e", envio.ToString()), ("$t", tipo));
            await command.ExecuteNonQueryAsync(ct); tx.Commit(); return captura;
        }
        finally { acceso.Release(); }
    }
    public async Task MarcarCapturaListaAsync(CuentaMovil cuenta, Guid id, CancellationToken ct = default)
    {
        await acceso.WaitAsync(ct);
        try
        {
            await using var db = await AbrirAsync(cuenta, ct);
            using var command = Comando(db, null, "UPDATE Capturas SET Lista=1 WHERE Id=$id AND OperacionId IS NULL AND Descartada=0", ("$id", id.ToString()));
            if (await command.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException("No existe una captura pendiente con ese identificador.");
        }
        finally { acceso.Release(); }
    }
    public async Task<IReadOnlyList<CapturaLocal>> LeerCapturasAsync(CuentaMovil cuenta, Guid jornada, CancellationToken ct = default)
    {
        await acceso.WaitAsync(ct);
        try
        {
            await using var db = await AbrirAsync(cuenta, ct);
            using var command = Comando(db, null, "SELECT Id,EnvioId,Tipo,Lista,OperacionId FROM Capturas WHERE JornadaId=$j AND Descartada=0", ("$j", jornada.ToString()));
            await using var reader = await command.ExecuteReaderAsync(ct); var salida = new List<CapturaLocal>();
            while (await reader.ReadAsync(ct)) salida.Add(new(Guid.Parse(reader.GetString(0)), jornada, Guid.Parse(reader.GetString(1)),
                reader.GetString(2), reader.GetBoolean(3), reader.IsDBNull(4) ? null : Guid.Parse(reader.GetString(4))));
            return salida;
        }
        finally { acceso.Release(); }
    }
    public async Task<JornadaLocal> RegistrarConCapturasAsync(CuentaMovil cuenta, Guid jornadaId, Guid[] capturas,
        Func<JornadaLocal, OperacionLocal> registrar, CancellationToken ct = default)
    {
        if (capturas.Distinct().Count() != capturas.Length) throw new ArgumentException("Las capturas no pueden repetirse.");
        await acceso.WaitAsync(ct);
        try
        {
            await using var db = await AbrirAsync(cuenta, ct); using var tx = db.BeginTransaction();
            var jornada = await LeerAsync(db, tx, cuenta, jornadaId, ct) ?? throw new InvalidOperationException("No existe la jornada.");
            var cantidad = jornada.Operaciones.Count;
            var operacion = registrar(jornada);
            if (operacion.Tipo is not (TipoOperacionLocal.EntregaFisica or TipoOperacionLocal.EvidenciaEntrega or TipoOperacionLocal.IntentoFallido)
                || jornada.Operaciones.Count != cantidad + 1 || jornada.Operaciones[^1].Id != operacion.Id)
                throw new InvalidOperationException("La captura debe confirmar exactamente una operación nueva.");
            foreach (var id in capturas)
            {
                using var leer = Comando(db, tx, "SELECT EnvioId,Tipo,Lista,OperacionId FROM Capturas WHERE Id=$id AND JornadaId=$j AND Descartada=0",
                    ("$id", id.ToString()), ("$j", jornadaId.ToString()));
                await using var reader = await leer.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct) || !reader.GetBoolean(2) || !reader.IsDBNull(3)
                    || Guid.Parse(reader.GetString(0)) != operacion.EnvioId)
                    throw new InvalidOperationException("La captura no está lista o corresponde a otra entrega.");
                var campo = reader.GetString(1) == "Foto" ? "fotoId" : "firmaId";
                if (!operacion.Datos.TryGetProperty(campo, out var valor) || !valor.TryGetGuid(out var referencia) || referencia != id)
                    throw new InvalidOperationException("La operación no referencia la captura guardada.");
            }
            foreach (var id in capturas)
            {
                using var asociar = Comando(db, tx, "UPDATE Capturas SET OperacionId=$op WHERE Id=$id", ("$op", operacion.Id.ToString()), ("$id", id.ToString()));
                await asociar.ExecuteNonQueryAsync(ct);
            }
            using var guardar = Comando(db, tx, "UPDATE Jornadas SET Estado=$estado WHERE Id=$j",
                ("$estado", SerializadorJornadaLocal.Serializar(jornada)), ("$j", jornadaId.ToString()));
            await guardar.ExecuteNonQueryAsync(ct); tx.Commit(); return jornada;
        }
        finally { acceso.Release(); }
    }
    public async Task DescartarCapturaAsync(CuentaMovil cuenta, Guid id, CancellationToken ct = default)
    {
        await acceso.WaitAsync(ct);
        try
        {
            await using var db = await AbrirAsync(cuenta, ct);
            using var command = Comando(db, null, "UPDATE Capturas SET Descartada=1 WHERE Id=$id AND OperacionId IS NULL", ("$id", id.ToString()));
            if (await command.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException("La captura ya pertenece a una operación confirmada.");
        }
        finally { acceso.Release(); }
    }
    public async Task GuardarMetadatoAsync(CuentaMovil cuenta, string clave, byte[] contenido, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(clave) || clave.Length > 128 || contenido.Length > 65536) throw new ArgumentException("El metadato excede su tamaño admitido.");
        await acceso.WaitAsync(ct);
        try
        {
            await using var db = await AbrirAsync(cuenta, ct);
            using var command = Comando(db, null, "INSERT INTO Metadatos(Clave,Datos) VALUES($c,$d) ON CONFLICT(Clave) DO UPDATE SET Datos=excluded.Datos", ("$c", clave), ("$d", contenido));
            await command.ExecuteNonQueryAsync(ct);
        }
        finally { acceso.Release(); }
    }
    public async Task<byte[]?> LeerMetadatoAsync(CuentaMovil cuenta, string clave, CancellationToken ct = default)
    {
        await acceso.WaitAsync(ct);
        try
        {
            await using var db = await AbrirAsync(cuenta, ct);
            using var command = Comando(db, null, "SELECT Datos FROM Metadatos WHERE Clave=$c", ("$c", clave));
            return await command.ExecuteScalarAsync(ct) as byte[];
        }
        finally { acceso.Release(); }
    }
}
