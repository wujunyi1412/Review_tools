using System.Windows;
using ImageReviewTool.ViewModels;

namespace ImageReviewTool;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
