using ImageReviewTool.Models;
using ImageReviewTool.Services;

var sandbox = Path.Combine(Path.GetTempPath(), "ImageReviewToolTests", Guid.NewGuid().ToString("N"));
var results = Path.Combine(sandbox, "results");
var originals = Path.Combine(sandbox, "originals");
var exports = Path.Combine(sandbox, "exports");

try
{
    Touch(Path.Combine(results, "line-a", "one.png"));
    Touch(Path.Combine(results, "line-b", "two.jpg"));
    Touch(Path.Combine(results, "ignored.txt"));
    Touch(Path.Combine(originals, "line-a", "one.png"));
    Touch(Path.Combine(originals, "different-tree", "two.jpg"));

    var scanner = new ImageScanner();
    var items = await scanner.ScanAsync(results, originals, [".png", ".jpg"]);
    Check(items.Count == 2, "recursive scan count");
    Check(items.All(x => x.OriginalPath is not null), "relative and unique-filename original matching");

    items[0].DetectionTag = "正确检出";
    items[0].ResultTag = "OK";
    var database = new ReviewDatabase
    {
        Items = items.Select(x => new ReviewRecord(x.RelativePath, x.DetectionTag, x.ResultTag)).ToList()
    };
    var store = new ReviewStore();
    await store.SaveAsync(results, database);
    var restored = await store.LoadAsync(results);
    Check(restored.Items.Count == 2 && restored.Items[0].DetectionTag == "正确检出", "review persistence");

    var exported = await new ExportService().ExportAsync([items[0]], exports, true, true);
    Check(exported == 2, "export count");
    Check(File.Exists(Path.Combine(exports, "正确检出", "OK", "result", "line-a", "one.png")), "result export path");
    Check(File.Exists(Path.Combine(exports, "正确检出", "OK", "original", "line-a", "one.png")), "original export path");

    Console.WriteLine("Smoke tests passed: scan, match, persist, export.");
    return 0;
}
finally
{
    if (Directory.Exists(sandbox)) Directory.Delete(sandbox, true);
}

static void Touch(string path)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllBytes(path, [0]);
}

static void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("Failed: " + name);
}
