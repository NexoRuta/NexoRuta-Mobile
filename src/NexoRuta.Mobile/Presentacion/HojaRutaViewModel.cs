using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using NexoRuta.Mobile.Api.Identidad;
using NexoRuta.Mobile.Api.Reparto;
using NexoRuta.Mobile.Application;
using NexoRuta.Mobile.Application.Reparto;
using NexoRuta.Mobile.Captura;
using NexoRuta.Mobile.Core.Identidad;
using NexoRuta.Mobile.Core.Jornadas;
using NexoRuta.Mobile.Core.Rutas;
using NexoRuta.Mobile.Storage;
using NexoRuta.Mobile.Ubicacion;
namespace NexoRuta.Mobile.Presentacion;

public sealed class HojaRutaViewModel : INotifyPropertyChanged
{
    private const string ConflictoInicioPorFecha = "La jornada nueva debe iniciarse con la hoja correspondiente al día del operador.";
    private readonly RepositorioJornadasSqlite datos;
    private readonly ClienteSesionMovil? cliente;
    private readonly SincronizadorJornada? sincronizador;
    private readonly ITrackingJornada seguimiento;
    private readonly ServicioAvisosReparto? avisos;
    private HojaRuta? vistaRemota;
    private readonly IInteraccionReparto interaccion;
    private readonly IVerificacionLocal verificacion;
    private Func<HojaRuta, Task>? retomarHoja;
    private HojaRuta? hojaBloqueante;
    private string mensajeJornadaBloqueante = "";
    public string EstadoMapa { get; private set; } = "La descarga offline es opcional; el mapa online requiere conexión.";
    private readonly Dictionary<Guid, ConfirmacionOperacion> confirmaciones = [];
    private bool ocupado;
    private bool sincronizando;
    private bool falloSincronizacion;
    private string mensaje = "";
    public HojaRuta Hoja { get; private set; }
    public CuentaMovil Cuenta { get; }
    public JornadaLocal? Jornada { get; private set; }
    public ObservableCollection<ParadaResumen> Paradas { get; } = [];
    public ObservableCollection<BultoCargaResumen> Carga { get; } = [];
    private int ParadasActivas => Hoja.Paradas.Count(ParadaActiva);
    private int ParadasCompletadas => Hoja.Paradas.Count(p => !ParadaActiva(p) && (vistaRemota?.Paradas.SingleOrDefault(r => r.EnvioId == p.EnvioId)?.ResultadoVisita ?? p.ResultadoVisita) == "Completada");
    private int ParadasFueraDeRuta => Hoja.Paradas.Length - ParadasActivas - ParadasCompletadas;
    private int BultosActivos => Hoja.Paradas.Where(ParadaActiva).Sum(p => p.Bultos.Length);
    private string TextoParadasActivas => ParadasActivas == 1 ? "1 parada activa" : $"{ParadasActivas} paradas activas";
    private string TextoParadasFueraDeRuta => ParadasFueraDeRuta == 1 ? "1 fuera de ruta" : $"{ParadasFueraDeRuta} fuera de ruta";
    private bool EntregaConfirmada(ParadaRuta parada) => Estado(parada) is "Entregado" or "EntregaPendienteValidacion" or "EntregaSinValidacion";
    private int Entregadas => Hoja.Paradas.Count(EntregaConfirmada);
    private int TotalEntregas => Entregadas + Hoja.Paradas.Count(p => ParadaActiva(p) && !EntregaConfirmada(p));
    public string Etapa => Cerrada ? "JORNADA FINALIZADA" : !Iniciada ? "ANTES DE SALIR" : !Salida ? "CARGA EN DEPÓSITO" : "EN REPARTO";
    public string IndicadorJornada => EstadoJornada().Fase;
    public string IndicadorSincronizacion => Sincronizando ? "Sincronizando operaciones guardadas..."
        : falloSincronizacion ? "No se pudo actualizar \u00b7 reintent\u00e1" : EstadoJornada().EstadoSincronizacion;
    public bool MostrarIndicadorSincronizacion => Sincronizando || falloSincronizacion || EstadoJornada().TieneOperacionesPendientes || EstadoJornada().TieneRechazos;
    public string Indicacion => Cerrada ? "Tu recorrido terminó. Gracias por el trabajo de hoy."
        : !Iniciada && ParadasActivas == 0 ? "No hay paradas activas para iniciar. Actualizá el recorrido o contactá al depósito."
        : !Iniciada ? "Revisá tus paradas e iniciá la jornada en el depósito."
        : !Salida ? "Escaneá los bultos asignados antes de comenzar el reparto."
        : ParadasActivas == 0 ? $"No hay entregas activas. {TextoParadasFueraDeRuta}; revisá el detalle y la custodia antes de continuar."
        : "Elegí tu próxima parada para registrar la entrega.";
    public string CantidadParadas => $"{TextoParadasActivas} · {ParadasCompletadas} completadas · {TextoParadasFueraDeRuta}";
    public string EstadoInformativo => !string.IsNullOrWhiteSpace(IncidenciasCierre) ? IncidenciasCierre
        : !Iniciada && ParadasActivas == 0 ? Indicacion
        : !string.IsNullOrWhiteSpace(Mensaje) ? Mensaje : Indicacion;
    private EvaluacionCargaSalida EvaluacionCarga => EvaluacionCargaSalida.Crear(Hoja, BultosConfirmadosPorServidor());
    public string ResumenCarga => $"{EvaluacionCarga.BultosConfirmados} de {EvaluacionCarga.BultosTotales} bultos confirmados por el servidor";
    private int BultosPendientesCarga => EvaluacionCarga.BultosPendientes;
    public string AvisoCarga => BultosPendientesCarga > 0 && Hoja.Politica.BloquearDiscrepanciasCarga
        ? $"Faltan {BultosPendientesCarga} bulto(s) por verificar en {EvaluacionCarga.ParadasAfectadas} parada(s). La política del operador bloquea la salida hasta que el servidor confirme la carga."
        : BultosPendientesCarga > 0
            ? $"Faltan {BultosPendientesCarga} bulto(s) por verificar en {EvaluacionCarga.ParadasAfectadas} parada(s). La política permite salir solo con confirmación explícita; esas paradas quedarán suspendidas y esos envíos no saldrán a reparto."
            : Carga.Any(b => b.Cargado && !b.Confirmado)
                ? "Hay escaneos locales pendientes de acuse; el servidor aún no confirma la carga." : "";
    public bool MostrarAvisoCarga => !string.IsNullOrWhiteSpace(AvisoCarga);
    private bool TieneComandoConfirmarCarga => Jornada?.Operaciones.Any(o => o.Tipo == TipoOperacionLocal.ConfirmarCarga) == true;
    public bool PuedeConfirmarCarga => MostrarCarga && !TieneComandoConfirmarCarga
        && EvaluacionCarga.PuedeIniciar(Hoja.Politica);
    public bool MostrarEstadoConfirmacionCarga => TieneComandoConfirmarCarga;
    public string EstadoConfirmacionCarga
    {
        get
        {
            var operacion = Jornada?.Operaciones.LastOrDefault(o => o.Tipo == TipoOperacionLocal.ConfirmarCarga);
            if (operacion is null) return "";
            return ResultadoSalida(operacion);
        }
    }
    public double Progreso => TotalEntregas == 0 ? 0 : (double)Entregadas / TotalEntregas;
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool Ocupado { get => ocupado; private set { ocupado = value; Actualizar(); } }
    public bool Sincronizando { get => sincronizando; private set { sincronizando = value; Actualizar(); } }
    public string Mensaje { get => mensaje; private set { mensaje = value; Cambiar(); Cambiar(nameof(EstadoInformativo)); } }
    public string MensajeJornadaBloqueante { get => mensajeJornadaBloqueante; private set { mensajeJornadaBloqueante = value; Cambiar(); Cambiar(nameof(MostrarBloqueoJornada)); } }
    public bool MostrarBloqueoJornada => !string.IsNullOrWhiteSpace(MensajeJornadaBloqueante);
    public bool PuedeRetomarJornada => hojaBloqueante is not null && retomarHoja is not null;
    public bool Iniciada => InicioActual is { } operacion && confirmaciones.TryGetValue(operacion.Id, out var inicio) && inicio.Resultado == "Aplicada";
    private OperacionLocal? InicioActual => Jornada?.Operaciones.LastOrDefault(x => x.Tipo == TipoOperacionLocal.InicioJornada);
    public bool Cerrada => (vistaRemota ?? Hoja).Estado == "Finalizada"
        || Jornada?.Operaciones.LastOrDefault(o => o.Tipo == TipoOperacionLocal.FinJornada) is { } cierre
            && confirmaciones.TryGetValue(cierre.Id, out var ackCierre) && ackCierre.Resultado == "Aplicada";
    public bool Salida => EstadoJornada().RepartoEnCurso;
    public bool MostrarInicio => !MostrarBloqueoJornada && !Iniciada && !Cerrada && VistaParada.PuedeIniciar(Hoja, vistaRemota, confirmaciones.Values);
    public bool MostrarCarga => Iniciada && !Salida && !Cerrada;
    public bool MostrarRendicion => Iniciada && !Cerrada && BultosParaRendir().Count > 0;
    public bool MostrarCierre => Iniciada && !Cerrada && Jornada?.EstaCerrada != true;
    public string IncidenciasCierre => (vistaRemota ?? Hoja).IncidenciasCierre is { Length: > 0 } incidencias
        ? "Cierre con incidencia por bultos sin entrega ni rendición: " + string.Join(", ", incidencias.Select(i => i.Codigo))
            + ". Se conserva el historial y la custodia hasta la recepción física en depósito." : "";
    public string Resumen => Iniciada || Cerrada
        ? TotalEntregas == 0 ? $"Sin entregas activas · {TextoParadasFueraDeRuta}" : $"{Entregadas} de {TotalEntregas} entregas confirmadas"
        : $"{TextoParadasActivas} · {TextoReparto.Bultos(BultosActivos)}";
    public ComandoAsincrono Iniciar { get; }
    public ComandoAsincrono Escanear { get; }
    public ComandoAsincrono ConfirmarCarga { get; }
    public ComandoAsincrono Sincronizar { get; }
    public ComandoAsincrono Rendir { get; }
    public ComandoAsincrono Finalizar { get; }
    public ComandoAsincrono RetomarJornada { get; }

