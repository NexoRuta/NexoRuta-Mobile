using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using NexoRuta.Mobile.Api.Identidad;
using NexoRuta.Mobile.Core.Identidad;
using NexoRuta.Mobile.Core.Jornadas;
using NexoRuta.Mobile.Core.Rutas;
using NexoRuta.Mobile.Storage;
using NexoRuta.Mobile.Application;
using NexoRuta.Mobile.Ubicacion;
namespace NexoRuta.Mobile.Presentacion;

public sealed class PrincipalViewModel : INotifyPropertyChanged
{
    private readonly ClienteSesionMovil? cliente;
    private readonly RepositorioJornadasSqlite datos;
    private readonly IVerificacionLocal verificacion;
    private readonly Notificaciones.ControlNotificaciones notificaciones;
    private readonly ITrackingJornada seguimiento;
    private readonly ServicioHojasRuta? hojasServicio;
    private bool actualizando;
    public bool Actualizando { get => actualizando; private set { actualizando = value; Cambiar(); Descargar?.Actualizar(); } }
    private CuentaMovil? cuenta;
    private bool ocupado, desbloqueada;
    private string mensaje = "", email = "", password = "";
    private DateTime fecha = DateTime.Today;
    public ObservableCollection<VistaHoja> Hojas { get; } = [];
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool Ocupado { get => ocupado; private set { ocupado = value; Cambiar(); Actualizar(); } }
    public bool Desbloqueada { get => desbloqueada; private set { desbloqueada = value; Cambiar(); Actualizar(); } }
    public bool MostrarIngreso => !Desbloqueada;
    public bool PuedeOperar => Desbloqueada && !Ocupado;
    public bool MostrarTituloHoja => string.IsNullOrWhiteSpace(Mensaje);
    public bool MostrarMensajeHoja => !MostrarTituloHoja;
    public bool TieneCuenta => cuenta is not null;
    public bool ServicioConfigurado => cliente is not null;
    public string Mensaje { get => mensaje; private set { mensaje = value; Cambiar(); Cambiar(nameof(MostrarTituloHoja)); Cambiar(nameof(MostrarMensajeHoja)); } }
    public string Email { get => email; set { email = value; Cambiar(); } }
    public string Password { get => password; set { password = value; Cambiar(); } }
    public DateTime Fecha { get => fecha; set { fecha = value; Cambiar(); } }
    public ComandoAsincrono Ingresar { get; }
    public ComandoAsincrono Desbloquear { get; }
    public ComandoAsincrono Descargar { get; }
    public ComandoAsincrono ActivarAvisos { get; }
    public ComandoAsincrono Reingresar { get; }
    public ComandoAsincrono CerrarSesion { get; }
    public string EstadoAvisos => notificaciones.Estado;
    public bool PuedeActivarAvisos => notificaciones.PuedeActivar;

