using ImageReviewTool.Infrastructure;

namespace ImageReviewTool.Models;

public sealed class ReviewItem : ObservableObject
{
    private string _detectionTag = "待定";
    public required string RelativePath { get; init; }
    public required string ResultPath { get; init; }
    public string? OriginalPath { get; init; }
    public string FileName => Path.GetFileName(ResultPath);
    public string DetectionTag { get => _detectionTag; set => Set(ref _detectionTag, value); }
}

public sealed class FilterOption : ObservableObject
{
    private bool _isSelected = true;
    public required string Name { get; init; }
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
}

public sealed record ReviewRecord(string RelativePath, string DetectionTag);
public sealed class ReviewDatabase
{
    public List<string> DetectionTags { get; set; } = [];
    public List<ReviewRecord> Items { get; set; } = [];
}
