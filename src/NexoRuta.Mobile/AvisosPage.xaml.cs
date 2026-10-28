using System.Collections.ObjectModel;
using NexoRuta.Mobile.Api.Identidad;
using NexoRuta.Mobile.Api.Notificaciones;
using NexoRuta.Mobile.Application;
using NexoRuta.Mobile.Core.Jornadas;
using NexoRuta.Mobile.Core.Rutas;
using NexoRuta.Mobile.Storage;
namespace NexoRuta.Mobile;

public sealed record AvisoResumen(Guid Id, string Titulo, string Motivo, string Fecha);
public partial class AvisosPage : ContentView
{
    private readonly CuentaMovil cuenta;
    private readonly ServicioAvisosReparto servicio;
    private readonly IServiceProvider servicios;
    private readonly Func<HojaRuta, Task> abrirHoja;
    private AvisoReparto? seleccionado;
    private HojaRuta? remota;
    private bool disponible = true;
    private DateTimeOffset? ultimaConsulta;
    private string mensaje = "Los avisos te ayudan a continuar con el recorrido correcto.";
    public ObservableCollection<AvisoResumen> Avisos { get; } = [];
    public bool Disponible { get => disponible; private set { disponible = value; OnPropertyChanged(); OnPropertyChanged(nameof(Cargando)); OnPropertyChanged(nameof(TituloVacio)); } }
    public bool Cargando => !Disponible;
    public string TituloVacio => "Todo al día";
    public string Mensaje { get => mensaje; private set { mensaje = value; OnPropertyChanged(); } }
    public AvisosPage(CuentaMovil cuenta, ServicioAvisosReparto servicio, IServiceProvider servicios, Func<HojaRuta, Task> abrirHoja)
    {
        InitializeComponent(); this.cuenta = cuenta; this.servicio = servicio; this.servicios = servicios;
        this.abrirHoja = abrirHoja; BindingContext = this;
    }
    public async Task ActivarAsync(Guid? apertura = null)
    {
        await Ejecutar(async () =>
        {
            if (apertura is not null || ultimaConsulta is null || DateTimeOffset.UtcNow - ultimaConsulta > TimeSpan.FromSeconds(30))
                await Listar();
            if (apertura is { } id) await Mostrar(id);
        });
    }
    private async Task Listar()
    {
        var lista = await servicio.ListarAsync(cuenta);
        var nuevos = lista.Select(a => new AvisoResumen(a.Id, a.Tipo == "Cancelacion" ? "Entrega cancelada" : "Entrega postergada",
            a.Motivo, a.CreadoEnUtc.ToLocalTime().ToString("dd/MM HH:mm"))).ToArray();
        for (var destino = 0; destino < nuevos.Length; destino++)
        {
            if (destino < Avisos.Count && Avisos[destino] == nuevos[destino]) continue;
            var existente = -1;
            for (var i = destino + 1; i < Avisos.Count; i++)
                if (Avisos[i].Id == nuevos[destino].Id) { existente = i; break; }
            if (existente >= 0) Avisos.Move(existente, destino);
            else if (destino < Avisos.Count) Avisos[destino] = nuevos[destino];
            else Avisos.Add(nuevos[destino]);
        }
        while (Avisos.Count > nuevos.Length) Avisos.RemoveAt(Avisos.Count - 1);
        ultimaConsulta = DateTimeOffset.UtcNow;
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
    private async void Actualizar(object? sender, EventArgs e) => await Ejecutar(async () => { await Listar(); });
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
        await abrirHoja(local ?? remota);
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
