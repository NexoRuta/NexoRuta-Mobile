using NexoRuta.Mobile.Core.Jornadas;
namespace NexoRuta.Mobile.Core.Notificaciones;

// El payload solo referencia un aviso; el estado operativo siempre se consulta al servidor.
public sealed record AvisoPush(Guid Id, Guid OperadorId, Guid AccesoId)
{
    public bool CorrespondeA(CuentaMovil cuenta) => OperadorId == cuenta.OperadorId && AccesoId == cuenta.AccesoId;
    public static bool IntentarLeer(IReadOnlyDictionary<string, string>? datos, out AvisoPush? aviso)
    {
        aviso = null;
        if (datos is null || datos.Count != 4 || !datos.TryGetValue("v", out var version) || version != "1"
            || !Identificador(datos, "notificacionId", out var id)
            || !Identificador(datos, "operadorId", out var operador)
            || !Identificador(datos, "accesoId", out var acceso)) return false;
        aviso = new(id, operador, acceso); return true;
    }
    private static bool Identificador(IReadOnlyDictionary<string, string> datos, string nombre, out Guid id)
    {
        id = Guid.Empty;
        return datos.TryGetValue(nombre, out var valor) && valor is { Length: 36 }
            && Guid.TryParseExact(valor, "D", out id) && id != Guid.Empty;
    }
}
