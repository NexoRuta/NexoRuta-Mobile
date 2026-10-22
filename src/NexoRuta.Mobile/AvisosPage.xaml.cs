using System.Collections.ObjectModel;
using NexoRuta.Mobile.Api.Identidad;
using NexoRuta.Mobile.Api.Notificaciones;
using NexoRuta.Mobile.Application;
using NexoRuta.Mobile.Core.Jornadas;
using NexoRuta.Mobile.Core.Rutas;
using NexoRuta.Mobile.Storage;
namespace NexoRuta.Mobile;

public sealed record AvisoResumen(Guid Id, string Titulo, string Motivo, string Fecha);
public partial class AvisosPage : ContentPage
{
    private readonly CuentaMovil cuenta;
    private readonly ServicioAvisosReparto servicio;
    private readonly IServiceProvider servicios;
    private Guid? apertura;
    private AvisoReparto? seleccionado;
    private HojaRuta? remota;
    private bool disponible = true;
    private string mensaje = "Los avisos te ayudan a continuar con el recorrido correcto.";
    public ObservableCollection<AvisoResumen> Avisos { get; } = [];
    public bool Disponible { get => disponible; private set { disponible = value; OnPropertyChanged(); } }
    public string Mensaje { get => mensaje; private set { mensaje = value; OnPropertyChanged(); } }
    public AvisosPage(CuentaMovil cuenta, ServicioAvisosReparto servicio, IServiceProvider servicios, Guid? apertura = null)
    {
        InitializeComponent(); this.cuenta = cuenta; this.servicio = servicio; this.servicios = servicios;
        this.apertura = apertura; BindingContext = this;
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await Ejecutar(async () =>
        {
            await Listar();
            if (apertura is { } id) { apertura = null; await Mostrar(id); }
        });
    }
    private async Task Listar()
    {
        var lista = await servicio.ListarAsync(cuenta);
        Avisos.Clear();
        foreach (var a in lista) Avisos.Add(new(a.Id, a.Tipo == "Cancelacion" ? "Entrega cancelada" : "Entrega postergada",
            a.Motivo, a.CreadoEnUtc.ToLocalTime().ToString("dd/MM HH:mm")));
    }
    private async Task Mostrar(Guid id)
    {
        seleccionado = null; remota = null;
        VerHoja.IsVisible = Entendido.IsVisible = false; Detalle.Text = "Consultando el estado actual…";
        var resultado = await servicio.AbrirAsync(cuenta, id);
        seleccionado = resultado.Aviso; remota = resultado.Hoja;
        Detalle.Text = seleccionado.Vigente
            ? $"{seleccionado.Motivo}\nEste envío salió de tu recorrido. Si lo tenés cargado, devolvelo al depósito. Si ya lo entregaste, conservá su registro."
            : "Este aviso ya no está vigente. Revisá el estado actual de tu hoja.";
        VerHoja.IsVisible = remota is not null && seleccionado.Vigente;
        Entendido.IsVisible = seleccionado.ConfirmadoEnUtc is null;
    }
    private async void Actualizar(object? sender, EventArgs e) => await Ejecutar(Listar);
    private async void Elegir(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is CollectionView lista) lista.SelectedItem = null;
        if (e.CurrentSelection.FirstOrDefault() is AvisoResumen aviso) await Ejecutar(() => Mostrar(aviso.Id));
    }
    private async void Confirmar(object? sender, EventArgs e) => await Ejecutar(async () =>
    {
        if (seleccionado is null) return;
        await servicio.ConfirmarAsync(cuenta, seleccionado.Id);
        Entendido.IsVisible = false; Mensaje = "Aviso confirmado. Podés continuar con tu recorrido.";
        await Listar();
    });
    private async void AbrirHoja(object? sender, EventArgs e) => await Ejecutar(async () =>
    {
        if (remota is null) return;
        var datos = servicios.GetRequiredService<RepositorioJornadasSqlite>();
        var local = (await datos.LeerHojasAsync(cuenta)).Select(b => SerializadorHojaRuta.Deserializar(b, cuenta)).SingleOrDefault(h => h.Id == remota.Id);
        await Navigation.PushAsync(new HojaRutaPage(local ?? remota, cuenta, servicios));
    });
    private async Task Ejecutar(Func<Task> accion)
    {
        if (!Disponible) return;
        Disponible = false;
        try { await accion(); }
        catch (SesionMovilException ex) { Mensaje = ex.Message; }
        catch (HttpRequestException) { Mensaje = "No pudimos actualizar los avisos. Volvé a intentarlo en un momento."; }
        catch (Exception) { Mensaje = "No pudimos consultar este aviso. Volvé a intentarlo."; }
        finally { Disponible = true; }
    }
}
