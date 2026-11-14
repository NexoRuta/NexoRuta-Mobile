using System.Diagnostics;

namespace NexoRuta.Mobile.Api.Identidad;

public static class TelemetryMovil
{
    public const string SourceName = "NexoRuta.Mobile";
    private static readonly ActivitySource Source = new(SourceName);
    private static double samplingRatio = 0.1;
    private static readonly ActivityListener Listener = new()
    {
        ShouldListenTo = source => source.Name == SourceName,
        Sample = (ref ActivityCreationOptions<ActivityContext> options) =>
        {
            if ((options.Parent.TraceFlags & ActivityTraceFlags.Recorded) != 0)
                return ActivitySamplingResult.AllDataAndRecorded;
            return Random.Shared.NextDouble() < Volatile.Read(ref samplingRatio)
                ? ActivitySamplingResult.AllDataAndRecorded
                : ActivitySamplingResult.PropagationData;
        }
    };

    static TelemetryMovil() => ActivitySource.AddActivityListener(Listener);

    public static double SamplingRatio => Volatile.Read(ref samplingRatio);

    public static void Configure(double ratio)
    {
        if (!double.IsFinite(ratio) || ratio is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(ratio), "The mobile telemetry sampling ratio must be between 0 and 1.");
        Volatile.Write(ref samplingRatio, ratio);
    }

    public static string CrearContextoOperacion()
    {
        using var activity = Source.StartActivity("mobile.operation", ActivityKind.Internal)
            ?? throw new InvalidOperationException("The mobile telemetry activity listener is not active.");
        return activity.Id ?? throw new InvalidOperationException("The mobile operation has no W3C trace context.");
    }

    public static Activity? IniciarIntento(string traceParent, string nombre, ActivityKind kind = ActivityKind.Client)
    {
        if (!ActivityContext.TryParse(traceParent, traceState: null, out var parent))
            throw new ArgumentException("A valid W3C traceparent is required.", nameof(traceParent));
        return Source.StartActivity(nombre, kind, parent);
    }

    public static Activity? IniciarEstadoSync()
        => Source.StartActivity("mobile.sync.status", ActivityKind.Client);

    public static Activity? IniciarSolicitudHttp()
        => Source.StartActivity("HTTP", ActivityKind.Client);
}
