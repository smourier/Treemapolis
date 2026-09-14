namespace Treemapolis.Shell;

// a picture the shell gave, copied out as premultiplied BGRA rows, so it can leave the thread that asked for it.
public sealed class ShellImage
{
    public const int BytesPerPixel = 4;

    public required int Width { get; init; }
    public required int Height { get; init; }
    public required byte[] Pixels { get; init; }

    public static unsafe ShellImage FromBitmap(IComObject<IWICBitmap> bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        var size = bitmap.GetSizeU();
        using var converter = WicImagingFactory.CreateFormatConverter();
        converter.Object.Initialize(bitmap.Object, DirectN.Constants.GUID_WICPixelFormat32bppPBGRA, WICBitmapDitherType.WICBitmapDitherTypeNone, null!, 0, WICBitmapPaletteType.WICBitmapPaletteTypeCustom).ThrowOnError();

        var pixels = new byte[size.width * size.height * BytesPerPixel];
        fixed (byte* pointer = pixels)
        {
            converter.Object.CopyPixels(0, size.width * BytesPerPixel, (uint)pixels.Length, (nint)pointer).ThrowOnError();
        }
        return new ShellImage { Width = (int)size.width, Height = (int)size.height, Pixels = pixels };
    }
}
