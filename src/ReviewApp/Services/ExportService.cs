using ImageReviewTool.Models;

namespace ImageReviewTool.Services;

public sealed class ExportService
{
    public Task<int> ExportAsync(IEnumerable<ReviewItem> items, string outputRoot, bool result, bool original) =>
        Task.Run(() =>
        {
            var count = 0;
            foreach (var item in items)
            {
                if (result) { Copy(item.ResultPath, "result", item, outputRoot); count++; }
                if (original && item.OriginalPath is not null) { Copy(item.OriginalPath, "original", item, outputRoot); count++; }
            }
            return count;
        });

    private static void Copy(string source, string kind, ReviewItem item, string root)
    {
        var relativeDirectory = Path.GetDirectoryName(item.RelativePath.Replace('/', Path.DirectorySeparatorChar)) ?? "";
        var destinationDirectory = Path.Combine(root, Safe(item.DetectionTag), Safe(item.ResultTag), kind, relativeDirectory);
        Directory.CreateDirectory(destinationDirectory);
        File.Copy(source, Path.Combine(destinationDirectory, Path.GetFileName(source)), true);
    }

    private static string Safe(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var result = new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrEmpty(result) ? "NULL" : result;
    }
}
