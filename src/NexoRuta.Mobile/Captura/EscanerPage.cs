using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;
namespace NexoRuta.Mobile.Captura;
public sealed class EscanerPage : ContentPage
{
    private readonly CameraBarcodeReaderView lector = new() { IsDetecting = false, Options = new() { Formats = BarcodeFormats.All, AutoRotate = true, Multiple = false } };
    private readonly TaskCompletionSource<string?> resultado = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<string?> Resultado => resultado.Task;
    public EscanerPage()
    {
        Title = "Escanear bulto";
        var mensaje = new Label { Text = "Apuntá al QR o código de barras del bulto.", Margin = 16 };
        lector.BarcodesDetected += (_, e) => MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (resultado.Task.IsCompleted) return;
            var codigo = e.Results.FirstOrDefault()?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(codigo) || codigo.Length > 80) { mensaje.Text = "Ese código no corresponde a un bulto de NexoRuta."; return; }
            lector.IsDetecting = false; resultado.TrySetResult(codigo); await Navigation.PopAsync();
        });
        lector.Loaded += async (_, _) =>
        {
            if (await Permissions.RequestAsync<Permissions.Camera>() == PermissionStatus.Granted) lector.IsDetecting = true;
            else mensaje.Text = "Se necesita permiso de cámara para escanear los bultos.";
        };
        var grid = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Star) } };
        grid.Add(mensaje, 0, 0); grid.Add(lector, 0, 1); Content = grid;
    }
    protected override void OnDisappearing() { base.OnDisappearing(); lector.IsDetecting = false; resultado.TrySetResult(null); }
}