    public HojaRutaViewModel(HojaRuta hoja, CuentaMovil cuenta, RepositorioJornadasSqlite datos,
        ITrackingJornada seguimiento, IInteraccionReparto interaccion, IVerificacionLocal verificacion,
        ClienteSesionMovil? cliente = null, SincronizadorJornada? sincronizador = null, ServicioAvisosReparto? avisos = null)
    {
        Hoja = hoja; Cuenta = cuenta; this.datos = datos; this.seguimiento = seguimiento;
        this.avisos = avisos; this.interaccion = interaccion; this.verificacion = verificacion; this.cliente = cliente; this.sincronizador = sincronizador;
        Iniciar = new(() => Ejecutar(IniciarJornadaAsync), () => !Ocupado && !Sincronizando && MostrarInicio && cliente is not null);
        RetomarJornada = new(async () =>
        {
            if (hojaBloqueante is { } hoja && retomarHoja is { } abrir) await abrir(hoja);
        }, () => !Ocupado && !Sincronizando && PuedeRetomarJornada);
        Escanear = new(() => Ejecutar(async () =>
        {
            var codigo = await interaccion.EscanearAsync(); if (codigo is null) return;
            var coincidencias = Hoja.Paradas.Where(p => ParadaActiva(p) && !p.EsRetiro).SelectMany(p => p.Bultos.Where(b => b.Resultado == "Pendiente").Select(b => (Parada: p, Bulto: b)))
                .Where(x => x.Bulto.Codigo == codigo || x.Bulto.Id.ToString() == codigo).ToArray();
            if (coincidencias.Length != 1) throw new InvalidOperationException("El código no identifica un único bulto asignado a esta hoja. Consultá al depósito.");
            var elegido = coincidencias[0];
            if (Cargados().Contains(elegido.Bulto.Id)) { Mensaje = "Ese bulto ya está escaneado."; return; }
            Jornada = await datos.ModificarAsync(Cuenta, Jornada!.Id, j => j.RegistrarCarga(elegido.Parada.EnvioId,
                Elemento(new() { BultoId = elegido.Bulto.Id }), DateTimeOffset.UtcNow));
            var operacion = Jornada.Operaciones[^1];
            await IntentarSincronizar(); Mensaje = ResultadoOperacion(operacion, "Bulto verificado por el servidor.",
                "Bulto registrado localmente y pendiente de sincronización.");
        }), () => !Ocupado && !Sincronizando && MostrarCarga);
        ConfirmarCarga = new(() => Ejecutar(async () =>
        {
            var evaluacion = EvaluacionCarga;
            if (!evaluacion.PuedeIniciar(Hoja.Politica))
                throw new InvalidOperationException("Faltan bultos por escanear. Resolvé las diferencias con el depósito antes de salir.");
            if (evaluacion.RequiereConfirmacionExplicita
                && !await interaccion.ConfirmarSalidaConDiferenciasAsync(evaluacion.BultosPendientes, evaluacion.ParadasAfectadas))
            {
                Mensaje = "La salida no se inició. Podés verificar los bultos pendientes y volver a intentarlo.";
                return;
            }
            Jornada = await datos.ModificarAsync(Cuenta, Jornada!.Id, j => j.ConfirmarCarga(DateTimeOffset.UtcNow));
            var operacion = Jornada.Operaciones[^1];
            await IntentarSincronizar(); Mensaje = ResultadoSalida(operacion);
        }), () => !Ocupado && !Sincronizando && PuedeConfirmarCarga);
        Sincronizar = new(ActualizarRecorridoAsync, () => !Ocupado && !Sincronizando && Jornada is not null && sincronizador is not null);
        Rendir = new(() => Ejecutar(async () =>
        {
            var seleccionados = await interaccion.SeleccionarRendicionAsync(BultosParaRendir()); if (seleccionados is null) return;
            if (!await verificacion.VerificarAsync("Confirmar la declaración de bultos devueltos al depósito")) { Mensaje = "La rendición no fue autorizada."; return; }
            Jornada = await datos.ModificarAsync(Cuenta, Jornada!.Id, j => j.DeclararRendicion(Elemento(new() { BultoIds = seleccionados }), DateTimeOffset.UtcNow));
            await IntentarSincronizar(); Mensaje = "Bultos declarados. El depósito confirmará la recepción.";
        }), () => !Ocupado && !Sincronizando && MostrarRendicion);
        Finalizar = new(() => Ejecutar(async () =>
        {
            if ((await datos.LeerCapturasAsync(Cuenta, Jornada!.Id)).Any(x => x.OperacionId is null))
                throw new InvalidOperationException("Confirmá o descartá explícitamente los borradores de evidencia antes de cerrar.");
            var pendientes = Hoja.Paradas.Any(p => !p.EsRetiro && BultosActuales(p).Any(b => b.Resultado == "EntregaPendienteValidacion"));
            var sinRendir = BultosParaRendir().Count;
            if (!await interaccion.ConfirmarCierreAsync(pendientes, sinRendir)) return;
            Jornada = await datos.ModificarAsync(Cuenta, Jornada.Id, j => j.Finalizar(DateTimeOffset.UtcNow));
            seguimiento.Detener();
            await IntentarSincronizar();
            Mensaje = ResultadoOperacion(Jornada.Operaciones.Last(o => o.Tipo == TipoOperacionLocal.FinJornada),
                "Cierre confirmado por el servidor. El retorno y la disponibilidad se confirman en Backoffice.",
                "Cierre guardado y pendiente de confirmación. Usá Actualizar para reintentar; no generes otro cierre.");
        }), () => !Ocupado && !Sincronizando && MostrarCierre);
    }
    public async Task InicializarAsync() => await Ejecutar(async () =>
    {
        // Hidratar desde SQLite primero permite abrir la ruta con contenido completo sin
        // esperar a la red ni a una consulta de mapa que no es necesaria para operar online.
        await Refrescar();
        if (Jornada is null) await BuscarJornadaBloqueanteAsync();
        if (Iniciada && !Cerrada) _ = await seguimiento.IniciarAsync();
    });

