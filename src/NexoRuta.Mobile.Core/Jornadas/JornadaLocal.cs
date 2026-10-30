using System.Text.Json;

namespace NexoRuta.Mobile.Core.Jornadas;

public sealed class JornadaLocal
{
    public const int VersionEstado = 1;

    private readonly List<OperacionLocal> operaciones;
    private readonly HashSet<Guid> confirmadas;

    public Guid Id { get; }
    public Guid RutaId { get; }
    public CuentaMovil Cuenta { get; }
    public bool EstaCerrada => operaciones[^1].Tipo == TipoOperacionLocal.FinJornada;
    public bool TienePendientes => operaciones.Any(x => !confirmadas.Contains(x.Id));
    public IReadOnlyList<OperacionLocal> Operaciones => operaciones.AsReadOnly();

    private JornadaLocal(Guid id, Guid rutaId, CuentaMovil cuenta,
        IEnumerable<OperacionLocal> operaciones, IEnumerable<Guid> confirmadas)
    {
        Id = id;
        RutaId = rutaId;
        Cuenta = cuenta;
        this.operaciones = operaciones.ToList();
        this.confirmadas = confirmadas.ToHashSet();
    }

    public static JornadaLocal Iniciar(Guid id, Guid rutaId, CuentaMovil cuenta, DateTimeOffset instante)
    {
        ArgumentNullException.ThrowIfNull(cuenta);
        if (id == Guid.Empty || rutaId == Guid.Empty)
            throw new ArgumentException("La jornada y la hoja de ruta son obligatorias.");

        var inicio = new OperacionLocal(Guid.CreateVersion7(), 1, TipoOperacionLocal.InicioJornada,
            null, instante, DatosVacios());
        return new JornadaLocal(id, rutaId, cuenta, [inicio], []);
    }

    public OperacionLocal ReintentarInicio(Guid inicioRechazadoId, string resultadoOriginal, DateTimeOffset instante)
    {
        if (operaciones.Count != 1 || operaciones[0].Tipo != TipoOperacionLocal.InicioJornada
            || operaciones[0].Id != inicioRechazadoId || !confirmadas.Contains(inicioRechazadoId)
            || resultadoOriginal != "Conflicto")
            throw new InvalidOperationException("Solo se puede reintentar un inicio rechazado y confirmado, una vez.");

        return Agregar(TipoOperacionLocal.InicioJornada, null, DatosVacios(), instante);
    }

    public OperacionLocal RegistrarEntregaFisica(Guid envioId, JsonElement datos, DateTimeOffset instante)
        => Agregar(TipoOperacionLocal.EntregaFisica, envioId, datos, instante);

    public OperacionLocal AdjuntarEvidencia(Guid envioId, JsonElement datos, DateTimeOffset instante)
        => Agregar(TipoOperacionLocal.EvidenciaEntrega, envioId, datos, instante);

    public OperacionLocal RegistrarCarga(Guid envioId, JsonElement datos, DateTimeOffset instante)
        => Agregar(TipoOperacionLocal.CargarBulto, envioId, datos, instante);
    public OperacionLocal ConfirmarCarga(DateTimeOffset instante)
        => Agregar(TipoOperacionLocal.ConfirmarCarga, null, DatosVacios(), instante);
    public OperacionLocal RegistrarIntento(Guid envioId, JsonElement datos, DateTimeOffset instante)
        => Agregar(TipoOperacionLocal.IntentoFallido, envioId, datos, instante);
    public OperacionLocal DeclararRendicion(JsonElement datos, DateTimeOffset instante)
        => Agregar(TipoOperacionLocal.Rendicion, null, datos, instante);
    public OperacionLocal RegistrarPosicion(JsonElement datos, DateTimeOffset instante)
        => Agregar(TipoOperacionLocal.Posicion, null, datos, instante);

    public OperacionLocal Finalizar(DateTimeOffset instante)
        => EstaCerrada
            ? operaciones[^1]
            : Agregar(TipoOperacionLocal.FinJornada, null, DatosVacios(), instante);

