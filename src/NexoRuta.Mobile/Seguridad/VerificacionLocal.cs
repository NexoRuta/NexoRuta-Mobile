using NexoRuta.Mobile.Core.Identidad;

namespace NexoRuta.Mobile.Seguridad;

public sealed class VerificacionLocal : IVerificacionLocal
{
    // TODO: poner en false y repetir desbloqueo/rendición con biometría antes de cerrar la aceptación móvil.
    // Solo la variante de desarrollo habilita este atajo; distribución exige la verificación del teléfono.
    private static readonly bool OmitirVerificacionLocalParaDesarrollo =
#if VALIDAR_ALMACENAMIENTO
        true;
#else
        false;
#endif
    private readonly SemaphoreSlim exclusividad = new(1, 1);

    public async Task<bool> VerificarAsync(string motivo, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(motivo);
        ct.ThrowIfCancellationRequested();
        if (OmitirVerificacionLocalParaDesarrollo) return true;
        await exclusividad.WaitAsync(ct);
        try
        {
            return await MainThread.InvokeOnMainThreadAsync(async () =>
            {
#if ANDROID
                if (Platform.CurrentActivity is not { } activity) return false;
                using var cancelacion = new Android.OS.CancellationSignal();
                var resultado = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using var registro = ct.Register(() => { cancelacion.Cancel(); resultado.TrySetCanceled(ct); });
                using var builder = new Android.Hardware.Biometrics.BiometricPrompt.Builder(activity);
                builder.SetTitle("Verificar identidad");
                builder.SetDescription(motivo);
                builder.SetAllowedAuthenticators(0x000F | 0x8000); // BIOMETRIC_STRONG | DEVICE_CREDENTIAL
                using var prompt = builder.Build();
                using var callback = new RespuestaBiometrica(resultado);
                ct.ThrowIfCancellationRequested();
                prompt.Authenticate(cancelacion, activity.MainExecutor!, callback);
                return await resultado.Task;
#elif IOS || MACCATALYST
                using var contexto = new LocalAuthentication.LAContext();
                if (!contexto.CanEvaluatePolicy(LocalAuthentication.LAPolicy.DeviceOwnerAuthentication, out var error))
                {
                    error?.Dispose();
                    return false;
                }
                using var registro = ct.Register(contexto.Invalidate);
                var resultado = await contexto.EvaluatePolicyAsync(LocalAuthentication.LAPolicy.DeviceOwnerAuthentication, motivo);
                ct.ThrowIfCancellationRequested();
                resultado.Item2?.Dispose();
                return resultado.Item1;
#else
                return await Task.FromResult(false);
#endif
            });
        }
        finally { exclusividad.Release(); }
    }

#if ANDROID
    private sealed class RespuestaBiometrica(TaskCompletionSource<bool> resultado)
        : Android.Hardware.Biometrics.BiometricPrompt.AuthenticationCallback
    {
        public override void OnAuthenticationSucceeded(Android.Hardware.Biometrics.BiometricPrompt.AuthenticationResult? result)
            => resultado.TrySetResult(true);
        public override void OnAuthenticationError(Android.Hardware.Biometrics.BiometricErrorCode errorCode,
            Java.Lang.ICharSequence? errString) => resultado.TrySetResult(false);
    }
#endif
}
