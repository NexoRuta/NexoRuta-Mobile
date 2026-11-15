using Microsoft.Extensions.Logging;
using NexoRuta.Mobile.Api.Identidad;

namespace NexoRuta.Mobile.Application.Notificaciones;

public sealed class ServicioRegistroPush(
    IRegistroPush registro,
    IAlmacenFidPush almacenFid,
    IRegistroInstalacionPush? instalaciones,
    string plataforma,
    ILogger<ServicioRegistroPush>? logger = null)
{
    public const string ClaveFid = "nexoruta.push.fid.v1";
    private readonly SemaphoreSlim bloqueoRegistro = new(1, 1);

    public event Action? EstadoCambiado;
    public bool PuedeActivar => registro.MotivoNoDisponible is null;
    public string Estado { get; private set; } = registro.MotivoNoDisponible ?? "Activá los avisos para recibir cambios urgentes de tu ruta.";

    public async Task ActivarAsync()
    {
        if (instalaciones is null)
        {
            CambiarEstado("Primero debe configurarse el servicio de NexoRuta.");
            return;
        }
        if (registro.MotivoNoDisponible is { } motivo)
        {
            CambiarEstado(motivo);
            return;
        }

        try
        {
            CambiarEstado("Activando las notificaciones…");
            if (!await registro.SolicitarPermisoAsync())
            {
                CambiarEstado("Las notificaciones están desactivadas. Podés consultar las novedades en Avisos.");
                await ActualizarRegistroAsync();
                return;
            }

            await registro.RegistrarAsync();
            if (!await ActualizarRegistroAsync())
                CambiarEstado("Activando las notificaciones…");
        }
        catch (Exception ex)
        {
            logger?.LogWarning("No se pudo iniciar el registro push. ErrorType: {ErrorType}", ex.GetType().Name);
            CambiarEstado("No pudimos activar las notificaciones. Se volverá a intentar al recuperar conexión.");
            throw;
        }
    }

    public async Task RegistroRecibidoAsync(string fid)
    {
        if (fid.Length is < 20 or > 128 || fid.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_'))) return;

        try
        {
            CambiarEstado("Activando las notificaciones…");
            await almacenFid.GuardarAsync(fid);
            await ActualizarRegistroAsync();
        }
        catch (Exception ex)
        {
            logger?.LogWarning("No se pudo guardar el registro push. ErrorType: {ErrorType}", ex.GetType().Name);
            CambiarEstado("No pudimos guardar el registro de notificaciones. Se volverá a intentar al recuperar conexión.");
            throw;
        }
    }

    public async Task<bool> ActualizarRegistroAsync(CancellationToken ct = default)
    {
        if (instalaciones is null || registro.MotivoNoDisponible is not null) return false;

        await bloqueoRegistro.WaitAsync(ct);
        try
        {
            while (true)
            {
                var cuenta = await instalaciones.LeerCuentaActualAsync(ct);
                var fid = await almacenFid.LeerAsync();
                if (cuenta is null || fid is null) return false;

                var permitido = await registro.PermitidoAsync();
                await instalaciones.RegistrarAsync(cuenta, plataforma, fid, permitido, ct);

                // Una rotación del FID, un cambio de permiso o una sesión nueva durante el
                // POST requiere registrar el estado actual antes de mostrar éxito.
                var cuentaActual = await instalaciones.LeerCuentaActualAsync(ct);
                var fidActual = await almacenFid.LeerAsync();
                var permitidoActual = await registro.PermitidoAsync();
                if (cuentaActual == cuenta && fidActual == fid && permitidoActual == permitido)
                {
                    CambiarEstado(permitido
                        ? "Notificaciones activadas."
                        : "Las notificaciones están desactivadas. Podés consultar las novedades en Avisos.");
                    return true;
                }

                if (cuentaActual is null || fidActual is null) return false;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogWarning("No se pudo registrar la instalación push en NexoRuta. ErrorType: {ErrorType}", ex.GetType().Name);
            CambiarEstado("No pudimos confirmar el registro de notificaciones. Se volverá a intentar al recuperar conexión.");
            throw;
        }
        finally
        {
            bloqueoRegistro.Release();
        }
    }

    public void SalidaIncompleta()
        => CambiarEstado("Si la salida no se completó, los avisos se reactivan al recuperar conexión o volver a ingresar.");

    public void SesionCerrada()
        => CambiarEstado("Activá los avisos para recibir cambios urgentes de tu ruta.");

    public void AvisoPendiente()
        => CambiarEstado("Hay una novedad en tu recorrido. Revisá tus avisos.");

    private void CambiarEstado(string estado)
    {
        if (Estado == estado) return;
        Estado = estado;
        EstadoCambiado?.Invoke();
    }
}
