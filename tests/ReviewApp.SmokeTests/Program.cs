using ImageReviewTool.Models;
using ImageReviewTool.Services;
using ImageReviewTool.ViewModels;

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
    var database = new ReviewDatabase
    {
        Items = items.Select(x => new ReviewRecord(x.RelativePath, x.DetectionTag)).ToList()
    };
    var store = new ReviewStore();
    await store.SaveAsync(results, database);
    var restored = await store.LoadAsync(results);
    Check(restored.Items.Count == 2 && restored.Items[0].DetectionTag == "正确检出", "review persistence");

    var legacyRoot = Path.Combine(sandbox, "legacy");
    Directory.CreateDirectory(legacyRoot);
    File.WriteAllText(Path.Combine(legacyRoot, ".review-data.json"),
        """{"DetectionTags":["自定义"],"ResultTags":["OK"],"Items":[{"RelativePath":"old.png","DetectionTag":"自定义","ResultTag":"OK"}]}""");
    var legacy = await store.LoadAsync(legacyRoot);
    Check(legacy.DetectionTags.Single() == "自定义" && legacy.Items.Single().DetectionTag == "自定义",
        "legacy two-tag data compatibility");

    var exported = await new ExportService().ExportAsync([items[0]], exports, true, true);
    Check(exported == 2, "export count");
    Check(File.Exists(Path.Combine(exports, "正确检出", "result", "line-a", "one.png")), "result export path");
    Check(File.Exists(Path.Combine(exports, "正确检出", "original", "line-a", "one.png")), "original export path");

    RunSta(() =>
    {
        var viewModel = new MainViewModel();
        Check(viewModel.AutoAdvance, "automatic advance enabled by default");
        Check(viewModel.ImageFormats.Count(x => x.IsSelected) == 6, "image format checkbox defaults");
        Check(viewModel.ImageFormats.All(x => x.Name != ".gif"), "GIF is absent from image format options");
        viewModel.NewDetectionTag = "临时标签";
        viewModel.AddDetectionTagCommand.Execute(null);
        Check(viewModel.CustomDetectionTags.Contains("临时标签") && viewModel.DetectionTags.Contains("临时标签"),
            "custom tag can be added before opening a folder");
        viewModel.DeleteDetectionTagCommand.Execute("临时标签");
        Check(!viewModel.CustomDetectionTags.Contains("临时标签") && !viewModel.DetectionTags.Contains("临时标签"),
            "unused custom tag can be deleted");
        viewModel.DeleteDetectionTagCommand.Execute("待定");
        Check(viewModel.DetectionTags.Contains("待定"), "built-in tag cannot be deleted");
        var first = new ReviewItem { RelativePath = "first.png", ResultPath = Path.Combine(sandbox, "missing-first.png") };
        var second = new ReviewItem { RelativePath = "second.png", ResultPath = Path.Combine(sandbox, "missing-second.png") };
        viewModel.AddReviewItem(first);
        viewModel.AddReviewItem(second);
        viewModel.SelectedItem = first;
        Check(viewModel.ImageCountSummary.Contains("1 / 2"), "image list shows position within filtered images");
        viewModel.SelectDetectionTagCommand.Execute("漏检");
        Check(first.DetectionTag == "漏检" && ReferenceEquals(viewModel.SelectedItem, second),
            "one-click tagging advances to next image");
        Check(viewModel.ImageCountSummary.Contains("2 / 2"), "image position follows automatic advance");

        viewModel.AutoAdvance = false;
        viewModel.SelectDetectionTagCommand.Execute("误检");
        Check(second.DetectionTag == "误检" && ReferenceEquals(viewModel.SelectedItem, second),
            "automatic advance can be disabled");

        viewModel.AutoAdvance = true;
        viewModel.DetectionFilters.Single(x => x.Name == "误检").IsSelected = false;
        Check(viewModel.ItemsView.Cast<ReviewItem>().SequenceEqual([first])
              && ReferenceEquals(viewModel.SelectedItem, first)
              && viewModel.ImageCountSummary.Contains("1 / 1")
              && viewModel.Statistics.Contains("当前过滤：1"),
            "filter checkbox immediately updates list, selection and count");
        viewModel.DetectionFilters.Single(x => x.Name == "漏检").IsSelected = false;
        Check(!viewModel.ItemsView.Cast<ReviewItem>().Any() && viewModel.SelectedItem is null,
            "unchecking all matching labels clears the image view");
        viewModel.DetectionFilters.Single(x => x.Name == "漏检").IsSelected = true;
        Check(ReferenceEquals(viewModel.SelectedItem, first),
            "rechecking a label immediately restores a visible image");
        viewModel.SelectedItem = first;
        viewModel.SelectDetectionTagCommand.Execute("误检");
        Check(ReferenceEquals(viewModel.SelectedItem, null),
            "automatic advance does not select images excluded by filter");

        var app = new ImageReviewTool.App();
        app.InitializeComponent();
        var window = new ImageReviewTool.MainWindow();
        Check(window.Title == "图片复判工具", "main window XAML and icon load at startup");
        window.Close();
    });

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

static void RunSta(Action test)
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try { test(); }
        catch (Exception ex) { failure = ex; }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure is not null) throw failure;
}
