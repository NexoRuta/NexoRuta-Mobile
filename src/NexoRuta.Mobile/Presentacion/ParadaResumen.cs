using NexoRuta.Mobile.Application;
namespace NexoRuta.Mobile.Presentacion;
public sealed record ParadaResumen(Guid EnvioId, int Orden, string Destinatario, string Direccion, string Estado, string Bultos)
{
    public string EtiquetaEstado => TextoReparto.Estado(Estado);
}
public sealed record BultoCargaResumen(string Codigo, bool Cargado)
{
    public string Estado => Cargado ? "✓ Verificado" : "Por escanear";
}