    public void ConfigurarRetomar(Func<HojaRuta, Task>? abrirHoja)
    {
        retomarHoja = abrirHoja;
        Cambiar(nameof(PuedeRetomarJornada));
        RetomarJornada.Actualizar();
    }

    private async Task IniciarJornadaAsync()
    {
        if (Jornada is null)
        {
            await BuscarJornadaBloqueanteAsync();
            if (MostrarBloqueoJornada) return;
            if (await datos.TienePendientesAsync(Cuenta))
            {
                Mensaje = "Hay trabajo local pendiente de confirmar. Retomá la hoja donde se registró y usá Actualizar antes de iniciar otra jornada.";
                return;
            }

            await cliente!.ExigirPerfilAsync();
            var (hojas, fechaServidor) = await cliente.ObtenerHojasConFechaServidorAsync(Hoja.Fecha);
            var actual = hojas.SingleOrDefault(x => x.Id == Hoja.Id);
            ExigirHojaDespachadaConParadas(actual);
            if (fechaServidor is null)
                throw new InvalidOperationException("El servidor no informó su fecha; no se creó un comando de inicio. Volvé a actualizar con conexión.");
            if (!VistaParada.EsFechaInicioPermitida(actual!, fechaServidor.Value))
                throw new InvalidOperationException($"La jornada solo puede iniciarse el {actual!.Fecha:dd/MM/yyyy}, según el día del operador. No se creó un comando local.");

            Hoja = actual!;
            await datos.GuardarHojaAsync(Cuenta, Hoja.Id, SerializadorHojaRuta.Serializar(Hoja));
            Jornada = JornadaLocal.Iniciar(Hoja.JornadaId, Hoja.Id, Cuenta, DateTimeOffset.UtcNow);
            await datos.CrearAsync(Jornada);
        }
        else if (EsReintentoDeConflictoDeFecha())
        {
            await cliente!.ExigirPerfilAsync();
            var (hojas, fechaServidor) = await cliente.ObtenerHojasConFechaServidorAsync(Hoja.Fecha);
            var actual = hojas.SingleOrDefault(x => x.Id == Hoja.Id);
            var inicioAnterior = Jornada.Operaciones[0];
            var rechazo = confirmaciones[inicioAnterior.Id];
            if (actual is null || fechaServidor is null || actual.Estado != "Despachada"
                || actual.Fecha != Hoja.Fecha || actual.JornadaId != Jornada.Id || actual.Version != rechazo.VersionRuta
                || actual.UltimaSecuencia != inicioAnterior.Secuencia
                || actual.Politica.Version != Hoja.Politica.Version
                || VistaParada.ContarActivas(actual, null, []) == 0)
                throw new InvalidOperationException("La hoja cambió desde el rechazo anterior. Actualizá el recorrido; no se creó otro comando.");
            if (!VistaParada.EsFechaInicioPermitida(actual, fechaServidor.Value))
                throw new InvalidOperationException($"La hoja corresponde al {actual.Fecha:dd/MM/yyyy}, pero todavía no es el día del operador. Se conserva el rechazo original y no se creó otro comando.");

            var abiertas = await datos.LeerJornadasAsync(Cuenta, soloAbiertas: true);
            if (await datos.TienePendientesAsync(Cuenta) || Jornada.TienePendientes
                || abiertas.Count != 1 || abiertas[0].Id != Jornada.Id)
                throw new InvalidOperationException("Hay trabajo local pendiente o una jornada anterior. Sincronizá y revisá la jornada antes de reintentar.");

            Hoja = actual;
            await datos.GuardarHojaAsync(Cuenta, Hoja.Id, SerializadorHojaRuta.Serializar(Hoja));
            Jornada = await datos.ModificarAsync(Cuenta, Jornada.Id,
                j => j.ReintentarInicio(inicioAnterior.Id, rechazo.Resultado, DateTimeOffset.UtcNow));
        }

        await sincronizador!.SincronizarAsync(Cuenta, Jornada.Id);
        await Refrescar();
        if (!Iniciada)
        {
            var operacionInicio = InicioActual;
            if (operacionInicio is not null && confirmaciones.TryGetValue(operacionInicio.Id, out var rechazo)
                && rechazo.Resultado == "Conflicto")
            {
                var detalle = string.IsNullOrWhiteSpace(rechazo.Detalle) ? "El servidor rechazó el inicio." : rechazo.Detalle;
                throw new InvalidOperationException(rechazo.Detalle == ConflictoInicioPorFecha
                    ? $"{detalle} La hoja corresponde al {Hoja.Fecha:dd/MM/yyyy}; el comando se conserva y no se enviará otra vez automáticamente."
                    : $"{detalle} El comando se conserva para revisión y no se generó otro.");
            }
            throw new InvalidOperationException("El servidor todavía no confirmó el inicio. Se conserva el mismo comando para sincronizarlo sin duplicarlo.");
        }
        Mensaje = await seguimiento.IniciarAsync() ? "Jornada iniciada. Verificá tu carga para comenzar."
            : "Jornada iniciada. Activá la ubicación para registrar el recorrido.";
    }

