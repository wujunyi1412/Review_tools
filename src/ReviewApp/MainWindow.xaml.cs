using System.Windows;
using ImageReviewTool.ViewModels;

namespace ImageReviewTool;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var viewModel = new MainViewModel();
        DataContext = viewModel;
        Title = $"图片复判工具 {viewModel.AppVersion}";
    }
}
