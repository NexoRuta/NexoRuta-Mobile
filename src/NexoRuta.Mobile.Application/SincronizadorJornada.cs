using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NexoRuta.Mobile.Api.Identidad;
using NexoRuta.Mobile.Api.Reparto;
using NexoRuta.Mobile.Core.Jornadas;
using NexoRuta.Mobile.Core.Rutas;
using NexoRuta.Mobile.Storage;
namespace NexoRuta.Mobile.Application;

public sealed class SincronizadorJornada(ClienteSesionMovil cliente, RepositorioJornadasSqlite datos,
    RepositorioArchivosCifrados archivos, ILogger<SincronizadorJornada>? logger = null) : IDisposable
{
    private readonly SemaphoreSlim exclusividad = new(1, 1);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(CuentaMovil Cuenta, Guid Jornada, Guid Operacion), ConfirmacionOperacion> recuperadas = new();
    public ConfirmacionOperacion? ObtenerConfirmacionRecuperada(CuentaMovil cuenta, Guid jornada, Guid operacion)
        => recuperadas.GetValueOrDefault((cuenta, jornada, operacion));
    public async Task<int> SincronizarPendientesAsync(CuentaMovil cuenta, CancellationToken ct = default)
    {
        var total = 0;
        await ReportarEstadoAsync(cuenta);
        try
        {
            var jornadas = await datos.LeerJornadasAsync(cuenta, ct);
            foreach (var jornada in jornadas)
                while ((await datos.LeerAsync(cuenta, jornada.Id, ct))!.TienePendientes)
                {
                    var enviados = await SincronizarAsync(cuenta, jornada.Id, ct);
                    total += enviados;
                    if (enviados == 0) break;
                }
            return total;
        }
        finally { await ReportarEstadoAsync(cuenta); }
    }
    public async Task<int> SincronizarAsync(CuentaMovil cuenta, Guid jornadaId, CancellationToken ct = default)
    {
        await exclusividad.WaitAsync(ct);
        try
        {
            if (await cliente.ObtenerCuentaLocalAsync(ct) != cuenta) throw new InvalidOperationException("La sesión no corresponde a la cuenta de la jornada.");
            await ReportarEstadoAsync(cuenta);
            try
            {
            var jornada = await datos.LeerAsync(cuenta, jornadaId, ct) ?? throw new InvalidOperationException("No existe la jornada local.");
            var bytesHoja = await datos.LeerHojaAsync(cuenta, jornada.RutaId, ct) ?? throw new InvalidDataException("No existe la hoja descargada de esta jornada.");
            var hoja = SerializadorHojaRuta.Deserializar(bytesHoja, cuenta);
            var versiones = hoja.Paradas.ToDictionary(x => x.EnvioId, x => x.EnvioVersion);
            var versionesBulto = hoja.Paradas.SelectMany(p => p.Bultos).ToDictionary(b => b.Id, b => b.Version);
            var declarados = hoja.Paradas.SelectMany(p => p.Bultos).Where(b => b.Resultado != "Pendiente").Select(b => b.Id).ToHashSet();
            foreach (var bytes in await datos.LeerConfirmacionesAsync(cuenta, jornadaId, ct))
            {
                var ack = Leer(bytes);
                var op = jornada.Operaciones.SingleOrDefault(o => o.Id == ack.Id && o.Secuencia == ack.Secuencia)
                    ?? throw new InvalidDataException("El acuse local no corresponde al diario de esta jornada.");
                var esperados = ack.Resultado == "Aplicada" ? BultosModificados(op, hoja, declarados) : [];
                if (esperados.Any(id => !versionesBulto.ContainsKey(id)))
                    throw new InvalidDataException("El diario contiene un bulto ajeno a la hoja; conservamos sus registros para revisión.");
                Validar(ack, versiones); ValidarBultos(ack, versionesBulto);
                if (ack.Resultado == "Aplicada" && esperados.Any(id => !ack.VersionesBulto.ContainsKey(id)))
                {
                    if (!recuperadas.TryGetValue((cuenta, jornadaId, op.Id), out var recuperada))
                    {
                        var sobreOriginal = await datos.LeerSobreAsync(cuenta, jornadaId, op.Id, ct)
                            ?? throw new InvalidDataException("Falta el sobre original para recuperar el acuse de bultos. Conservá los datos y revisá la jornada.");
                        // ponytail: historical truncated receipts replay once per process/operation; restart may replay again.
                        // The server's existing identity/hash check owns idempotence. Neither durable bytes nor receipts change.
                        var traceParent = await ObtenerTraceParentAsync(cuenta, jornadaId, op.Id, ct);
                        recuperada = await cliente.EnviarOperacionAsync(cuenta, jornadaId, sobreOriginal, op.Id, traceParent, jornada.CantidadPendientes, ct);
                        Validar(recuperada, versiones); ValidarBultos(recuperada, versionesBulto);
                        if (recuperada.Id != ack.Id || recuperada.Secuencia != ack.Secuencia || recuperada.Resultado != ack.Resultado
                            || recuperada.VersionRuta != ack.VersionRuta || recuperada.VersionEnvio != ack.VersionEnvio
                            || recuperada.VersionesEnvio.Count != ack.VersionesEnvio.Count || ack.VersionesEnvio.Any(v => recuperada.VersionesEnvio.GetValueOrDefault(v.Key, -1) != v.Value)
                            || recuperada.EstadosEnvio.Count != ack.EstadosEnvio.Count || ack.EstadosEnvio.Any(v => recuperada.EstadosEnvio.GetValueOrDefault(v.Key) != v.Value)
                            || esperados.Any(id => !recuperada.VersionesBulto.TryGetValue(id, out var v) || v <= versionesBulto[id]))
                            throw new InvalidDataException("El acuse recuperado no confirma las versiones de bulto de la operación original. Conservá los datos y revisá la jornada.");
                        recuperadas[(cuenta, jornadaId, op.Id)] = recuperada;
                    }
                    ack = recuperada;
                }
                AplicarVersiones(ack, versiones);
                if (ack.Resultado == "Aplicada") foreach (var b in ack.VersionesBulto) versionesBulto[b.Key] = b.Value;
                IncorporarHecho(ack, op, hoja, declarados);
            }
            var confirmadas = 0;
            while (confirmadas < 100)
            {
                jornada = (await datos.LeerAsync(cuenta, jornadaId, ct))!;
                var operacionesPendientes = jornada.PrepararEnvio();
                var pendiente = operacionesPendientes.OrderBy(x => x.Secuencia).FirstOrDefault();
                if (pendiente is null) break;
                var sobre = await datos.LeerSobreAsync(cuenta, jornadaId, pendiente.Id, ct);
                if (sobre is null)
                {
                    long? version = pendiente.EnvioId is { } envio
                        ? versiones.GetValueOrDefault(envio, -1) : null;
                    if (version < 0) throw new InvalidDataException("La operación pertenece a otro envío.");
                    var datosEnvio = pendiente.Datos;
                    if (pendiente.Tipo is TipoOperacionLocal.EntregaBultos or TipoOperacionLocal.RetirarBultos)
                    {
                        var d = pendiente.Datos.Deserialize(DatosOperacionJson.Default.DatosOperacionMovil)!;
                        var nuevos = new DatosOperacionMovil { ResuelveOperacionId = d.ResuelveOperacionId, ResultadosBultos = d.ResultadosBultos?.Select(b =>
                            b with { Version = versionesBulto.GetValueOrDefault(b.BultoId, b.Version) }).ToArray(),
                            FotoId = d.FotoId, FirmaId = d.FirmaId, Nombre = d.Nombre, Documento = d.Documento,
                            Latitud = d.Latitud, Longitud = d.Longitud, PrecisionMetros = d.PrecisionMetros, PosicionEnUtc = d.PosicionEnUtc };
                        datosEnvio = JsonSerializer.SerializeToElement(nuevos, DatosOperacionJson.Default.DatosOperacionMovil);
                    }
                    sobre = JsonSerializer.SerializeToUtf8Bytes(new SobreOperacion
                    {
                        Id = pendiente.Id, Secuencia = pendiente.Secuencia, Tipo = pendiente.Tipo.ToString(),
                        EnvioId = pendiente.EnvioId, VersionEnvio = version, OcurridaEn = pendiente.OcurridaEn,
                        Datos = datosEnvio
                    }, ContratosRepartoJson.Default.SobreOperacion);
                    await datos.GuardarSobreAsync(cuenta, jornadaId, pendiente.Id, sobre, ct);
                }
                var operationTraceParent = await ObtenerTraceParentAsync(cuenta, jornadaId, pendiente.Id, ct);
                using var operationScope = logger?.BeginScope(new Dictionary<string, object?>
                {
                    ["correlation_id"] = pendiente.Id.ToString("D"),
                    ["mobile_operation_id"] = pendiente.Id.ToString("D"),
                    ["journey_id"] = jornadaId.ToString("D")
                });
                logger?.LogInformation("Mobile operation is queued for synchronization.");
                await SubirArchivos(cuenta, jornadaId, pendiente, operationTraceParent, ct);
                var resultado = await cliente.EnviarOperacionAsync(
                    cuenta, jornadaId, sobre, pendiente.Id, operationTraceParent, jornada.CantidadPendientes, ct);
                Validar(resultado, versiones); ValidarBultos(resultado, versionesBulto);
                if (resultado.Id != pendiente.Id || resultado.Secuencia != pendiente.Secuencia)
                    throw new InvalidDataException("El servidor no confirmó la operación enviada.");
                if (resultado.Resultado == "Aplicada" && BultosModificados(pendiente, hoja, declarados)
                        .Any(id => !versionesBulto.ContainsKey(id) || !resultado.VersionesBulto.TryGetValue(id, out var v) || v <= versionesBulto[id]))
                    throw new InvalidDataException("El acuse no confirma las versiones de los bultos modificados. Conservamos el sobre para reintentar la misma operación.");
                var respuesta = JsonSerializer.SerializeToUtf8Bytes(resultado, ContratosRepartoJson.Default.ConfirmacionOperacion);
                await datos.ConfirmarSobreAsync(cuenta, jornadaId, pendiente.Id, respuesta, ct);
                AplicarVersiones(resultado, versiones);
                if (resultado.Resultado == "Aplicada") foreach (var b in resultado.VersionesBulto) versionesBulto[b.Key] = b.Value;
                IncorporarHecho(resultado, pendiente, hoja, declarados);
                confirmadas++;
            }
            return confirmadas;
            }
            finally
            {
                await ReportarEstadoAsync(cuenta);
            }
        }
        finally { exclusividad.Release(); }
    }
    private static Guid[] BultosModificados(OperacionLocal op, HojaRuta hoja, HashSet<Guid> declarados)
    {
        var d = op.Datos.Deserialize(DatosOperacionJson.Default.DatosOperacionMovil)!;
        if (op.Tipo == TipoOperacionLocal.CargarBulto && d.BultoId is null
            || op.Tipo == TipoOperacionLocal.Rendicion && d.BultoIds is not { Length: > 0 }
            || op.Tipo is TipoOperacionLocal.EntregaBultos or TipoOperacionLocal.RetirarBultos && d.ResultadosBultos is not { Length: > 0 })
            throw new InvalidDataException("El acuse aplicado no identifica los bultos de su operación; conservamos el diario para revisión.");
        return op.Tipo switch
        {
            TipoOperacionLocal.CargarBulto => d.BultoId is { } id ? [id] : [],
            TipoOperacionLocal.Rendicion => d.BultoIds ?? [],
            TipoOperacionLocal.RetirarBultos => d.ResultadosBultos?.Select(b => b.BultoId).ToArray() ?? [],
            TipoOperacionLocal.EntregaBultos => d.ResultadosBultos?.Where(b => !declarados.Contains(b.BultoId)).Select(b => b.BultoId).ToArray() ?? [],
            TipoOperacionLocal.EntregaFisica => hoja.Paradas.Single(p => p.EnvioId == op.EnvioId).Bultos.Select(b => b.Id).ToArray(),
            _ => []
        };
    }
    private static void IncorporarHecho(ConfirmacionOperacion ack, OperacionLocal op, HojaRuta hoja, HashSet<Guid> declarados)
    {
        if (ack.Resultado != "Aplicada") return;
        if (op.Tipo == TipoOperacionLocal.EntregaFisica)
            declarados.UnionWith(hoja.Paradas.Single(p => p.EnvioId == op.EnvioId).Bultos.Select(b => b.Id));
        else if (op.Tipo == TipoOperacionLocal.EntregaBultos)
            declarados.UnionWith(op.Datos.Deserialize(DatosOperacionJson.Default.DatosOperacionMovil)!.ResultadosBultos?.Where(b => b.Entregado).Select(b => b.BultoId) ?? []);
    }
    private async Task SubirArchivos(CuentaMovil cuenta, Guid jornada, OperacionLocal op, string traceParent, CancellationToken ct)
    {
        if (op.EnvioId is not { } envio) return;
        foreach (var (campo, tipo) in new[] { ("fotoId", "Foto"), ("firmaId", "Firma") })
        {
            if (!op.Datos.TryGetProperty(campo, out var valor) || valor.ValueKind == JsonValueKind.Null) continue;
            if (!valor.TryGetGuid(out var id) || id == Guid.Empty) throw new InvalidDataException("La evidencia local no tiene un identificador válido.");
            var contenido = await archivos.LeerAsync(cuenta, id, ct);
            try { await cliente.SubirEvidenciaAsync(cuenta, jornada, envio, id, op.Id, traceParent, tipo, contenido, ct); }
            finally { CryptographicOperations.ZeroMemory(contenido); }
        }
    }

    private async Task<string> ObtenerTraceParentAsync(CuentaMovil cuenta, Guid jornada, Guid operacion, CancellationToken ct)
    {
        var actual = await datos.LeerTraceParentAsync(cuenta, jornada, operacion, ct);
        return actual ?? await datos.GuardarTraceParentSiAusenteAsync(cuenta, jornada, operacion,
            TelemetryMovil.CrearContextoOperacion(), ct);
    }

    private async Task ReportarEstadoAsync(CuentaMovil cuenta)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var cancellationToken = timeout.Token;
        try
        {
            var pendientes = 0;
            foreach (var jornada in await datos.LeerJornadasAsync(cuenta, cancellationToken))
                pendientes = checked(pendientes + jornada.CantidadPendientes);
            var identity = await datos.SiguienteReporteSyncAsync(cuenta, cancellationToken);
            await cliente.ReportarEstadoSincronizacionAsync(cuenta,
                new(identity.InstallationId, identity.Sequence, pendientes), timeout.Token);
        }
        catch (Exception error)
        {
            logger?.LogDebug("Mobile synchronization status report was not delivered. ErrorType: {ErrorType}", error.GetType().Name);
        }
    }
    private static void ValidarBultos(ConfirmacionOperacion r, Dictionary<Guid, long> versiones)
    {
        if (r.VersionesBulto is null || r.VersionesBulto.Any(b => !versiones.ContainsKey(b.Key) || b.Value < 0
                || r.Resultado == "Aplicada" && b.Value < versiones[b.Key])
            || r.Resultado == "Conflicto" && r.VersionesBulto.Count > 0)
            throw new InvalidDataException(
                $"Acuse={r.Id}; secuencia={r.Secuencia}; resultado={r.Resultado}; " +
                $"bultosHoja=[{string.Join(",", versiones.Keys)}]; " +
                $"versionesAcuse=[{(r.VersionesBulto is null
                    ? "NULL"
                    : string.Join(",", r.VersionesBulto.Select(b => $"{b.Key}:{b.Value}")))}]");
    }
    private static ConfirmacionOperacion Leer(byte[] bytes) => JsonSerializer.Deserialize(bytes, ContratosRepartoJson.Default.ConfirmacionOperacion)
        ?? throw new InvalidDataException("La confirmación local está vacía.");
    private static void AplicarVersiones(ConfirmacionOperacion resultado, Dictionary<Guid, long> versiones)
    {
        Validar(resultado, versiones);
        // Únicamente cambios causados por nuestros propios comandos aplicados: una lectura nueva
        // del servidor podría esconder una reasignación o cancelación concurrente.
        if (resultado.Resultado == "Aplicada") foreach (var cambio in resultado.VersionesEnvio) versiones[cambio.Key] = cambio.Value;
    }
    private static void Validar(ConfirmacionOperacion r, Dictionary<Guid, long> versiones)
    {
        if (r.Id == Guid.Empty || r.Secuencia < 1 || r.Resultado is not ("Aplicada" or "Conflicto") || r.VersionRuta < 0
            || r.VersionEnvio < 0 || r.VersionesEnvio is null || r.VersionesEnvio.Any(x => !versiones.ContainsKey(x.Key) || x.Value < 0)
            || r.EstadosEnvio is null || r.EstadosEnvio.Keys.Any(x => !versiones.ContainsKey(x))
            || r.Resultado == "Conflicto" && (r.VersionesEnvio.Count != 0 || r.EstadosEnvio.Count != 0))
            throw new InvalidDataException("La confirmación de sincronización está incompleta o no corresponde a la hoja.");
    }
    public void Dispose() => exclusividad.Dispose();
}
