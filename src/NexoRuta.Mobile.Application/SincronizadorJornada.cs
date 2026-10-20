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
            foreach (var bytes in await datos.LeerConfirmacionesAsync(cuenta, jornadaId, ct))
                AplicarVersiones(Leer(bytes), versiones);
            var confirmadas = 0;
            while (confirmadas < 100)
            {
                jornada = (await datos.LeerAsync(cuenta, jornadaId, ct))!;
                var pendiente = jornada.PrepararEnvio().OrderBy(x => x.Secuencia).FirstOrDefault();
                if (pendiente is null) break;
                var sobre = await datos.LeerSobreAsync(cuenta, jornadaId, pendiente.Id, ct);
                if (sobre is null)
                {
                    long? version = pendiente.EnvioId is { } envio
                        ? versiones.GetValueOrDefault(envio, -1) : null;
                    if (version < 0) throw new InvalidDataException("La operación pertenece a otro envío.");
                    sobre = JsonSerializer.SerializeToUtf8Bytes(new SobreOperacion
                    {
                        Id = pendiente.Id, Secuencia = pendiente.Secuencia, Tipo = pendiente.Tipo.ToString(),
                        EnvioId = pendiente.EnvioId, VersionEnvio = version, OcurridaEn = pendiente.OcurridaEn,
                        Datos = pendiente.Datos
                    }, ContratosRepartoJson.Default.SobreOperacion);
                    await datos.GuardarSobreAsync(cuenta, jornadaId, pendiente.Id, sobre, ct);
                }
                await SubirArchivos(cuenta, jornadaId, pendiente, ct);
                var resultado = await cliente.EnviarOperacionAsync(cuenta, jornadaId, sobre, ct);
                Validar(resultado, versiones);
                if (resultado.Id != pendiente.Id || resultado.Secuencia != pendiente.Secuencia)
                    throw new InvalidDataException("El servidor no confirmó la operación enviada.");
                var respuesta = JsonSerializer.SerializeToUtf8Bytes(resultado, ContratosRepartoJson.Default.ConfirmacionOperacion);
                await datos.ConfirmarSobreAsync(cuenta, jornadaId, pendiente.Id, respuesta, ct);
                AplicarVersiones(resultado, versiones); confirmadas++;
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
