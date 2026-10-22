using System.Security.Cryptography;
using NexoRuta.Mobile.Core.Jornadas;
using NexoRuta.Mobile.Storage;
namespace NexoRuta.Mobile.Application;
public sealed class EvidenciasLocales(RepositorioJornadasSqlite datos, RepositorioArchivosCifrados archivos)
{
    public async Task<CapturaLocal> GuardarAsync(CuentaMovil cuenta, Guid jornada, Guid envio, string tipo, byte[] contenido, CancellationToken ct = default)
    {
        if (contenido.Length is 0 or > 2097152) throw new ArgumentException("La imagen debe tener hasta 2 MiB.");
        var captura = await datos.IniciarCapturaAsync(cuenta, jornada, envio, tipo, ct);
        await archivos.GuardarAsync(cuenta, captura.Id, contenido, ct);
        await datos.MarcarCapturaListaAsync(cuenta, captura.Id, ct);
        return captura with { Lista = true };
    }
    public async Task CompletarAsync(CuentaMovil cuenta, CapturaLocal captura, byte[] contenido, CancellationToken ct = default)
    {
        if (contenido.Length is 0 or > 2097152) throw new ArgumentException("La imagen debe tener hasta 2 MiB.");
        var propia = (await datos.LeerCapturasAsync(cuenta, captura.JornadaId, ct)).SingleOrDefault(x => x.Id == captura.Id);
        if (propia is null || propia.Lista || propia.OperacionId is not null) throw new InvalidOperationException("El borrador ya no admite recaptura.");
        await archivos.GuardarAsync(cuenta, propia.Id, contenido, ct);
        await datos.MarcarCapturaListaAsync(cuenta, propia.Id, ct);
    }
    public async Task RecuperarAsync(CuentaMovil cuenta, Guid jornada, CancellationToken ct = default)
    {
        foreach (var captura in (await datos.LeerCapturasAsync(cuenta, jornada, ct)).Where(x => !x.Lista && x.OperacionId is null))
        {
            byte[]? bytes = null;
            try
            {
                bytes = await archivos.LeerAsync(cuenta, captura.Id, ct);
                await datos.MarcarCapturaListaAsync(cuenta, captura.Id, ct);
            }
            catch (FileNotFoundException) { } // El borrador permanece visible para recapturarlo.
            finally { if (bytes is not null) CryptographicOperations.ZeroMemory(bytes); }
        }
    }
}
