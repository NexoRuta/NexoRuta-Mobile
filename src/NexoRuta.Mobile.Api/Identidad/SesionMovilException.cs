namespace NexoRuta.Mobile.Api.Identidad;

public sealed class SesionMovilException(ErrorSesionMovil error) : Exception(error switch
{
    ErrorSesionMovil.CredencialesRechazadas => "No se pudo ingresar con esa cuenta y contraseña.",
    ErrorSesionMovil.ReingresoNecesario => "Ingresá nuevamente con la misma cuenta para continuar.",
    ErrorSesionMovil.CuentaDiferente => "Debés ingresar con la cuenta que conserva el trabajo local.",
    ErrorSesionMovil.AccesoDenegado => "La cuenta no tiene acceso a esta operación.",
    ErrorSesionMovil.DemasiadasSolicitudes => "Se realizaron demasiados intentos. Esperá antes de volver a intentar.",
    ErrorSesionMovil.ServicioNoDisponible => "El servicio no está disponible en este momento.",
    ErrorSesionMovil.EstadoLocalInvalido => "No se pudo recuperar la sesión local. Se conservan los datos existentes.",
    ErrorSesionMovil.RecursoNoDisponible => "El aviso o la ruta ya no están disponibles para esta cuenta.",
    ErrorSesionMovil.ConflictoServidor => "Hay cambios en el servidor. Sincronizá para revisar el estado actual.",
    ErrorSesionMovil.ReautenticarParaCerrarSesion => "Volvé a ingresar con la misma cuenta para cerrar la sesión. Tus datos locales se conservan.",
    ErrorSesionMovil.CierreNoConfirmado => "No recibimos confirmación del cierre. Tu cuenta y el trabajo local se conservan; reconectate y volvé a intentarlo.",
    _ => "El servicio devolvió una respuesta de sesión inválida."
})
{
    public ErrorSesionMovil Error { get; } = error;
}
