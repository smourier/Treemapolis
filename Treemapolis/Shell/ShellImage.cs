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

    public unsafe IComObject<ID2D1Bitmap> CreateBitmap(IComObject<ID2D1DeviceContext> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var properties = new D2D1_BITMAP_PROPERTIES1
        {
            pixelFormat = new D2D1_PIXEL_FORMAT { format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, alphaMode = D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED },
        };

        fixed (byte* pixels = Pixels)
        {
            return context.CreateBitmap(new D2D_SIZE_U((uint)Width, (uint)Height), (nint)pixels, (uint)(Width * BytesPerPixel), properties);
        }
    }
}