    public PrincipalViewModel(ConfiguracionAplicacion config, IAlmacenSesionMovil almacen,
        RepositorioJornadasSqlite datos, IVerificacionLocal verificacion, Notificaciones.ControlNotificaciones notificaciones,
        ITrackingJornada seguimiento, ClienteSesionMovil? cliente = null, ServicioSesionTrabajo? salida = null, ServicioHojasRuta? hojasServicio = null)
    {
        this.datos = datos; this.verificacion = verificacion; this.hojasServicio = hojasServicio;
        this.cliente = cliente;
        this.notificaciones = notificaciones;
        this.seguimiento = seguimiento;
        CerrarSesion = new(() => Ejecutar(async () =>
        {
            try { await salida!.CerrarAsync(Cuenta); }
            catch { notificaciones.SalidaIncompleta(); Cambiar(nameof(EstadoAvisos)); throw; }
            cuenta = null; Email = ""; Bloquear();
            try { seguimiento.Detener(); } catch { /* Sin cuenta, el callback GPS tampoco admite nuevos registros. */ }
            try { notificaciones.SesionCerrada(); } catch { /* No se restaura una sesión ya revocada si falla la limpieza nativa. */ }
            Cambiar(nameof(EstadoAvisos));
            Mensaje = "Sesión cerrada.";
            Actualizar();
        }), () => Desbloqueada && !Ocupado && salida is not null);
        Reingresar = new(() => { Bloquear(); return Task.CompletedTask; }, () => Desbloqueada && !Ocupado);
        ActivarAvisos = new(() => Ejecutar(async () =>
        {
            await notificaciones.ActivarAsync(); Cambiar(nameof(EstadoAvisos));
        }), () => Desbloqueada && ServicioConfigurado && !Ocupado);
        Ingresar = new(() => Ejecutar(async () =>
        {
            if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrEmpty(Password)) { Mensaje = "Ingresá tu correo y contraseña."; return; }
            var clave = Password; Password = "";
            cuenta = await cliente!.IngresarAsync(Email, clave);
            await cliente.ExigirPerfilAsync();
            await datos.ReanudarCuentaAsync(cuenta);
            Desbloqueada = true; await CargarLocales();
            Mensaje = "";
        }), () => cliente is not null && !Ocupado);
        Desbloquear = new(() => Ejecutar(async () =>
        {
            if (await verificacion.VerificarAsync("Abrir tu jornada de reparto"))
            {
                Desbloqueada = true; await CargarLocales(); Mensaje = "";
            }
            else Mensaje = "No se verificó la identidad. Podés volver a intentar.";
        }), () => TieneCuenta && !Ocupado);
        Descargar = new(() => ActualizarHojaAsync(informar: true), () => cliente is not null && Desbloqueada && !Ocupado && !Actualizando);
        if (cliente is null) Mensaje = "El servicio aún no está configurado. Contactá al administrador.";
    }
    public async Task InicializarAsync() => await Ejecutar(async () =>
    {
        cuenta = cliente is null ? null : await cliente.ObtenerCuentaLocalAsync();
        Actualizar();
        if (cuenta is not null) Mensaje = "";
    });
    public Task RefrescarLocalesAsync() => Desbloqueada && !Ocupado ? Ejecutar(CargarLocales) : Task.CompletedTask;
    public async Task ActualizarHojaAsync(bool informar = false)
    {
        if (!Desbloqueada || Ocupado || Actualizando || hojasServicio is null) return;
        var esperada = Cuenta; Actualizando = true;
        try
        {
            using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            if (Connectivity.Current.NetworkAccess == NetworkAccess.Internet)
                await hojasServicio.ActualizarAsync(esperada, DateOnly.FromDateTime(Fecha), limite.Token);
            else if (informar) Mensaje = "No pudimos actualizar. Tu hoja sigue disponible.";
            if (Desbloqueada && cuenta == esperada)
            {
                await CargarLocales();
                if (informar && Connectivity.Current.NetworkAccess == NetworkAccess.Internet) Mensaje = "Hoja actualizada.";
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        { if (cuenta == esperada && (informar || Hojas.Count == 0)) Mensaje = "No pudimos cargar las novedades. Reintentá en un momento."; }
        catch (SesionMovilException ex) { if (cuenta == esperada) Mensaje = ex.Message; }
        catch (Exception) { if (cuenta == esperada) Mensaje = "No pudimos actualizar tu hoja. Volvé a intentarlo."; }
        finally { Actualizando = false; }
    }
    public void Bloquear()
    {
        Desbloqueada = false; Password = ""; Hojas.Clear();
        if (cuenta is not null) Mensaje = "";
    }
    public CuentaMovil Cuenta => cuenta ?? throw new InvalidOperationException("No hay una cuenta local.");
    private async Task CargarLocales()
    {
        var esperada = Cuenta;
        var fechaEsperada = Fecha;
        var hojas = await datos.LeerHojasAsync(esperada);
        var vistas = new List<VistaHoja>();
        foreach (var original in hojas.Select(bytes => SerializadorHojaRuta.Deserializar(bytes, esperada))
            .Where(h => h.Fecha == DateOnly.FromDateTime(fechaEsperada)).OrderByDescending(h => h.Version))
        {
            var actual = await datos.LeerVistaRemotaAsync(esperada, original.Id);
            vistas.Add(VistaHoja.Crear(original, actual is null ? null : SerializadorHojaRuta.Deserializar(actual, esperada)));
        }
        if (!Desbloqueada || cuenta != esperada || Fecha != fechaEsperada) return;
        for (var destino = 0; destino < vistas.Count; destino++)
        {
            var siguiente = vistas[destino];
            if (destino < Hojas.Count && MismoContenidoVisible(Hojas[destino], siguiente)) continue;
            var existente = -1;
            for (var i = destino + 1; i < Hojas.Count; i++)
                if (Hojas[i].Original.Id == siguiente.Original.Id) { existente = i; break; }
            if (existente >= 0) Hojas.Move(existente, destino);
            else if (destino < Hojas.Count) Hojas[destino] = siguiente;
            else Hojas.Add(siguiente);
        }
        while (Hojas.Count > vistas.Count) Hojas.RemoveAt(Hojas.Count - 1);
    }
    private static bool MismoContenidoVisible(VistaHoja actual, VistaHoja siguiente) =>
        actual.Original.Id == siguiente.Original.Id && actual.Estado == siguiente.Estado &&
        actual.Fecha == siguiente.Fecha && actual.Matricula == siguiente.Matricula &&
        actual.Paradas == siguiente.Paradas && actual.Bultos == siguiente.Bultos;
    private async Task Ejecutar(Func<Task> accion)
    {
        Ocupado = true;
        try { await accion(); }
        catch (SesionMovilException ex) { Mensaje = ex.Message; }
        catch (HttpRequestException) { Mensaje = "No pudimos conectar. Podés continuar con tu hoja de ruta."; }
        catch (OperationCanceledException) { Mensaje = "La acción se interrumpió. Volvé a intentarlo."; }
        catch (InvalidOperationException ex) { Mensaje = ex.Message; }
        catch (Exception) { Mensaje = "No pudimos completar la acción. Si el problema continúa, contactá a tu operador."; }
        finally { Ocupado = false; }
    }
    private void Actualizar()
    {
        Cambiar(nameof(MostrarIngreso)); Cambiar(nameof(TieneCuenta)); Cambiar(nameof(PuedeOperar));
        Ingresar?.Actualizar(); Desbloquear?.Actualizar(); Descargar?.Actualizar(); ActivarAvisos?.Actualizar(); Reingresar?.Actualizar();
        CerrarSesion?.Actualizar();
    }
    private void Cambiar([CallerMemberName] string? nombre = null) => PropertyChanged?.Invoke(this, new(nombre));
}
