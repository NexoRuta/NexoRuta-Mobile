namespace NexoRuta.Mobile.Captura;
public static class ImagenEnMemoria
{
    public static async Task<byte[]> ComprimirFotoAsync(Stream entrada, CancellationToken ct)
    {
        using var memoria = new MemoryStream();
        var bloque = new byte[81920];
        while (true)
        {
            var leidos = await entrada.ReadAsync(bloque, ct); if (leidos == 0) break;
            if (memoria.Length + leidos > 20 * 1024 * 1024) throw new IOException("La captura excede el tamaño admitido.");
            await memoria.WriteAsync(bloque.AsMemory(0, leidos), ct);
        }
        memoria.Position = 0;
#if ANDROID
        using var dimensiones = new Android.Graphics.BitmapFactory.Options { InJustDecodeBounds = true };
        _ = await Android.Graphics.BitmapFactory.DecodeStreamAsync(memoria, null, dimensiones);
        if (dimensiones.OutWidth <= 0 || dimensiones.OutHeight <= 0) throw new IOException("La captura no es una imagen válida.");
        var muestra = 1;
        while (Math.Max(dimensiones.OutWidth, dimensiones.OutHeight) / muestra > 3200) muestra *= 2;
        memoria.Position = 0;
        using var opciones = new Android.Graphics.BitmapFactory.Options { InSampleSize = muestra };
        using var original = await Android.Graphics.BitmapFactory.DecodeStreamAsync(memoria, null, opciones) ?? throw new IOException("No se pudo leer la captura.");
        memoria.Position = 0;
        using var exif = new AndroidX.ExifInterface.Media.ExifInterface(memoria);
        var orientacion = exif.GetAttributeInt("Orientation", 1);
        using var matriz = new Android.Graphics.Matrix();
        switch (orientacion)
        {
            case 2: matriz.PostScale(-1, 1); break;
            case 3: matriz.PostRotate(180); break;
            case 4: matriz.PostScale(1, -1); break;
            case 5: matriz.PostRotate(90); matriz.PostScale(-1, 1); break;
            case 6: matriz.PostRotate(90); break;
            case 7: matriz.PostRotate(270); matriz.PostScale(-1, 1); break;
            case 8: matriz.PostRotate(270); break;
        }
        using var orientada = Android.Graphics.Bitmap.CreateBitmap(original, 0, 0, original.Width, original.Height, matriz, true)!;
        var escala = Math.Min(1d, 1600d / Math.Max(orientada.Width, orientada.Height));
        using var reducida = Android.Graphics.Bitmap.CreateScaledBitmap(orientada, Math.Max(1, (int)(orientada.Width * escala)), Math.Max(1, (int)(orientada.Height * escala)), true)!;
        using var salida = new MemoryStream();
        await reducida.CompressAsync(Android.Graphics.Bitmap.CompressFormat.Jpeg!, 75, salida);
        var bytes = salida.ToArray();
#elif IOS || MACCATALYST
        using var datos = Foundation.NSData.FromArray(memoria.ToArray());
        using var imagen = UIKit.UIImage.LoadFromData(datos) ?? throw new IOException("No se pudo leer la captura.");
        var escala = Math.Min(1d, 1600d / Math.Max(imagen.Size.Width, imagen.Size.Height));
        var tamano = new CoreGraphics.CGSize(imagen.Size.Width * escala, imagen.Size.Height * escala);
        UIKit.UIGraphics.BeginImageContextWithOptions(tamano, true, 1);
        byte[] bytes;
        try
        {
            imagen.Draw(new CoreGraphics.CGRect(CoreGraphics.CGPoint.Empty, tamano));
            using var reducida = UIKit.UIGraphics.GetImageFromCurrentImageContext();
            using var jpeg = reducida.AsJPEG(0.75f) ?? throw new IOException("No se pudo comprimir la captura.");
            bytes = jpeg.ToArray();
        }
        finally { UIKit.UIGraphics.EndImageContext(); }
#else
        throw new PlatformNotSupportedException();
#endif
        if (bytes.Length > 2097152) throw new IOException("La imagen comprimida excede 2 MiB.");
        return bytes;
    }
}
