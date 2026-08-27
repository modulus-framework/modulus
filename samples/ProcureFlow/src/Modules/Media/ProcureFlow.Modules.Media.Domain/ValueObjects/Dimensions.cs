namespace ModulusSample.Modules.Media.Domain.ValueObjects;

public sealed record Dimensions(int Width, int Height)
{
    public bool IsPortrait => Height > Width;
    public bool IsLandscape => Width > Height;
    public bool IsSquare => Width == Height;
    public double AspectRatio => Width == 0 ? 0 : (double)Height / Width;
}
