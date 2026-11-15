using NexoRuta.Mobile.Api.Identidad;
using NexoRuta.Mobile.Core.Jornadas;

namespace NexoRuta.Mobile.Application.Notificaciones;

public sealed class RegistroInstalacionPushApi(
    ClienteSesionMovil cliente,
    ServicioAvisosReparto avisos) : IRegistroInstalacionPush
{
    public Task<CuentaMovil?> LeerCuentaActualAsync(CancellationToken ct = default)
        => cliente.LeerCuentaGuardadaAsync(ct);

    public Task RegistrarAsync(CuentaMovil cuenta, string plataforma, string fid, bool permitida, CancellationToken ct = default)
        => avisos.RegistrarInstalacionAsync(cuenta, plataforma, fid, permitida, ct);
}
