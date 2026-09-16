using ImageReviewTool.Models;

namespace ImageReviewTool.Services;

public sealed class ImageScanner
{
    public Task<List<ReviewItem>> ScanAsync(string resultRoot, string? originalRoot, IEnumerable<string> extensions) =>
        Task.Run(() => Scan(resultRoot, originalRoot, extensions));

    private static List<ReviewItem> Scan(string resultRoot, string? originalRoot, IEnumerable<string> extensions)
    {
        var allowed = extensions.Select(Normalize).Where(NativeImageFormats.IsSupported)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (allowed.Count == 0) throw new InvalidOperationException("没有有效的图片扩展名。");

        Dictionary<string, string>? originalsByRelative = null;
        Dictionary<string, string>? uniqueOriginalNames = null;
        if (!string.IsNullOrWhiteSpace(originalRoot) && Directory.Exists(originalRoot))
        {
            var originals = Directory.EnumerateFiles(originalRoot, "*", SearchOption.AllDirectories)
                .Where(p => allowed.Contains(Path.GetExtension(p))).ToList();
            originalsByRelative = originals.ToDictionary(
                p => NormalizeRelative(Path.GetRelativePath(originalRoot, p)), p => p,
                StringComparer.OrdinalIgnoreCase);
            uniqueOriginalNames = originals.GroupBy(p => Path.GetFileName(p)!, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single(), StringComparer.OrdinalIgnoreCase);
        }

        return Directory.EnumerateFiles(resultRoot, "*", SearchOption.AllDirectories)
            .Where(p => allowed.Contains(Path.GetExtension(p)))
            .Select(p =>
            {
                var relative = NormalizeRelative(Path.GetRelativePath(resultRoot, p));
                string? original = null;
                if (originalsByRelative?.TryGetValue(relative, out var exact) == true) original = exact;
                else uniqueOriginalNames?.TryGetValue(Path.GetFileName(p), out original);
                return new ReviewItem { RelativePath = relative, ResultPath = p, OriginalPath = original };
            }).OrderBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string Normalize(string value) =>
        value.Trim().StartsWith('.') ? value.Trim().ToLowerInvariant() : "." + value.Trim().ToLowerInvariant();
    private static string NormalizeRelative(string value) => value.Replace('\\', '/');
}
