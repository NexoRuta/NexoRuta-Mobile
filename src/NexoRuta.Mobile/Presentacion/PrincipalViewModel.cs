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
    private CuentaMovil? cuenta;
    private bool ocupado, desbloqueada;
    private string mensaje = "", email = "", password = "";
    private DateTime fecha = DateTime.Today;
    public ObservableCollection<HojaRuta> Hojas { get; } = [];
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool Ocupado { get => ocupado; private set { ocupado = value; Cambiar(); Actualizar(); } }
    public bool Desbloqueada { get => desbloqueada; private set { desbloqueada = value; Cambiar(); Actualizar(); } }
    public bool MostrarIngreso => !Desbloqueada;
    public bool PuedeOperar => Desbloqueada && !Ocupado;
    public bool TieneCuenta => cuenta is not null;
    public bool ServicioConfigurado => cliente is not null;
    public string Mensaje { get => mensaje; private set { mensaje = value; Cambiar(); } }
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

    public PrincipalViewModel(ConfiguracionAplicacion config, IAlmacenSesionMovil almacen,
        RepositorioJornadasSqlite datos, IVerificacionLocal verificacion, Notificaciones.ControlNotificaciones notificaciones,
        ITrackingJornada seguimiento, ClienteSesionMovil? cliente = null, ServicioSesionTrabajo? salida = null)
    {
        this.datos = datos; this.verificacion = verificacion;
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
            Mensaje = "Sesión cerrada. Tu trabajo cifrado se conserva; podés ingresar con otra cuenta.";
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
            Mensaje = "Sesión iniciada. Descargá tu hoja antes de salir del depósito.";
        }), () => cliente is not null && !Ocupado);
        Desbloquear = new(() => Ejecutar(async () =>
        {
            if (await verificacion.VerificarAsync("Desbloquear tu jornada de reparto"))
            {
                Desbloqueada = true; await CargarLocales(); Mensaje = "Hoja local disponible. Los cambios se sincronizarán al recuperar conexión.";
            }
            else Mensaje = "No se verificó la identidad. Podés volver a intentar.";
        }), () => TieneCuenta && !Ocupado);
        Descargar = new(() => Ejecutar(async () =>
        {
            await cliente!.ExigirPerfilAsync();
            var hojas = await cliente.ObtenerHojasAsync(DateOnly.FromDateTime(Fecha));
            foreach (var hoja in hojas)
            {
                var bytes = SerializadorHojaRuta.Serializar(hoja);
                if (await datos.LeerAsync(Cuenta, hoja.JornadaId) is null)
                    await datos.GuardarHojaAsync(Cuenta, hoja.Id, bytes);
                await datos.GuardarVistaRemotaAsync(Cuenta, hoja.Id, hoja.Version, bytes);
            }
            await CargarLocales(); Mensaje = hojas.Length == 0 ? "No hay rutas despachadas para esa fecha." : "Hoja guardada de forma segura en este teléfono.";
        }), () => cliente is not null && Desbloqueada && !Ocupado);
        if (cliente is null) Mensaje = "El servicio aún no está configurado. Contactá al administrador.";
    }
    public async Task InicializarAsync() => await Ejecutar(async () =>
    {
        cuenta = cliente is null ? null : await cliente.ObtenerCuentaLocalAsync();
        Actualizar();
        if (cuenta is not null) Mensaje = "Verificá tu identidad para abrir la jornada guardada.";
    });
    public void Bloquear()
    {
        Desbloqueada = false; Password = ""; Hojas.Clear();
        if (cuenta is not null) Mensaje = "La sesión local está bloqueada.";
    }
    public CuentaMovil Cuenta => cuenta ?? throw new InvalidOperationException("No hay una cuenta local.");
    private async Task CargarLocales()
    {
        var hojas = await datos.LeerHojasAsync(Cuenta);
        Hojas.Clear(); foreach (var bytes in hojas) Hojas.Add(SerializadorHojaRuta.Deserializar(bytes, Cuenta));
    }
    private async Task Ejecutar(Func<Task> accion)
    {
        Ocupado = true;
        try { await accion(); }
        catch (SesionMovilException ex) { Mensaje = ex.Message; }
        catch (HttpRequestException) { Mensaje = "No hay conexión con el servicio. Tu trabajo local se conserva."; }
        catch (OperationCanceledException) { Mensaje = "La operación se interrumpió. Tu trabajo local se conserva."; }
        catch (InvalidOperationException ex) { Mensaje = ex.Message; }
        catch (Exception) { Mensaje = "No se pudo completar la operación. Los datos existentes se conservan; contactá al administrador si persiste."; }
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
