using System.Security.Cryptography;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Views;
namespace NexoRuta.Mobile.Captura;
public sealed class FotoPage : ContentPage
{
    private readonly CameraView camara = new();
    private readonly TaskCompletionSource<byte[]?> resultado = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource cancelacion = new();
    private byte[]? imagen;
    public Task<byte[]?> Resultado => resultado.Task;
    public FotoPage()
    {
        Title = "Foto de entrega";
        var estado = new Label { Text = "Preparando cámara…" };
        var capturar = new Button { Text = "Tomar foto", IsEnabled = false };
        var usar = new Button { Text = "Usar esta foto", IsEnabled = false };
        var vista = new Image { HeightRequest = 140, IsVisible = false, Aspect = Aspect.AspectFit };
        camara.Loaded += async (_, _) =>
        {
            try
            {
                if (await Permissions.RequestAsync<Permissions.Camera>() != PermissionStatus.Granted) { estado.Text = "Se necesita permiso de cámara para tomar la foto."; return; }
                var disponibles = await camara.GetAvailableCameras(cancelacion.Token);
                camara.SelectedCamera = disponibles.FirstOrDefault(x => x.Position == CameraPosition.Rear) ?? disponibles.FirstOrDefault();
                if (camara.SelectedCamera is null) { estado.Text = "No hay una cámara disponible."; return; }
                await camara.StartCameraPreview(cancelacion.Token);
                capturar.IsEnabled = true; estado.Text = "Enfocá la entrega. La foto se guardará protegida en la app.";
            }
            catch (Exception) { estado.Text = "No se pudo iniciar la cámara."; }
        };
        camara.MediaCaptured += async (_, e) =>
        {
            try
            {
                using var entrada = e.Media;
                var nueva = await ImagenEnMemoria.ComprimirFotoAsync(entrada, cancelacion.Token);
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (imagen is not null) CryptographicOperations.ZeroMemory(imagen);
                    imagen = nueva; vista.Source = ImageSource.FromStream(() => new MemoryStream(nueva, false));
                    vista.IsVisible = true; usar.IsEnabled = true; capturar.IsEnabled = true; estado.Text = "Revisá la imagen antes de usarla.";
                });
            }
            catch (Exception) { await MainThread.InvokeOnMainThreadAsync(() => { estado.Text = "No se pudo preparar la foto. Volvé a intentar."; capturar.IsEnabled = true; }); }
        };
        camara.MediaCaptureFailed += (_, _) => MainThread.BeginInvokeOnMainThread(() => { estado.Text = "La captura falló. Volvé a intentar."; capturar.IsEnabled = true; });
        capturar.Clicked += async (_, _) =>
        {
            capturar.IsEnabled = false;
            try { await camara.CaptureImage(cancelacion.Token); }
            catch (Exception) { estado.Text = "No se pudo tomar la foto."; capturar.IsEnabled = true; }
        };
        usar.Clicked += async (_, _) =>
        {
            if (imagen is null) return;
            var bytes = imagen; imagen = null; resultado.TrySetResult(bytes); await Navigation.PopAsync();
        };
        var grid = new Grid { Padding = 16, RowSpacing = 10, RowDefinitions = { new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto) } };
        grid.Add(camara, 0, 0); grid.Add(vista, 0, 1); grid.Add(estado, 0, 2);
        grid.Add(new VerticalStackLayout { Spacing = 8, Children = { capturar, usar } }, 0, 3); Content = grid;
    }
    protected override void OnDisappearing()
    {
        base.OnDisappearing(); cancelacion.Cancel(); camara.StopCameraPreview(); resultado.TrySetResult(null);
        if (imagen is not null) { CryptographicOperations.ZeroMemory(imagen); imagen = null; }
    }
}
