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
    private CancellationTokenSource? _saveDebounce;

    public ObservableCollection<ReviewItem> Items { get; } = [];
    public ObservableCollection<FilterOption> ImageFormats { get; } = [];
    public ICollectionView ItemsView { get; }
    public ObservableCollection<string> DetectionTags { get; } = [];
    public ObservableCollection<FilterOption> DetectionFilters { get; } = [];
    public ObservableCollection<string> Statistics { get; } = [];
    public ViewportState Viewport { get; } = new();

    public RelayCommand BrowseResultCommand { get; }
    public RelayCommand BrowseOriginalCommand { get; }
    public RelayCommand OpenCommand { get; }
    public RelayCommand AddDetectionTagCommand { get; }
    public RelayCommand SelectDetectionTagCommand { get; }
    public RelayCommand PreviousCommand { get; }
    public RelayCommand NextCommand { get; }
    public RelayCommand ExportCommand { get; }

    public string ResultFolder { get => _resultFolder; set => Set(ref _resultFolder, value); }
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
    public string ImageCountSummary => $"图片列表 · 当前 {ItemsView.Cast<object>().Count()} / 全部 {Items.Count}";
    public bool ExportResult { get => _exportResult; set => Set(ref _exportResult, value); }
    public bool ExportOriginal { get => _exportOriginal; set => Set(ref _exportOriginal, value); }
    public bool AutoAdvance { get => _autoAdvance; set => Set(ref _autoAdvance, value); }
    public BitmapImage? ResultImage { get => _resultImage; private set => Set(ref _resultImage, value); }
    public BitmapImage? OriginalImage { get => _originalImage; private set => Set(ref _originalImage, value); }
    public ReviewItem? SelectedItem
    {
        get => _selectedItem;
        set { if (Set(ref _selectedItem, value)) { LoadSelectedImages(); Viewport.Reset(); } }
    }

    public MainViewModel()
    {
        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.Filter = FilterItem;
        BrowseResultCommand = new(_ => PickFolder(path => ResultFolder = path));
        BrowseOriginalCommand = new(_ => PickFolder(path => { OriginalFolder = path; Raise(nameof(HasOriginalFolder)); }));
        OpenCommand = new(async _ => await OpenAsync(), _ => !IsBusy);
        AddDetectionTagCommand = new(_ => AddTag(NewDetectionTag, DetectionTags, DetectionFilters, () => NewDetectionTag = ""));
        SelectDetectionTagCommand = new(tag => SelectDetectionTag(tag as string));
        PreviousCommand = new(_ => Navigate(-1));
        NextCommand = new(_ => Navigate(1));
        ExportCommand = new(async _ => await ExportAsync());
        foreach (var extension in new[] { ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff" })
            ImageFormats.Add(new FilterOption { Name = extension });
        SetTags(DefaultDetectionTags);
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
            SetTags(DefaultDetectionTags.Concat(database.DetectionTags.Select(NormalizeTag)));
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
    private void AddTag(string value, ObservableCollection<string> tags, ObservableCollection<FilterOption> filters, Action clear)
    {
        value = value.Trim();
        if (value.Length == 0 || tags.Contains(value, StringComparer.OrdinalIgnoreCase)) return;
        tags.Insert(tags.Count > 0 ? tags.Count - 1 : 0, value);
        var option = new FilterOption { Name = value };
        option.PropertyChanged += FilterChanged;
        filters.Insert(filters.Count > 0 ? filters.Count - 1 : 0, option);
        clear(); QueueSave(); UpdateStatistics();
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
        Status = $"过滤已更新：当前显示 {ItemsView.Cast<object>().Count()} / {Items.Count} 张图片";
    }
    private bool FilterItem(object value)
    {
        var item = (ReviewItem)value;
        return DetectionFilters.Any(x => x.IsSelected && x.Name.Equals(item.DetectionTag, StringComparison.OrdinalIgnoreCase));
    }
    private void ItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ReviewItem.DetectionTag))
        { ItemsView.Refresh(); UpdateStatistics(); QueueSave(); }
    }
    private void UpdateStatistics()
    {
        var visibleCount = ItemsView.Cast<object>().Count();
        Raise(nameof(ImageCountSummary));
        Statistics.Clear();
        Statistics.Add($"全部：{Items.Count}    当前过滤：{visibleCount}");
        foreach (var group in Items.GroupBy(x => x.DetectionTag).OrderBy(x => x.Key)) Statistics.Add($"{group.Key}：{group.Count()}");
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
        if (!ExportResult && !ExportOriginal) { Status = "请至少选择导出结果图或原图。"; return; }
        var dialog = new OpenFolderDialog { Title = "选择导出文件夹", Multiselect = false };
        if (dialog.ShowDialog() != true) return;
        IsBusy = true;
        try
        {
            var count = await _exporter.ExportAsync(selected, dialog.FolderName, ExportResult, ExportOriginal);
            Status = $"导出完成：{count} 个文件 → {dialog.FolderName}";
        }
        catch (Exception ex) { Status = "导出失败：" + ex.Message; }
        finally { IsBusy = false; }
    }
    private static void PickFolder(Action<string> set)
    {
        var dialog = new OpenFolderDialog { Multiselect = false };
        if (dialog.ShowDialog() == true) set(dialog.FolderName);
    }
}
