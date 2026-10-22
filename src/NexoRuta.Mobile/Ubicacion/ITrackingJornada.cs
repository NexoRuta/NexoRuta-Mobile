namespace NexoRuta.Mobile.Ubicacion;
public interface ITrackingJornada
{
    Task<bool> IniciarAsync();
    void Detener();
}
