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
    private static readonly string[] DefaultResultTags = ["OK", "NG", "待定"];
    private readonly ImageScanner _scanner = new();
    private readonly ReviewStore _store = new();
    private readonly ExportService _exporter = new();
    private string _resultFolder = "", _originalFolder = "", _extensions = ".jpg;.jpeg;.png;.bmp;.tif;.tiff";
    private string _loadedResultFolder = "";
    private string _newDetectionTag = "", _newResultTag = "", _status = "请选择结果文件夹";
    private ReviewItem? _selectedItem;
    private BitmapImage? _resultImage, _originalImage;
    private bool _isBusy, _exportResult = true, _exportOriginal = true;
    private CancellationTokenSource? _saveDebounce;

    public ObservableCollection<ReviewItem> Items { get; } = [];
    public ICollectionView ItemsView { get; }
    public ObservableCollection<string> DetectionTags { get; } = [];
    public ObservableCollection<string> ResultTags { get; } = [];
    public ObservableCollection<FilterOption> DetectionFilters { get; } = [];
    public ObservableCollection<FilterOption> ResultFilters { get; } = [];
    public ObservableCollection<string> Statistics { get; } = [];
    public ViewportState Viewport { get; } = new();

    public RelayCommand BrowseResultCommand { get; }
    public RelayCommand BrowseOriginalCommand { get; }
    public RelayCommand OpenCommand { get; }
    public RelayCommand AddDetectionTagCommand { get; }
    public RelayCommand AddResultTagCommand { get; }
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
    public string Extensions { get => _extensions; set => Set(ref _extensions, value); }
    public string NewDetectionTag { get => _newDetectionTag; set => Set(ref _newDetectionTag, value); }
    public string NewResultTag { get => _newResultTag; set => Set(ref _newResultTag, value); }
    public string Status { get => _status; set => Set(ref _status, value); }
    public bool IsBusy { get => _isBusy; set => Set(ref _isBusy, value); }
    public bool HasOriginalFolder => Directory.Exists(OriginalFolder);
    public int ViewerColumns => HasOriginalFolder ? 2 : 1;
    public bool ExportResult { get => _exportResult; set => Set(ref _exportResult, value); }
    public bool ExportOriginal { get => _exportOriginal; set => Set(ref _exportOriginal, value); }
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
        AddResultTagCommand = new(_ => AddTag(NewResultTag, ResultTags, ResultFilters, () => NewResultTag = ""));
        PreviousCommand = new(_ => Navigate(-1));
        NextCommand = new(_ => Navigate(1));
        ExportCommand = new(async _ => await ExportAsync());
        SetTags(DefaultDetectionTags, DefaultResultTags);
    }

    private async Task OpenAsync()
    {
        if (!Directory.Exists(ResultFolder)) { Status = "结果文件夹不存在。"; return; }
        IsBusy = true; Status = "正在递归扫描图片…";
        try
        {
            _saveDebounce?.Cancel();
            if (!string.IsNullOrEmpty(_loadedResultFolder)) await SaveAsync();
            var targetRoot = ResultFolder;
            var database = await _store.LoadAsync(targetRoot);
            SetTags(DefaultDetectionTags.Concat(database.DetectionTags.Select(NormalizeTag)),
                DefaultResultTags.Concat(database.ResultTags.Select(NormalizeTag)));
            var records = database.Items.ToDictionary(x => x.RelativePath, StringComparer.OrdinalIgnoreCase);
            var scanned = await _scanner.ScanAsync(targetRoot,
                Directory.Exists(OriginalFolder) ? OriginalFolder : null,
                Extensions.Split([';', ',', ' '], StringSplitOptions.RemoveEmptyEntries));
            Items.Clear();
            foreach (var item in scanned)
            {
                if (records.TryGetValue(item.RelativePath, out var record))
                { item.DetectionTag = NormalizeTag(record.DetectionTag); item.ResultTag = NormalizeTag(record.ResultTag); }
                item.PropertyChanged += ItemChanged;
                Items.Add(item);
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

    private void SetTags(IEnumerable<string> detection, IEnumerable<string> result)
    {
        ReplaceTags(DetectionTags, DetectionFilters, detection);
        ReplaceTags(ResultTags, ResultFilters, result);
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
    private void FilterChanged(object? sender, PropertyChangedEventArgs e)
    {
        ItemsView.Refresh();
        if (SelectedItem is not null && !ItemsView.Contains(SelectedItem)) SelectedItem = ItemsView.Cast<ReviewItem>().FirstOrDefault();
        UpdateStatistics();
    }
    private bool FilterItem(object value)
    {
        var item = (ReviewItem)value;
        return DetectionFilters.Any(x => x.IsSelected && x.Name.Equals(item.DetectionTag, StringComparison.OrdinalIgnoreCase))
            && ResultFilters.Any(x => x.IsSelected && x.Name.Equals(item.ResultTag, StringComparison.OrdinalIgnoreCase));
    }
    private void ItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ReviewItem.DetectionTag) or nameof(ReviewItem.ResultTag))
        { ItemsView.Refresh(); UpdateStatistics(); QueueSave(); }
    }
    private void UpdateStatistics()
    {
        Statistics.Clear();
        Statistics.Add($"全部：{Items.Count}    当前过滤：{ItemsView.Cast<object>().Count()}");
        foreach (var group in Items.GroupBy(x => x.DetectionTag).OrderBy(x => x.Key)) Statistics.Add($"{group.Key}：{group.Count()}");
        Statistics.Add("────────");
        foreach (var group in Items.GroupBy(x => x.ResultTag).OrderBy(x => x.Key)) Statistics.Add($"{group.Key}：{group.Count()}");
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
        ResultTags = ResultTags.Except(DefaultResultTags, StringComparer.OrdinalIgnoreCase).ToList(),
        Items = Items.Select(x => new ReviewRecord(x.RelativePath, x.DetectionTag, x.ResultTag)).ToList()
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
