using ImageReviewTool.Infrastructure;

namespace ImageReviewTool.Models;

public sealed class ViewportState : ObservableObject
{
    private double _scale = 1, _offsetX, _offsetY;
    public double Scale { get => _scale; set => Set(ref _scale, Math.Clamp(value, 0.05, 40)); }
    public double OffsetX { get => _offsetX; set => Set(ref _offsetX, value); }
    public double OffsetY { get => _offsetY; set => Set(ref _offsetY, value); }
    public void Reset() { Scale = 1; OffsetX = 0; OffsetY = 0; }
}
