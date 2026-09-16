using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageReviewTool.Models;

namespace ImageReviewTool.Controls;

public sealed class ZoomImageView : Border
{
    private readonly Image _image;
    private readonly ScaleTransform _scale = new();
    private readonly TranslateTransform _translate = new();
    private Point? _dragStart;

    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(BitmapSource), typeof(ZoomImageView), new PropertyMetadata(null, OnSourceChanged));
    public static readonly DependencyProperty ViewportProperty = DependencyProperty.Register(
        nameof(Viewport), typeof(ViewportState), typeof(ZoomImageView), new PropertyMetadata(null, OnViewportChanged));

    public BitmapSource? Source { get => (BitmapSource?)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public ViewportState? Viewport { get => (ViewportState?)GetValue(ViewportProperty); set => SetValue(ViewportProperty, value); }

    public ZoomImageView()
    {
        Background = new SolidColorBrush(Color.FromRgb(15, 18, 21));
        BorderBrush = new SolidColorBrush(Color.FromRgb(62, 70, 78));
        BorderThickness = new Thickness(1);
        ClipToBounds = true;
        _image = new Image { Stretch = Stretch.Uniform, RenderTransformOrigin = new Point(0, 0) };
        var transforms = new TransformGroup();
        transforms.Children.Add(_scale);
        transforms.Children.Add(_translate);
        _image.RenderTransform = transforms;
        Child = _image;
        MouseWheel += Wheel;
        MouseLeftButtonDown += Down;
        MouseLeftButtonUp += Up;
        MouseMove += Move;
    }

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ZoomImageView)d)._image.Source = (ImageSource?)e.NewValue;

    private static void OnViewportChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (ZoomImageView)d;
        if (e.OldValue is ViewportState oldState) oldState.PropertyChanged -= view.ViewChanged;
        if (e.NewValue is ViewportState newState) newState.PropertyChanged += view.ViewChanged;
        view.UpdateTransform();
    }

    private void ViewChanged(object? sender, PropertyChangedEventArgs e) => UpdateTransform();
    private void UpdateTransform()
    {
        if (Viewport is null) return;
        _scale.ScaleX = _scale.ScaleY = Viewport.Scale;
        _translate.X = Viewport.OffsetX;
        _translate.Y = Viewport.OffsetY;
    }

    private void Wheel(object sender, MouseWheelEventArgs e)
    {
        if (Viewport is null) return;
        var position = e.GetPosition(this);
        var previous = Viewport.Scale;
        var next = Math.Clamp(previous * (e.Delta > 0 ? 1.15 : 1 / 1.15), 0.05, 40);
        var ratio = next / previous;
        Viewport.OffsetX = position.X - (position.X - Viewport.OffsetX) * ratio;
        Viewport.OffsetY = position.Y - (position.Y - Viewport.OffsetY) * ratio;
        Viewport.Scale = next;
        e.Handled = true;
    }

    private void Down(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            Viewport?.Reset();
            e.Handled = true;
            return;
        }
        _dragStart = e.GetPosition(this);
        CaptureMouse();
        Cursor = Cursors.SizeAll;
    }
    private void Up(object sender, MouseButtonEventArgs e) { _dragStart = null; ReleaseMouseCapture(); Cursor = Cursors.Arrow; }
    private void Move(object sender, MouseEventArgs e)
    {
        if (_dragStart is null || Viewport is null || e.LeftButton != MouseButtonState.Pressed) return;
        var point = e.GetPosition(this);
        Viewport.OffsetX += point.X - _dragStart.Value.X;
        Viewport.OffsetY += point.Y - _dragStart.Value.Y;
        _dragStart = point;
    }
}