    private void ExigirHojaDespachadaConParadas(HojaRuta? actual)
    {
        if (actual is null || actual.Estado != "Despachada")
            throw new InvalidOperationException("La hoja ya no está disponible para iniciar. Descargá sus cambios.");
        if (VistaParada.ContarActivas(actual, null, []) == 0)
        {
            Hoja = actual;
            throw new InvalidOperationException("No hay paradas activas para iniciar. Actualizá el recorrido o contactá al depósito.");
        }
    }

    private bool EsReintentoDeConflictoDeFecha()
    {
        if (Jornada is not { Operaciones.Count: 1, TienePendientes: false } jornada
            || jornada.Operaciones[0].Tipo != TipoOperacionLocal.InicioJornada
            || !confirmaciones.TryGetValue(jornada.Operaciones[0].Id, out var rechazo)
            || rechazo.Resultado != "Conflicto" || rechazo.Detalle != ConflictoInicioPorFecha)
            return false;

        return true;
    }

    public async Task ActualizarDesdeServidorAsync() => await Ejecutar(async () =>
    {
        await ConsultarCambios();
        await Refrescar();
    });
    public Task Refrescar() => Refrescar(CancellationToken.None);
    private async Task Refrescar(CancellationToken ct)
    {
        var bytesRemotos = await datos.LeerVistaRemotaAsync(Cuenta, Hoja.Id, ct);
        vistaRemota = bytesRemotos is null ? null : SerializadorHojaRuta.Deserializar(bytesRemotos, Cuenta);
        Jornada = await datos.LeerAsync(Cuenta, Hoja.JornadaId, ct); confirmaciones.Clear();
        if (Jornada is not null)
            foreach (var bytes in await datos.LeerConfirmacionesAsync(Cuenta, Jornada.Id, ct))
            {
                var confirmada = JsonSerializer.Deserialize(bytes, ContratosRepartoJson.Default.ConfirmacionOperacion)!;
                confirmaciones[confirmada.Id] = confirmada;
            }
        Reconciliar(Paradas, Hoja.Paradas.OrderBy(x => x.Orden).Select(ResumenParada).ToArray());
        Reconciliar(Carga, Hoja.Paradas.Where(p => ParadaActiva(p) && !p.EsRetiro).SelectMany(p => p.Bultos.Where(b => b.Resultado == "Pendiente")).Select(EstadoBulto).ToArray());
        EstadoMapa = ParadasActivas == 0
            ? "No hay paradas activas; el recorrido no tiene marcadores para mostrar ni zona offline para descargar."
            : "El mapa muestra las paradas activas con coordenadas. La descarga offline es opcional.";
        Actualizar();
    }

