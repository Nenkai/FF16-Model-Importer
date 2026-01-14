namespace FinalFantasy16Library.Imaging.Interfaces
{
    public interface ImageBlockFormat
    {
        uint BlockWidth { get; } 
        uint BlockHeight { get; }
        uint BlockDepth { get; }
    }
}
