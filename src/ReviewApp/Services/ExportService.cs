using ImageReviewTool.Models;

namespace ImageReviewTool.Services;

public sealed record ExportProgress(int Completed, int Total, string FileName);

public sealed class ExportFailedException(string sourcePath, int completed, int total, Exception innerException)
    : IOException($"复制文件失败：{sourcePath}", innerException)
{
    public string SourcePath { get; } = sourcePath;
    public int Completed { get; } = completed;
    public int Total { get; } = total;
}

public sealed class ExportService
{
    public Task<int> ExportAsync(IEnumerable<ReviewItem> items, string outputRoot, bool result, bool original,
        IProgress<ExportProgress>? progress = null) =>
        Task.Run(() =>
        {
            var planned = items.ToList();
            var total = planned.Sum(item => (result ? 1 : 0) + (original && item.OriginalPath is not null ? 1 : 0));
            var count = 0;
            progress?.Report(new ExportProgress(0, total, ""));
            foreach (var item in planned)
            {
                if (result) CopyAndReport(item.ResultPath, "result", item);
                if (original && item.OriginalPath is not null) CopyAndReport(item.OriginalPath, "original", item);
            }
            return count;

            void CopyAndReport(string source, string kind, ReviewItem item)
            {
                try { Copy(source, kind, item, outputRoot); }
                catch (Exception ex) { throw new ExportFailedException(source, count, total, ex); }
                count++;
                progress?.Report(new ExportProgress(count, total, Path.GetFileName(source)));
            }
        });

    private static void Copy(string source, string kind, ReviewItem item, string root)
    {
        var relativeDirectory = Path.GetDirectoryName(item.RelativePath.Replace('/', Path.DirectorySeparatorChar)) ?? "";
        var destinationDirectory = Path.Combine(root, Safe(item.DetectionTag), kind, relativeDirectory);
        Directory.CreateDirectory(destinationDirectory);
        File.Copy(source, Path.Combine(destinationDirectory, Path.GetFileName(source)), true);
    }

    private static string Safe(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var result = new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrEmpty(result) ? "待定" : result;
    }
}
