namespace NexoRuta.Mobile.Application.Notificaciones;

public static class TrabajoConRegistroPushIndependiente
{
    public static async Task EjecutarAsync(Func<Task> trabajo, Func<Task> registrarPush)
    {
        try { await trabajo(); }
        catch (Exception) { /* El trabajo principal conserva su política de reintento. */ }
        finally
        {
            try { await registrarPush(); }
            catch (Exception) { /* El próximo disparador reintentará el registro durable. */ }
        }
    }
}
