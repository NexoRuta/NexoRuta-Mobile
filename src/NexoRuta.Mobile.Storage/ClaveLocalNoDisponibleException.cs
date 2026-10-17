namespace NexoRuta.Mobile.Storage;

public sealed class ClaveLocalNoDisponibleException : InvalidOperationException
{
    public ClaveLocalNoDisponibleException()
        : base("La clave local no está disponible. Los archivos se conservan; se requiere recuperar el acceso sin borrar los pendientes.") { }
}