    private async Task BuscarJornadaBloqueanteAsync()
    {
        hojaBloqueante = null;
        MensajeJornadaBloqueante = "";
        var jornadas = await datos.LeerJornadasAsync(Cuenta);
        foreach (var jornada in jornadas.OrderByDescending(x => x.Operaciones[0].OcurridaEn))
        {
            var recibos = (await datos.LeerConfirmacionesAsync(Cuenta, jornada.Id))
                .Select(bytes => JsonSerializer.Deserialize(bytes, ContratosRepartoJson.Default.ConfirmacionOperacion))
                .Where(x => x is not null).Cast<ConfirmacionOperacion>().ToArray();
            var estado = ClasificadorInicioJornada.Clasificar(jornada, recibos);
            var capturas = await datos.LeerCapturasAsync(Cuenta, jornada.Id);
            var capturaPendiente = capturas.Any(x => x.OperacionId is null);
            if (!ClasificadorInicioJornada.BloqueaOtraJornada(jornada, estado, capturaPendiente)) continue;

            var bytesHoja = await datos.LeerHojaAsync(Cuenta, jornada.RutaId);
            hojaBloqueante = bytesHoja is null ? null : SerializadorHojaRuta.Deserializar(bytesHoja, Cuenta);
            var fecha = hojaBloqueante?.Fecha.ToString("dd/MM/yyyy") ?? "fecha no disponible";
            var ruta = jornada.RutaId.ToString("D");
            MensajeJornadaBloqueante = estado switch
            {
                EstadoInicioJornadaLocal.Aplicado => $"Ya hay una jornada iniciada para el {fecha} (ruta {ruta}). Retomá esa hoja antes de iniciar otra.",
                EstadoInicioJornadaLocal.Pendiente => $"El inicio de la jornada del {fecha} (ruta {ruta}) no tiene un acuse definitivo. Retomá la hoja y usá Actualizar para recuperar el mismo comando.",
                _ when capturaPendiente => $"Hay evidencia local pendiente en la jornada del {fecha} (ruta {ruta}). Retomá esa hoja y resolvé la evidencia antes de iniciar otra.",
                _ => $"La jornada local del {fecha} (ruta {ruta}) requiere revisión. Retomá esa hoja antes de iniciar otra."
            };
            Actualizar();
            return;
        }
        Cambiar(nameof(PuedeRetomarJornada));
        Actualizar();
    }

