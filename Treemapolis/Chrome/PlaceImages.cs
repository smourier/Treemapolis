namespace Treemapolis.Chrome;

// the pictures of the island's places as Direct2D bitmaps, made once per place the first time a tile shows it.
public sealed class PlaceImages : IDisposable
{
    private readonly Dictionary<Place, IComObject<ID2D1Bitmap>> _bitmaps = [];

    public IComObject<ID2D1Bitmap>? Get(IComObject<ID2D1DeviceContext> context, Place place)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(place);
        var image = place.Image;
        if (image == null)
            return null;

        if (_bitmaps.TryGetValue(place, out var bitmap))
            return bitmap;

        bitmap = image.CreateBitmap(context);
        _bitmaps[place] = bitmap;
        return bitmap;
    }

    // places read again are other objects, so what was made for the previous ones goes.
    public void Clear()
    {
        foreach (var bitmap in _bitmaps.Values)
        {
            bitmap.Dispose();
        }
        _bitmaps.Clear();
    }

    public void Dispose() => Clear();
}
