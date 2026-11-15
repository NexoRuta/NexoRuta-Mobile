using NexoRuta.Mobile.Api.Identidad;
using NexoRuta.Mobile.Core.Jornadas;

namespace NexoRuta.Mobile.Application.Notificaciones;

public interface IRegistroInstalacionPush
{
    Task<CuentaMovil?> LeerCuentaActualAsync(CancellationToken ct = default);
    Task RegistrarAsync(CuentaMovil cuenta, string plataforma, string fid, bool permitida, CancellationToken ct = default);
}