    private static void Reconciliar<T>(ObservableCollection<T> actual, IReadOnlyList<T> deseado) where T : notnull
    {
        for (var destino = 0; destino < deseado.Count; destino++)
        {
            if (destino < actual.Count && EqualityComparer<T>.Default.Equals(actual[destino], deseado[destino])) continue;
            var existente = -1;
            for (var i = destino + 1; i < actual.Count; i++)
                if (EqualityComparer<T>.Default.Equals(actual[i], deseado[destino])) { existente = i; break; }
            if (existente >= 0) actual.Move(existente, destino);
            else if (destino < actual.Count) actual[destino] = deseado[destino];
            else actual.Add(deseado[destino]);
        }
        while (actual.Count > deseado.Count) actual.RemoveAt(actual.Count - 1);
    }
    public string EstadoPresentado(ParadaRuta parada) =>
        ConflictoRetiro(parada.EnvioId) is not null ? "Retiro rechazado · requiere resolución" :
        EntregaFisica(parada.EnvioId) && Estado(parada) is not ("Entregado" or "EntregadoParcialmente" or "EntregaPendienteValidacion" or "EntregaSinValidacion" or "Retirado" or "RetiroEnDeposito" or "EntregadoComercio")
            ? parada.EsRetiro ? "Retiro declarado · pendiente de confirmación" : "Entrega registrada · en revisión" : Estado(parada);
    public bool EntregaFisica(Guid envio) => Jornada?.Operaciones.Any(o => o.EnvioId == envio && o.Tipo is TipoOperacionLocal.EntregaFisica or TipoOperacionLocal.EntregaBultos or TipoOperacionLocal.RetirarBultos) ?? false;
    public string Estado(ParadaRuta parada) => VistaParada.Obtener(parada, vistaRemota, confirmaciones.Values).Estado;
    public OperacionLocal? ConflictoRetiro(Guid envio) => Jornada?.Operaciones.LastOrDefault(o => o.EnvioId == envio && o.Tipo == TipoOperacionLocal.RetirarBultos) is { } ultima
        && confirmaciones.TryGetValue(ultima.Id, out var ack) && ack.Resultado == "Conflicto" ? ultima : null;
    public bool OperacionRechazada(OperacionLocal operacion) => confirmaciones.TryGetValue(operacion.Id, out var ack) && ack.Resultado == "Conflicto";
    public BultoRuta[] BultosActuales(ParadaRuta parada)
    {
        var actuales = (vistaRemota?.Paradas.SingleOrDefault(p => p.EnvioId == parada.EnvioId)?.Bultos ?? parada.Bultos).ToDictionary(b => b.Id);
        foreach (var op in Jornada?.Operaciones.Where(o => o.EnvioId == parada.EnvioId && o.Tipo == TipoOperacionLocal.EntregaBultos) ?? [])
        {
            if (OperacionRechazada(op)) continue;
            var d = op.Datos.Deserialize(DatosOperacionJson.Default.DatosOperacionMovil)!;
            foreach (var r in d.ResultadosBultos ?? [])
            {
                if (!r.Entregado || !actuales.TryGetValue(r.BultoId, out var b)) continue;
                // A server projection at a newer parcel version wins over the local journal.
                if (confirmaciones.TryGetValue(op.Id, out var ack) && ack.VersionesBulto.TryGetValue(b.Id, out var version) && b.Version >= version) continue;
                if (b.Resultado is not ("Pendiente" or "EntregaPendienteValidacion")) continue;
                actuales[b.Id] = new() { Id = b.Id, Codigo = b.Codigo, PesoGramos = b.PesoGramos, VolumenCm3 = b.VolumenCm3,
                    EnEstaRuta = true, Version = b.Version, Custodia = b.Custodia, Resultado = PruebaLocalCompleta(d, parada.Reglas)
                        ? confirmaciones.ContainsKey(op.Id) ? "Entregado" : "PruebaListaPendienteSincronizacion" : "EntregaPendienteValidacion" };
            }
        }
        return actuales.Values.ToArray();
    }
    private static bool PruebaLocalCompleta(DatosOperacionMovil d, ReglasEnvio reglas) => d.Latitud is not null && d.Longitud is not null
        && d.PrecisionMetros is not null && d.PosicionEnUtc is not null && (!reglas.RequiereFoto || d.FotoId is not null)
        && (!reglas.RequiereFirma || d.FirmaId is not null) && (!reglas.RequiereNombre || !string.IsNullOrWhiteSpace(d.Nombre))
        && (!reglas.RequiereDocumento || !string.IsNullOrWhiteSpace(d.Documento));
    public bool ParadaActiva(ParadaRuta parada) => VistaParada.Obtener(parada, vistaRemota, confirmaciones.Values).Activa;
    private async Task<bool> ConsultarCambios()
    {
        if (avisos is null) return true;
        if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet) return false;
        using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try { await avisos.ActualizarRutaAsync(Cuenta, Hoja.Id, limite.Token); return true; }
        catch (Exception ex) when (ex is HttpRequestException or SesionMovilException or OperationCanceledException)
        { return false; /* The original route and queued commands remain available offline. */ }
    }
    private bool Registrada(TipoOperacionLocal tipo) => Jornada?.Operaciones.Any(o => o.Tipo == tipo
        && confirmaciones.TryGetValue(o.Id, out var r) && r.Resultado == "Aplicada") ?? false;
    private EstadoJornadaIndicador EstadoJornada() => EstadoJornadaIndicador.Crear(Jornada,
        confirmaciones.ToDictionary(x => x.Key, x => x.Value.Resultado), (vistaRemota ?? Hoja).Estado);
    public string ResultadoOperacion(OperacionLocal operacion, string aplicada, string pendiente)
    {
        if (!confirmaciones.TryGetValue(operacion.Id, out var acuse)) return pendiente;
        if (acuse.Resultado != "Aplicada") return "El servidor rechazó la operación: " + (acuse.Detalle ?? "revisá la jornada antes de volver a intentar.");
        return string.IsNullOrWhiteSpace(acuse.Detalle) ? aplicada : aplicada + " " + acuse.Detalle;
    }
    private string ResultadoSalida(OperacionLocal operacion)
    {
        if (!confirmaciones.TryGetValue(operacion.Id, out var acuse))
            return (vistaRemota ?? Hoja).Estado == "EnCurso"
                ? "El servidor muestra la ruta en curso; el acuse de esta operación todavía no llegó."
                : "Salida guardada localmente y pendiente de sincronización. No está confirmada.";
        if (acuse.Resultado != "Aplicada")
            return "El servidor rechazó la salida: " + (acuse.Detalle ?? "revisá la jornada antes de volver a intentar.");
        var escaneados = BultosConfirmadosAlSalir(operacion.Secuencia);
        var suspendidas = ParadasSuspendidasPorCarga(acuse.Detalle);
        return $"El servidor confirmó el reparto: {escaneados} de {BultosTotalesAsignados} bultos cargados; {suspendidas} paradas suspendidas."
            + (string.IsNullOrWhiteSpace(acuse.Detalle) ? "" : " Observación del servidor: " + acuse.Detalle);
    }
    private int BultosTotalesAsignados => Hoja.Paradas.Where(p => p.Activa).Sum(p => p.Bultos.Length);
    private HashSet<Guid> BultosConfirmadosPorServidor()
    {
        var confirmados = new HashSet<Guid>();
        if (Jornada is null) return confirmados;
        foreach (var op in Jornada.Operaciones)
        {
            if (!confirmaciones.TryGetValue(op.Id, out var acuse) || acuse.Resultado != "Aplicada") continue;
            if (op.Tipo == TipoOperacionLocal.CargarBulto && op.Datos.TryGetProperty("bultoId", out var bultoId)
                && bultoId.TryGetGuid(out var id)) confirmados.Add(id);
            if (op.Tipo == TipoOperacionLocal.Rendicion && op.Datos.TryGetProperty("bultoIds", out var ids))
                foreach (var value in ids.EnumerateArray()) if (value.TryGetGuid(out var rendido)) confirmados.Remove(rendido);
        }
        return confirmados;
    }
    private int BultosConfirmadosAlSalir(long secuencia)
    {
        var confirmados = new HashSet<Guid>();
        if (Jornada is null) return 0;
        foreach (var op in Jornada.Operaciones.Where(o => o.Secuencia <= secuencia))
        {
            if (!confirmaciones.TryGetValue(op.Id, out var acuse) || acuse.Resultado != "Aplicada") continue;
            if (op.Tipo == TipoOperacionLocal.CargarBulto && op.Datos.TryGetProperty("bultoId", out var bultoId)
                && bultoId.TryGetGuid(out var id)) confirmados.Add(id);
            if (op.Tipo == TipoOperacionLocal.Rendicion && op.Datos.TryGetProperty("bultoIds", out var ids))
                foreach (var value in ids.EnumerateArray()) if (value.TryGetGuid(out var rendido)) confirmados.Remove(rendido);
        }
        return confirmados.Count;
    }
    private int ParadasSuspendidasPorCarga(string? detalle)
    {
        if (detalle is not null)
        {
            var match = System.Text.RegularExpressions.Regex.Match(detalle,
                @"Salida con (\d+) envío\(s\) suspendidos por diferencias de carga\.");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var cantidad)) return cantidad;
        }
        var ids = Hoja.Paradas.Where(p => p.Activa).Select(p => p.EnvioId).ToHashSet();
        return vistaRemota?.Paradas.Count(p => ids.Contains(p.EnvioId) && !p.Activa
            && p.MotivoSuspension?.StartsWith("Carga incompleta:", StringComparison.Ordinal) == true) ?? 0;
    }
    private ParadaResumen ResumenParada(ParadaRuta parada)
    {
        var vista = VistaParada.Obtener(parada, vistaRemota, confirmaciones.Values);
        var real = vistaRemota?.Paradas.SingleOrDefault(p => p.EnvioId == parada.EnvioId && p.EnvioVersion >= vista.Version) ?? parada;
        var estadoRuta = real.ResultadoVisita == "Completada" ? "Visita completada" : real.ResultadoVisita == "Retirada" ? "Retirada de esta ruta · " + vista.MotivoSuspension : real.ResultadoVisita == "CerradaSinResultado" ? "Visita cerrada sin resultado acreditado" : vista.Activa ? "Parada activa"
            : "Fuera de esta ruta" + (string.IsNullOrWhiteSpace(vista.MotivoSuspension) ? " · motivo no informado" : " · " + vista.MotivoSuspension);
        return new(parada.EnvioId, parada.Orden, parada.Destinatario, parada.Direccion, EstadoPresentado(parada),
            TextoReparto.Bultos(real.Bultos.Length), estadoRuta, Custodia(real.Bultos));
    }
    private static string Custodia(IReadOnlyCollection<BultoRuta> bultos)
    {
        var custodias = bultos.Select(b => b.Custodia).Distinct(StringComparer.Ordinal).ToArray();
        if (custodias.Length == 0) return "Custodia no informada";
        if (custodias.Length > 1) return "Custodia mixta";
        return custodias[0] switch
        {
            "Deposito" => "Custodia: depósito",
            "Repartidor" => "Custodia: repartidor",
            "PendienteDeposito" => "Custodia: recepción pendiente en depósito",
            var otra => "Custodia: " + otra
        };
    }
    private BultoCargaResumen EstadoBulto(BultoRuta bulto)
    {
        var cargado = false; var confirmado = false; var rechazado = false;
        if (Jornada is null) return new(bulto.Codigo, false, false, false);
        foreach (var operacion in Jornada.Operaciones)
        {
            if (operacion.Tipo == TipoOperacionLocal.CargarBulto
                && operacion.Datos.TryGetProperty("bultoId", out var id) && id.TryGetGuid(out var bultoId) && bultoId == bulto.Id)
            {
                if (!confirmaciones.TryGetValue(operacion.Id, out var ack)) { cargado = true; confirmado = false; rechazado = false; }
                else if (ack.Resultado == "Aplicada") { cargado = true; confirmado = true; rechazado = false; }
                else { cargado = false; confirmado = false; rechazado = true; }
            }
            if (operacion.Tipo == TipoOperacionLocal.Rendicion
                && operacion.Datos.TryGetProperty("bultoIds", out var ids)
                && ids.EnumerateArray().Any(x => x.TryGetGuid(out var id) && id == bulto.Id))
            {
                if (!confirmaciones.TryGetValue(operacion.Id, out var ack)) { cargado = false; confirmado = false; }
                else if (ack.Resultado == "Aplicada") { cargado = false; confirmado = false; rechazado = false; }
            }
        }
        return new(bulto.Codigo, cargado, confirmado, rechazado);
    }
    private HashSet<Guid> Cargados()
    {
        var cargados = new HashSet<Guid>();
        if (Jornada is null) return cargados;
        foreach (var op in Jornada.Operaciones)
        {
            if (confirmaciones.TryGetValue(op.Id, out var r) && r.Resultado != "Aplicada") continue;
            if (op.Tipo == TipoOperacionLocal.CargarBulto && op.Datos.TryGetProperty("bultoId", out var bulto) && bulto.TryGetGuid(out var id)) cargados.Add(id);
            if (op.Tipo == TipoOperacionLocal.Rendicion && op.Datos.TryGetProperty("bultoIds", out var ids))
                foreach (var valor in ids.EnumerateArray()) cargados.Remove(valor.GetGuid());
        }
        return cargados;
    }
    public IReadOnlyList<BultoRuta> BultosParaRendir()
    {
        var cargados = Cargados();
        if (Jornada is not null) foreach (var op in Jornada.Operaciones)
        {
            if (OperacionRechazada(op)) continue;
            if (op.Tipo == TipoOperacionLocal.RetirarBultos && (!confirmaciones.TryGetValue(op.Id, out var ack) || ack.Resultado != "Aplicada")) continue;
            if (op.Tipo == TipoOperacionLocal.EntregaFisica)
                foreach (var b in Hoja.Paradas.Single(p => p.EnvioId == op.EnvioId).Bultos) cargados.Remove(b.Id);
            if (op.Tipo is TipoOperacionLocal.EntregaBultos or TipoOperacionLocal.RetirarBultos)
                foreach (var b in op.Datos.Deserialize(DatosOperacionJson.Default.DatosOperacionMovil)!.ResultadosBultos ?? [])
                    if (b.Entregado) { if (op.Tipo == TipoOperacionLocal.RetirarBultos) cargados.Add(b.BultoId); else cargados.Remove(b.BultoId); }
        }
        if (Jornada is not null)
            foreach (var op in Jornada.Operaciones.Where(o => o.Tipo == TipoOperacionLocal.Rendicion
                && (!confirmaciones.TryGetValue(o.Id, out var r) || r.Resultado == "Aplicada")))
                if (op.Datos.TryGetProperty("bultoIds", out var ids)) foreach (var id in ids.EnumerateArray()) cargados.Remove(id.GetGuid());
        return Hoja.Paradas.SelectMany(x => x.Bultos).Where(x => cargados.Contains(x.Id)).ToArray();
    }
    private async Task ActualizarRecorridoAsync()
    {
        if (Ocupado || Sincronizando || Jornada is null || sincronizador is null) return;
        Sincronizando = true;
        try
        {
            if (await IntentarSincronizar())
                Mensaje = Jornada!.TienePendientes
                    ? $"Quedan {Jornada.CantidadPendientes} operaciones por confirmar. Us\u00e1 Actualizar para continuar."
                    : EstadoJornada().TieneRechazos ? "Hay operaciones rechazadas por el servidor. Revis\u00e1 el detalle antes de continuar."
                    : Cerrada ? "Cierre confirmado por el servidor." : "Recorrido actualizado.";
        }
        catch (Exception) { falloSincronizacion = true; Mensaje = "No pudimos actualizar. Conservamos tus datos. Reintent\u00e1 con Actualizar."; }
        finally { Sincronizando = false; }
    }
    public async Task<bool> IntentarSincronizar()
    {
        if (sincronizador is null || Jornada is null) return false;
        falloSincronizacion = false;
        // Match the existing reconnect budget; includes waiting for the shared sync semaphore.
        using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { await sincronizador.SincronizarAsync(Cuenta, Jornada.Id, limite.Token); }
        catch (OperationCanceledException) { falloSincronizacion = true; Mensaje = "Se agot\u00f3 el tiempo de actualizaci\u00f3n. Tus operaciones siguen guardadas. Reintent\u00e1 con Actualizar."; }
        catch (SesionMovilException ex) { falloSincronizacion = true; Mensaje = ex.Message + " Las operaciones pendientes siguen guardadas."; }
        catch (HttpRequestException) { falloSincronizacion = true; Mensaje = "No pudimos conectar. Tus operaciones siguen guardadas. Reintent\u00e1 con Actualizar."; }
        if (!await ConsultarCambios())
        {
            if (!falloSincronizacion) Mensaje = "No pudimos consultar los cambios del servidor. Conservamos el último estado confirmado. Reintentá con Actualizar.";
            falloSincronizacion = true;
        }
        using var lectura = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Refrescar(lectura.Token);
        return !falloSincronizacion;
    }
    public async Task Ejecutar(Func<Task> operacion)
    {
        if (Ocupado || Sincronizando) return; Ocupado = true;
        try { await operacion(); }
        catch (InvalidOperationException ex) { Mensaje = ex.Message; }
        catch (SesionMovilException ex) { Mensaje = ex.Message; }
        catch (HttpRequestException) { Mensaje = "No pudimos actualizar. Podés continuar con tu recorrido."; }
        catch (Exception) { Mensaje = "No pudimos completar la acción. Volvé a intentarlo."; }
        finally { await RefrescarSeguro(); Ocupado = false; }
    }
    private async Task RefrescarSeguro()
    {
        using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await Refrescar(limite.Token); }
        catch (Exception) { Mensaje = "No pudimos cargar tu recorrido. Volvé a intentarlo."; }
    }
    public static JsonElement Elemento(DatosOperacionMovil datos) => JsonSerializer.SerializeToElement(datos, DatosOperacionJson.Default.DatosOperacionMovil);
    private void Actualizar()
    {
        foreach (var propiedad in new[] { nameof(Hoja), nameof(Jornada), nameof(Ocupado), nameof(Sincronizando), nameof(Iniciada), nameof(Cerrada), nameof(Salida), nameof(MostrarInicio), nameof(MostrarBloqueoJornada), nameof(MensajeJornadaBloqueante), nameof(PuedeRetomarJornada), nameof(MostrarCarga), nameof(MostrarCierre), nameof(MostrarRendicion), nameof(Resumen), nameof(Etapa), nameof(Indicacion), nameof(EstadoInformativo), nameof(CantidadParadas), nameof(ResumenCarga), nameof(AvisoCarga), nameof(MostrarAvisoCarga), nameof(PuedeConfirmarCarga), nameof(MostrarEstadoConfirmacionCarga), nameof(EstadoConfirmacionCarga), nameof(IndicadorJornada), nameof(IndicadorSincronizacion), nameof(MostrarIndicadorSincronizacion), nameof(EstadoMapa), nameof(Progreso), nameof(IncidenciasCierre) }) Cambiar(propiedad);
        Iniciar?.Actualizar(); RetomarJornada?.Actualizar(); Escanear?.Actualizar(); ConfirmarCarga?.Actualizar(); Sincronizar?.Actualizar(); Rendir?.Actualizar(); Finalizar?.Actualizar();
    }
    private void Cambiar([CallerMemberName] string? nombre = null) => PropertyChanged?.Invoke(this, new(nombre));
}
