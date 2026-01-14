namespace FinalFantasy16Library.Imaging.Interfaces;

public interface ImageEncoder
{
    uint BitsPerPixel { get; }
    uint BytesPerPixel => (BitsPerPixel + 7) / 8;
    byte[] Decode(byte[] data, uint width, uint height);

    byte[] Encode(byte[] data, uint width, uint height);
    uint CalculateSize(int width, int height);
}
