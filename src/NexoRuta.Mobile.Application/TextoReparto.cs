namespace NexoRuta.Mobile.Application;

public static class TextoReparto
{
    public static string Estado(string estado) => estado switch
    {
        "Despachada" => "Lista para comenzar",
        "EnCurso" => "En curso",
        "Finalizada" => "Finalizada",
        "Admitido" or "EnDeposito" or "AsignadoRuta" => "Por entregar",
        "EnTransito" => "En camino",
        "Entregado" => "Entregado",
        "EntregaPendienteValidacion" => "Prueba pendiente",
        "EntregaSinValidacion" => "Requiere revisión",
        "IntentoFallido" => "Intento registrado",
        "EnDevolucion" => "Volver al depósito",
        "Devuelto" => "Devuelto",
        "Cancelado" => "Cancelado",
        _ => estado
    };
    public static string Bultos(int cantidad) => cantidad == 1 ? "1 bulto" : $"{cantidad} bultos";
}
