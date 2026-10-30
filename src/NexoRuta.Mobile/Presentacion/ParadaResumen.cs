using NexoRuta.Mobile.Application;
namespace NexoRuta.Mobile.Presentacion;
public sealed record ParadaResumen(Guid EnvioId, int Orden, string Destinatario, string Direccion,
    string Estado, string Bultos, string EstadoRuta, string Custodia)
{
    public string EtiquetaEstado => TextoReparto.Estado(Estado);
}
public sealed record BultoCargaResumen(string Codigo, bool Cargado, bool Confirmado, bool Rechazado)
{
    public string Estado => Rechazado ? "Rechazado · revisar" : !Cargado ? "Por escanear"
        : Confirmado ? "Verificado por el servidor" : "Registrado · pendiente de sincronización";
}