    public IReadOnlyList<OperacionLocal> PrepararEnvio()
    {
        var anteriores = operaciones.Where(x => x.Tipo != TipoOperacionLocal.FinJornada
            && !confirmadas.Contains(x.Id)).ToArray();

        // El cierre no puede vencer pruebas cuyo registro/subida aún no confirmó el servidor.
        return anteriores.Length > 0
            ? anteriores
            : operaciones.Where(x => !confirmadas.Contains(x.Id)).ToArray();
    }

    public void ConfirmarPersistencia(Guid operacionId)
    {
        var operacion = operaciones.SingleOrDefault(x => x.Id == operacionId)
            ?? throw new ArgumentException("La confirmación no pertenece a esta jornada.", nameof(operacionId));

        if (operacion.Tipo == TipoOperacionLocal.FinJornada
            && operaciones.Any(x => x.Secuencia < operacion.Secuencia && !confirmadas.Contains(x.Id)))
            throw new InvalidOperationException("El cierre requiere confirmar primero sus operaciones anteriores.");

        confirmadas.Add(operacionId);
    }

    public EstadoJornadaLocal ExportarEstado()
        => new(VersionEstado, Id, RutaId, Cuenta, operaciones.ToArray(), confirmadas.ToArray());

    public static JornadaLocal Restaurar(EstadoJornadaLocal estado, CuentaMovil cuentaEsperada)
    {
        ArgumentNullException.ThrowIfNull(estado);
        ArgumentNullException.ThrowIfNull(cuentaEsperada);

        if (estado.Version != VersionEstado)
            throw new InvalidDataException("La versión del estado local no es compatible.");
        if (estado.JornadaId == Guid.Empty || estado.RutaId == Guid.Empty || estado.Cuenta != cuentaEsperada)
            throw new InvalidDataException("La jornada o su cuenta no corresponden al contexto actual.");
        if (estado.Operaciones is null || estado.Confirmadas is null || estado.Operaciones.Count == 0)
            throw new InvalidDataException("El estado local está incompleto.");

        var ids = new HashSet<Guid>();
        for (var i = 0; i < estado.Operaciones.Count; i++)
        {
            var operacion = estado.Operaciones[i];
            var esReintentoDeInicio = i == 1 && operacion?.Tipo == TipoOperacionLocal.InicioJornada
                && estado.Confirmadas.Contains(estado.Operaciones[0].Id);
            if (operacion is null || !ids.Add(operacion.Id) || operacion.Secuencia != i + 1L
                || (i == 0 && operacion.Tipo != TipoOperacionLocal.InicioJornada)
                || (i > 0 && operacion.Tipo == TipoOperacionLocal.InicioJornada && !esReintentoDeInicio)
                || (operacion.Tipo == TipoOperacionLocal.FinJornada && i != estado.Operaciones.Count - 1))
                throw new InvalidDataException("El registro local no tiene una secuencia válida.");
        }

        if (estado.Confirmadas.Distinct().Count() != estado.Confirmadas.Count
            || estado.Confirmadas.Any(x => !ids.Contains(x)))
            throw new InvalidDataException("El estado contiene confirmaciones desconocidas o repetidas.");

        var resultado = new JornadaLocal(estado.JornadaId, estado.RutaId, estado.Cuenta,
            estado.Operaciones, estado.Confirmadas);
        var cierre = estado.Operaciones[^1];
        if (cierre.Tipo == TipoOperacionLocal.FinJornada && resultado.confirmadas.Contains(cierre.Id)
            && resultado.TienePendientes)
            throw new InvalidDataException("El cierre figura confirmado antes de sus operaciones anteriores.");

        return resultado;
    }

    private OperacionLocal Agregar(TipoOperacionLocal tipo, Guid? envioId, JsonElement datos,
        DateTimeOffset instante)
    {
        if (EstaCerrada)
            throw new InvalidOperationException("La jornada está cerrada; no se pueden agregar operaciones.");

        var operacion = new OperacionLocal(Guid.CreateVersion7(), operaciones.Count + 1L,
            tipo, envioId, instante, datos);
        operaciones.Add(operacion);
        return operacion;
    }

    private static JsonElement DatosVacios()
    {
        using var documento = JsonDocument.Parse("{}");
        return documento.RootElement.Clone();
    }
}
