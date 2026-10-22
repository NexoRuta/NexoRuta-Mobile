namespace NexoRuta.Mobile.Captura;
public sealed class FirmaPage : ContentPage
{
    private readonly Trazado trazado = new();
    private readonly GraphicsView vista;
    private readonly TaskCompletionSource<byte[]?> resultado = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<byte[]?> Resultado => resultado.Task;
    public FirmaPage()
    {
        Title = "Firma del receptor";
        vista = new GraphicsView { Drawable = trazado, BackgroundColor = Colors.White, MinimumHeightRequest = 240 };
        var estado = new Label { Text = "Pedí al receptor que firme dentro del recuadro." };
        vista.StartInteraction += (_, e) =>
        {
            if (trazado.Lineas.Count >= 128) return;
            trazado.Lineas.Add([]); Agregar(e.Touches);
        };
        vista.DragInteraction += (_, e) => Agregar(e.Touches);
        var borrar = new Button { Text = "Borrar y volver a firmar" };
        borrar.Clicked += (_, _) => { trazado.Lineas.Clear(); vista.Invalidate(); };
        var usar = new Button { Text = "Usar esta firma" };
        usar.Clicked += async (_, _) =>
        {
            if (trazado.Lineas.Sum(x => x.Count) < 4) { estado.Text = "La firma está vacía o incompleta."; return; }
            try { resultado.TrySetResult(Exportar()); await Navigation.PopAsync(); }
            catch (Exception) { estado.Text = "No se pudo preparar la firma. Volvé a intentar."; }
        };
        var grid = new Grid { Padding = 16, RowSpacing = 12, RowDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto) } };
        grid.Add(estado, 0, 0); grid.Add(new Border { Content = vista, Stroke = Colors.Gray }, 0, 1);
        grid.Add(borrar, 0, 2); grid.Add(usar, 0, 3); Content = grid;
    }
    private void Agregar(PointF[] puntos)
    {
        if (trazado.Lineas.Count == 0 || vista.Width <= 0 || vista.Height <= 0 || trazado.Lineas.Sum(x => x.Count) > 20000) return;
        foreach (var p in puntos.Take(1))
            trazado.Lineas[^1].Add(new((float)Math.Clamp(p.X / vista.Width, 0, 1), (float)Math.Clamp(p.Y / vista.Height, 0, 1)));
        vista.Invalidate();
    }
    private byte[] Exportar()
    {
#if ANDROID
        using var imagen = Android.Graphics.Bitmap.CreateBitmap(800, 320, Android.Graphics.Bitmap.Config.Argb8888!)!;
        using var canvas = new Android.Graphics.Canvas(imagen); canvas.DrawColor(Android.Graphics.Color.White);
        using var pincel = new Android.Graphics.Paint(Android.Graphics.PaintFlags.AntiAlias) { Color = Android.Graphics.Color.Black, StrokeWidth = 3 };
        pincel.SetStyle(Android.Graphics.Paint.Style.Stroke);
        foreach (var linea in trazado.Lineas.Where(x => x.Count > 1))
        {
            using var path = new Android.Graphics.Path(); path.MoveTo(linea[0].X * 800, linea[0].Y * 320);
            foreach (var p in linea.Skip(1)) path.LineTo(p.X * 800, p.Y * 320);
            canvas.DrawPath(path, pincel);
        }
        using var salida = new MemoryStream(); imagen.Compress(Android.Graphics.Bitmap.CompressFormat.Png!, 100, salida); return salida.ToArray();
#elif IOS || MACCATALYST
        UIKit.UIGraphics.BeginImageContextWithOptions(new CoreGraphics.CGSize(800, 320), true, 1);
        try
        {
            UIKit.UIColor.White.SetFill(); UIKit.UIGraphics.RectFill(new CoreGraphics.CGRect(0, 0, 800, 320));
            UIKit.UIColor.Black.SetStroke();
            foreach (var linea in trazado.Lineas.Where(x => x.Count > 1))
            {
                using var path = new UIKit.UIBezierPath { LineWidth = 3 };
                path.MoveTo(new(linea[0].X * 800, linea[0].Y * 320));
                foreach (var p in linea.Skip(1)) path.AddLineTo(new(p.X * 800, p.Y * 320));
                path.Stroke();
            }
            using var imagen = UIKit.UIGraphics.GetImageFromCurrentImageContext();
            using var png = imagen.AsPNG() ?? throw new IOException("No se pudo preparar la firma.");
            return png.ToArray();
        }
        finally { UIKit.UIGraphics.EndImageContext(); }
#else
        throw new PlatformNotSupportedException();
#endif
    }
    protected override void OnDisappearing() { base.OnDisappearing(); resultado.TrySetResult(null); trazado.Lineas.Clear(); }
    private sealed class Trazado : IDrawable
    {
        public List<List<PointF>> Lineas { get; } = [];
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            canvas.FillColor = Colors.White; canvas.FillRectangle(dirtyRect); canvas.StrokeColor = Colors.Black; canvas.StrokeSize = 3;
            foreach (var linea in Lineas.Where(x => x.Count > 1))
            {
                using var path = new PathF(); path.MoveTo(linea[0].X * dirtyRect.Width, linea[0].Y * dirtyRect.Height);
                foreach (var p in linea.Skip(1)) path.LineTo(p.X * dirtyRect.Width, p.Y * dirtyRect.Height);
                canvas.DrawPath(path);
            }
        }
    }
}
