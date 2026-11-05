using System.Security.Cryptography;
using System.Text.Json;
using NexoRuta.Mobile.Api.Identidad;
using NexoRuta.Mobile.Api.Reparto;
using NexoRuta.Mobile.Core.Jornadas;
using NexoRuta.Mobile.Core.Rutas;
using NexoRuta.Mobile.Storage;
namespace NexoRuta.Mobile.Application;

public sealed class SincronizadorJornada(ClienteSesionMovil cliente, RepositorioJornadasSqlite datos,
    RepositorioArchivosCifrados archivos) : IDisposable
{
    private readonly SemaphoreSlim exclusividad = new(1, 1);
    public async Task<int> SincronizarPendientesAsync(CuentaMovil cuenta, CancellationToken ct = default)
    {
        var total = 0;
        foreach (var jornada in await datos.LeerJornadasAsync(cuenta, ct))
            while ((await datos.LeerAsync(cuenta, jornada.Id, ct))!.TienePendientes)
            {
                var enviados = await SincronizarAsync(cuenta, jornada.Id, ct);
                total += enviados;
                if (enviados == 0) break;
            }
        return total;
    }
    public async Task<int> SincronizarAsync(CuentaMovil cuenta, Guid jornadaId, CancellationToken ct = default)
    {
        await exclusividad.WaitAsync(ct);
        try
        {
            if (await cliente.ObtenerCuentaLocalAsync(ct) != cuenta) throw new InvalidOperationException("La sesión no corresponde a la cuenta de la jornada.");
            var jornada = await datos.LeerAsync(cuenta, jornadaId, ct) ?? throw new InvalidOperationException("No existe la jornada local.");
            var bytesHoja = await datos.LeerHojaAsync(cuenta, jornada.RutaId, ct) ?? throw new InvalidDataException("No existe la hoja descargada de esta jornada.");
            var hoja = SerializadorHojaRuta.Deserializar(bytesHoja, cuenta);
            var versiones = hoja.Paradas.ToDictionary(x => x.EnvioId, x => x.EnvioVersion);
            var versionesBulto = hoja.Paradas.SelectMany(p => p.Bultos).ToDictionary(b => b.Id, b => b.Version);
            foreach (var bytes in await datos.LeerConfirmacionesAsync(cuenta, jornadaId, ct))
            {
                var ack = Leer(bytes); ValidarBultos(ack, versionesBulto); AplicarVersiones(ack, versiones);
                if (ack.Resultado == "Aplicada") foreach (var b in ack.VersionesBulto) versionesBulto[b.Key] = b.Value;
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
                        var nuevos = new DatosOperacionMovil { ResultadosBultos = d.ResultadosBultos?.Select(b =>
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
                await SubirArchivos(cuenta, jornadaId, pendiente, ct);
                var resultado = await cliente.EnviarOperacionAsync(
                    cuenta, jornadaId, sobre, pendiente.Id, jornada.CantidadPendientes, ct);
                Validar(resultado, versiones); ValidarBultos(resultado, versionesBulto);
                if (resultado.Id != pendiente.Id || resultado.Secuencia != pendiente.Secuencia)
                    throw new InvalidDataException("El servidor no confirmó la operación enviada.");
                var respuesta = JsonSerializer.SerializeToUtf8Bytes(resultado, ContratosRepartoJson.Default.ConfirmacionOperacion);
                await datos.ConfirmarSobreAsync(cuenta, jornadaId, pendiente.Id, respuesta, ct);
                AplicarVersiones(resultado, versiones);
                if (resultado.Resultado == "Aplicada") foreach (var b in resultado.VersionesBulto) versionesBulto[b.Key] = b.Value;
                confirmadas++;
            }
            return confirmadas;
        }
        finally { exclusividad.Release(); }
    }
    private async Task SubirArchivos(CuentaMovil cuenta, Guid jornada, OperacionLocal op, CancellationToken ct)
    {
        if (op.EnvioId is not { } envio) return;
        foreach (var (campo, tipo) in new[] { ("fotoId", "Foto"), ("firmaId", "Firma") })
        {
            if (!op.Datos.TryGetProperty(campo, out var valor) || valor.ValueKind == JsonValueKind.Null) continue;
            if (!valor.TryGetGuid(out var id) || id == Guid.Empty) throw new InvalidDataException("La evidencia local no tiene un identificador válido.");
            var contenido = await archivos.LeerAsync(cuenta, id, ct);
            try { await cliente.SubirEvidenciaAsync(cuenta, jornada, envio, id, tipo, contenido, ct); }
            finally { CryptographicOperations.ZeroMemory(contenido); }
        }
    }
    private static void ValidarBultos(ConfirmacionOperacion r, Dictionary<Guid, long> versiones)
    {
        if (r.VersionesBulto is null || r.VersionesBulto.Any(b => !versiones.ContainsKey(b.Key) || b.Value < 0)
            || r.Resultado == "Conflicto" && r.VersionesBulto.Count > 0)
            throw new InvalidDataException("Las versiones de bulto confirmadas no corresponden a la hoja.");
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
