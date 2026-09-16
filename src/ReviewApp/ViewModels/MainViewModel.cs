using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using System.Windows;
using ImageReviewTool.Infrastructure;
using ImageReviewTool.Models;
using ImageReviewTool.Services;
using Microsoft.Win32;

namespace ImageReviewTool.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private static readonly string[] DefaultDetectionTags = ["正确检出", "漏检", "误检", "部分漏检", "部分误检", "待定"];
    private readonly ImageScanner _scanner = new();
    private readonly ReviewStore _store = new();
    private readonly ExportService _exporter = new();
    private string _resultFolder = "", _originalFolder = "";
    private string _loadedResultFolder = "";
    private string _newDetectionTag = "", _status = "请选择结果文件夹";
    private ReviewItem? _selectedItem;
    private BitmapImage? _resultImage, _originalImage;
    private bool _isBusy, _exportResult = true, _exportOriginal = true, _autoAdvance = true;
    private bool _isExporting;
    private bool _showExportProgress;
    private double _exportProgressPercent;
    private string _exportProgressText = "";
    private bool _updatingTags;
    private CancellationTokenSource? _saveDebounce;

    public ObservableCollection<ReviewItem> Items { get; } = [];
    public ObservableCollection<FilterOption> ImageFormats { get; } = [];
    public ICollectionView ItemsView { get; }
    public ObservableCollection<string> DetectionTags { get; } = [];
    public ObservableCollection<string> CustomDetectionTags { get; } = [];
    public ObservableCollection<FilterOption> DetectionFilters { get; } = [];
    public ObservableCollection<string> Statistics { get; } = [];
    public ViewportState Viewport { get; } = new();

    public RelayCommand BrowseResultCommand { get; }
    public RelayCommand BrowseOriginalCommand { get; }
    public RelayCommand OpenCommand { get; }
    public RelayCommand AddDetectionTagCommand { get; }
    public RelayCommand DeleteDetectionTagCommand { get; }
    public RelayCommand SelectDetectionTagCommand { get; }
    public RelayCommand PreviousCommand { get; }
    public RelayCommand NextCommand { get; }
    public RelayCommand ExportCommand { get; }

    public string ResultFolder { get => _resultFolder; set => Set(ref _resultFolder, value); }
    public string AppVersion
    {
        get
        {
            var version = typeof(App).Assembly.GetName().Version;
            return version is null ? "v未知" : $"v{version.Major}.{version.Minor}.{version.Build}";
        }
    }
    public string OriginalFolder
    {
        get => _originalFolder;
        set
        {
            if (Set(ref _originalFolder, value))
            { Raise(nameof(HasOriginalFolder)); Raise(nameof(ViewerColumns)); }
        }
    }
    public string NewDetectionTag { get => _newDetectionTag; set => Set(ref _newDetectionTag, value); }
    public string Status { get => _status; set => Set(ref _status, value); }
    public bool IsBusy { get => _isBusy; set => Set(ref _isBusy, value); }
    public bool HasOriginalFolder => Directory.Exists(OriginalFolder);
    public int ViewerColumns => HasOriginalFolder ? 2 : 1;
    public string ImageCountSummary
    {
        get
        {
            var visible = ItemsView.Cast<ReviewItem>().ToList();
            var position = SelectedItem is null ? -1 : visible.IndexOf(SelectedItem);
            return $"图片列表 · {position + 1} / {visible.Count}";
        }
    }
    public bool ExportResult { get => _exportResult; set => Set(ref _exportResult, value); }
    public bool ExportOriginal { get => _exportOriginal; set => Set(ref _exportOriginal, value); }
    public bool IsExporting { get => _isExporting; private set => Set(ref _isExporting, value); }
    public bool ShowExportProgress { get => _showExportProgress; private set => Set(ref _showExportProgress, value); }
    public double ExportProgressPercent { get => _exportProgressPercent; private set => Set(ref _exportProgressPercent, value); }
    public string ExportProgressText { get => _exportProgressText; private set => Set(ref _exportProgressText, value); }
    public bool AutoAdvance { get => _autoAdvance; set => Set(ref _autoAdvance, value); }
    public BitmapImage? ResultImage { get => _resultImage; private set => Set(ref _resultImage, value); }
    public BitmapImage? OriginalImage { get => _originalImage; private set => Set(ref _originalImage, value); }
    public ReviewItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (Set(ref _selectedItem, value))
            {
                LoadSelectedImages();
                Viewport.Reset();
                Raise(nameof(ImageCountSummary));
            }
        }
    }

    public MainViewModel()
    {
        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.Filter = FilterItem;
        BrowseResultCommand = new(_ => PickFolder(path => ResultFolder = path));
        BrowseOriginalCommand = new(_ => PickFolder(path => { OriginalFolder = path; Raise(nameof(HasOriginalFolder)); }));
        OpenCommand = new(async _ => await OpenAsync(), _ => !IsBusy);
        AddDetectionTagCommand = new(_ => AddTag(NewDetectionTag));
        DeleteDetectionTagCommand = new(tag => DeleteTag(tag as string));
        SelectDetectionTagCommand = new(tag => SelectDetectionTag(tag as string));
        PreviousCommand = new(_ => Navigate(-1));
        NextCommand = new(_ => Navigate(1));
        ExportCommand = new(async _ => await ExportAsync());
        foreach (var extension in new[] { ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff" })
            ImageFormats.Add(new FilterOption { Name = extension });
        SetTags(DefaultDetectionTags);
        UpdateStatistics();
    }

    private async Task OpenAsync()
    {
        if (!Directory.Exists(ResultFolder)) { Status = "结果文件夹不存在。"; return; }
        var selectedFormats = ImageFormats.Where(x => x.IsSelected).Select(x => x.Name).ToArray();
        if (selectedFormats.Length == 0) { Status = "请至少勾选一种图片格式。"; return; }
        IsBusy = true; Status = "正在递归扫描图片…";
        try
        {
            _saveDebounce?.Cancel();
            if (!string.IsNullOrEmpty(_loadedResultFolder)) await SaveAsync();
            var targetRoot = ResultFolder;
            var database = await _store.LoadAsync(targetRoot);
            var pendingTags = string.IsNullOrEmpty(_loadedResultFolder)
                ? CustomDetectionTags.ToArray() : [];
            SetTags(DefaultDetectionTags.Concat(database.DetectionTags.Select(NormalizeTag)).Concat(pendingTags));
            var records = database.Items.ToDictionary(x => x.RelativePath, StringComparer.OrdinalIgnoreCase);
            var scanned = await _scanner.ScanAsync(targetRoot,
                Directory.Exists(OriginalFolder) ? OriginalFolder : null,
                selectedFormats);
            foreach (var oldItem in Items) oldItem.PropertyChanged -= ItemChanged;
            Items.Clear();
            foreach (var item in scanned)
            {
                if (records.TryGetValue(item.RelativePath, out var record))
                { item.DetectionTag = NormalizeTag(record.DetectionTag); }
                AddReviewItem(item);
            }
            ItemsView.Refresh();
            _loadedResultFolder = targetRoot;
            SelectedItem = ItemsView.Cast<ReviewItem>().FirstOrDefault();
            UpdateStatistics();
            if (pendingTags.Length > 0) QueueSave();
            Raise(nameof(HasOriginalFolder));
            Raise(nameof(ViewerColumns));
            Status = $"已载入 {Items.Count} 张结果图，匹配原图 {Items.Count(x => x.OriginalPath is not null)} 张";
        }
        catch (Exception ex) { Status = "打开失败：" + ex.Message; }
        finally { IsBusy = false; }
    }

    private void SetTags(IEnumerable<string> detection)
    {
        ReplaceTags(DetectionTags, DetectionFilters, detection);
        SyncCustomTags();
    }
    public void AddReviewItem(ReviewItem item)
    {
        item.PropertyChanged += ItemChanged;
        Items.Add(item);
    }
    private static string NormalizeTag(string? tag) =>
        string.IsNullOrWhiteSpace(tag) || string.Equals(tag, "NULL", StringComparison.OrdinalIgnoreCase)
            ? "待定" : tag;
    private void ReplaceTags(ObservableCollection<string> tags, ObservableCollection<FilterOption> filters, IEnumerable<string> values)
    {
        foreach (var option in filters) option.PropertyChanged -= FilterChanged;
        tags.Clear(); filters.Clear();
        foreach (var value in values.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            tags.Add(value);
            var option = new FilterOption { Name = value };
            option.PropertyChanged += FilterChanged;
            filters.Add(option);
        }
    }
    private void AddTag(string value)
    {
        value = value.Trim();
        if (value.Length == 0 || DetectionTags.Contains(value, StringComparer.OrdinalIgnoreCase)) return;
        DetectionTags.Insert(DetectionTags.Count > 0 ? DetectionTags.Count - 1 : 0, value);
        var option = new FilterOption { Name = value };
        option.PropertyChanged += FilterChanged;
        DetectionFilters.Insert(DetectionFilters.Count > 0 ? DetectionFilters.Count - 1 : 0, option);
        NewDetectionTag = "";
        SyncCustomTags();
        QueueSave(); UpdateStatistics();
    }
    private void DeleteTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag) || DefaultDetectionTags.Contains(tag, StringComparer.OrdinalIgnoreCase)) return;
        var actual = DetectionTags.FirstOrDefault(x => x.Equals(tag, StringComparison.OrdinalIgnoreCase));
        if (actual is null) return;
        var affected = Items.Where(x => x.DetectionTag.Equals(actual, StringComparison.OrdinalIgnoreCase)).ToList();
        if (affected.Count > 0)
        {
            var answer = MessageBox.Show(
                $"“{actual}”已用于 {affected.Count} 张图片。删除后这些图片会改为“待定”，确定删除吗？",
                "删除自定义标签", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;
        }
        _updatingTags = true;
        try { foreach (var item in affected) item.DetectionTag = "待定"; }
        finally { _updatingTags = false; }
        DetectionTags.Remove(actual);
        var option = DetectionFilters.FirstOrDefault(x => x.Name.Equals(actual, StringComparison.OrdinalIgnoreCase));
        if (option is not null)
        {
            option.PropertyChanged -= FilterChanged;
            DetectionFilters.Remove(option);
        }
        SyncCustomTags();
        ItemsView.Refresh();
        if (SelectedItem is null || !ItemsView.Contains(SelectedItem))
            SelectedItem = ItemsView.Cast<ReviewItem>().FirstOrDefault();
        UpdateStatistics();
        QueueSave();
        Status = $"已删除标签“{actual}”" + (affected.Count > 0 ? $"，{affected.Count} 张图片改为“待定”。" : "。");
    }
    private void SyncCustomTags()
    {
        CustomDetectionTags.Clear();
        foreach (var tag in DetectionTags.Except(DefaultDetectionTags, StringComparer.OrdinalIgnoreCase))
            CustomDetectionTags.Add(tag);
    }
    private void SelectDetectionTag(string? tag)
    {
        var current = SelectedItem;
        if (current is null || tag is null || !DetectionTags.Contains(tag)) return;
        var before = ItemsView.Cast<ReviewItem>().ToList();
        var index = before.IndexOf(current);
        current.DetectionTag = tag;
        if (!AutoAdvance)
        {
            if (!ItemsView.Contains(current)) SelectedItem = ItemsView.Cast<ReviewItem>().FirstOrDefault();
            return;
        }
        if (index < 0) index = 0;
        for (var step = 1; step <= before.Count; step++)
        {
            var candidate = before[(index + step) % before.Count];
            if (!ItemsView.Contains(candidate)) continue;
            SelectedItem = candidate;
            return;
        }
        SelectedItem = null;
    }
    private void FilterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FilterOption.IsSelected)) return;
        var previouslySelected = SelectedItem;
        ItemsView.Refresh();
        if (previouslySelected is null || !ItemsView.Contains(previouslySelected))
            SelectedItem = ItemsView.Cast<ReviewItem>().FirstOrDefault();
        UpdateStatistics();
        Status = $"过滤已更新：当前过滤 {ItemsView.Cast<object>().Count()} 张（全部 {Items.Count} 张）";
    }
    private bool FilterItem(object value)
    {
        var item = (ReviewItem)value;
        return DetectionFilters.Any(x => x.IsSelected && x.Name.Equals(item.DetectionTag, StringComparison.OrdinalIgnoreCase));
    }
    private void ItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_updatingTags) return;
        if (e.PropertyName is nameof(ReviewItem.DetectionTag))
        { ItemsView.Refresh(); UpdateStatistics(); QueueSave(); }
    }
    private void UpdateStatistics()
    {
        var visibleCount = ItemsView.Cast<object>().Count();
        Raise(nameof(ImageCountSummary));
        Statistics.Clear();
        Statistics.Add($"全部：{Items.Count}");
        Statistics.Add($"当前过滤：{visibleCount}");
        foreach (var tag in DetectionTags.Concat(Items.Select(x => x.DetectionTag)).Distinct(StringComparer.OrdinalIgnoreCase))
            Statistics.Add($"{tag}：{Items.Count(x => x.DetectionTag.Equals(tag, StringComparison.OrdinalIgnoreCase))}");
    }
    private void Navigate(int delta)
    {
        var visible = ItemsView.Cast<ReviewItem>().ToList();
        if (visible.Count == 0) return;
        var index = Math.Max(0, visible.IndexOf(SelectedItem!));
        SelectedItem = visible[(index + delta + visible.Count) % visible.Count];
    }
    private void LoadSelectedImages()
    {
        ResultImage = LoadBitmap(SelectedItem?.ResultPath);
        OriginalImage = LoadBitmap(SelectedItem?.OriginalPath);
    }
    private static BitmapImage? LoadBitmap(string? path)
    {
        if (path is null || !File.Exists(path)) return null;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.UriSource = new Uri(path, UriKind.Absolute); bitmap.EndInit(); bitmap.Freeze(); return bitmap;
        }
        catch { return null; }
    }
    private async void QueueSave()
    {
        if (string.IsNullOrEmpty(_loadedResultFolder)) return;
        _saveDebounce?.Cancel();
        var tokenSource = _saveDebounce = new CancellationTokenSource();
        try { await Task.Delay(400, tokenSource.Token); await SaveAsync(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Status = "自动保存失败：" + ex.Message; }
    }
    private Task SaveAsync() => _store.SaveAsync(_loadedResultFolder, new ReviewDatabase
    {
        DetectionTags = DetectionTags.Except(DefaultDetectionTags, StringComparer.OrdinalIgnoreCase).ToList(),
        Items = Items.Select(x => new ReviewRecord(x.RelativePath, x.DetectionTag)).ToList()
    });
    private async Task ExportAsync()
    {
        if (IsBusy) return;
        if (Items.Count == 0)
        {
            Status = "请先打开包含图片的结果文件夹，再进行导出。";
            MessageBox.Show(Status, "无法导出", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var selected = ItemsView.Cast<ReviewItem>().ToList();
        if (selected.Count == 0)
        {
            Status = "当前过滤条件下没有图片，请调整左侧标签过滤。";
            MessageBox.Show(Status, "无法导出", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!ExportResult && !ExportOriginal)
        {
            Status = "请至少选择导出结果图或原图。";
            MessageBox.Show(Status, "无法导出", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (ExportOriginal && !ExportResult && selected.All(x => x.OriginalPath is null))
        {
            Status = "当前过滤结果中没有匹配的原图。";
            MessageBox.Show(Status, "无法导出", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialog = new OpenFolderDialog { Title = "选择导出文件夹", Multiselect = false };
        if (dialog.ShowDialog() != true) return;
        IsBusy = true;
        IsExporting = true;
        ShowExportProgress = true;
        ExportProgressPercent = 0;
        ExportProgressText = "准备导出…";
        Status = "正在导出图片…";
        var progress = new Progress<ExportProgress>(update =>
        {
            if (!IsExporting) return;
            ExportProgressPercent = update.Total == 0 ? 0 : 100.0 * update.Completed / update.Total;
            ExportProgressText = $"已完成 {update.Completed} / {update.Total} 个文件";
            if (update.FileName.Length > 0)
                Status = $"正在导出 {update.Completed}/{update.Total}：{update.FileName}";
        });
        try
        {
            var count = await _exporter.ExportAsync(selected, dialog.FolderName, ExportResult, ExportOriginal, progress);
            IsExporting = false;
            ExportProgressPercent = 100;
            ExportProgressText = $"导出完成 · {count} / {count} 个文件";
            Status = $"导出完成：{count} 个文件 → {dialog.FolderName}";
            MessageBox.Show($"成功导出 {count} 个文件。\n保存位置：{dialog.FolderName}",
                "导出完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (ExportFailedException ex)
        {
            IsExporting = false;
            ExportProgressPercent = ex.Total == 0 ? 0 : 100.0 * ex.Completed / ex.Total;
            Status = $"导出失败：已完成 {ex.Completed}/{ex.Total} 个文件。";
            ExportProgressText = $"导出失败 · 已完成 {ex.Completed} / {ex.Total} 个文件";
            MessageBox.Show($"{Status}\n失败文件：{ex.SourcePath}\n原因：{ex.InnerException?.Message}",
                "导出失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            IsExporting = false;
            ExportProgressText = "导出失败";
            Status = "导出失败：" + ex.Message;
            MessageBox.Show(Status, "导出失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { IsBusy = false; IsExporting = false; }
    }
    private static void PickFolder(Action<string> set)
    {
        var dialog = new OpenFolderDialog { Multiselect = false };
        if (dialog.ShowDialog() == true) set(dialog.FolderName);
    }
}
