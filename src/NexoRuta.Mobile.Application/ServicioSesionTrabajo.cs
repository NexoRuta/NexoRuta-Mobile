using NexoRuta.Mobile.Api.Identidad;
using NexoRuta.Mobile.Core.Jornadas;
using NexoRuta.Mobile.Storage;
namespace NexoRuta.Mobile.Application;

public sealed class ServicioSesionTrabajo(ClienteSesionMovil cliente, RepositorioJornadasSqlite datos, ServicioAvisosReparto avisos)
{
    public async Task CerrarAsync(CuentaMovil cuenta, CancellationToken ct = default)
    {
        if (await cliente.LeerCuentaGuardadaAsync(ct) != cuenta)
            throw new SesionMovilException(ErrorSesionMovil.CuentaDiferente);
        var instalacion = await avisos.ObtenerInstalacionLocalAsync(cuenta, ct);
        if (!await datos.SuspenderSiSinPendientesAsync(cuenta, ct))
            throw new InvalidOperationException("Sincronizá las operaciones y confirmá o descartá los borradores de evidencia antes de cerrar o cambiar de cuenta.");
        try { await cliente.CerrarSesionAsync(cuenta, instalacion, ct); }
        catch { await datos.ReanudarCuentaAsync(cuenta, CancellationToken.None); throw; }
        // La cuenta se reanuda solo tras un ingreso posterior; su diario y archivos no se borran.
    }
}
