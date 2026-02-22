using System;
using System.ComponentModel;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using AtlasHub.Services;
using AtlasHub.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace AtlasHub.Views.Pages;

[SupportedOSPlatform("windows7.0")]
public partial class LiveTvPage : UserControl
{
    private INotifyPropertyChanged? _vmNpc;

    // Categories filter (View-only)
    private ICollectionView? _categoriesView;

    // Drag
    private bool _isDragging;
    private Point _dragStartPoint;
    private double _dragStartOffset;
    private bool _suppressClickAfterDrag;
    private const double DragThreshold = 6;

    // Timeline measurement cache
    private double _timelineStride; // item width + margin

    private enum VideoScaleMode
    {
        Fit,
        Fill,
        Original
    }

    public LiveTvPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += (_, _) => HookVm();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Player binding
        var player = App.Services.GetRequiredService<PlayerService>();
        VideoHost.MediaPlayer = player.Player;

        // UI volume init
        try
        {
            var vol = player.Player?.Volume ?? 80;
            if (VolumeSlider is not null) VolumeSlider.Value = vol;
            if (VolumeValueText is not null) VolumeValueText.Text = vol.ToString();
        }
        catch { /* ignore */ }

        // Varsayılan görüntü modu: Sığdır
        ApplyVideoScaleMode(VideoScaleMode.Fit);

        HookVm();
        EnsureCategoryView();

        // İlk paint sonrası seçili kartı hizala
        Dispatcher.BeginInvoke(
            () => ScrollSelectedIntoView(retries: 12),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        UnhookVm();
    }

    private void HookVm()
    {
        UnhookVm();

        _vmNpc = DataContext as INotifyPropertyChanged;
        if (_vmNpc is not null)
            _vmNpc.PropertyChanged += VmOnPropertyChanged;
    }

    private void UnhookVm()
    {
        if (_vmNpc is not null)
            _vmNpc.PropertyChanged -= VmOnPropertyChanged;

        _vmNpc = null;
    }

    private void VmOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LiveTvViewModel.SelectedTimelineItem))
        {
            // Her seçim değiştiğinde hizalamayı dene
            Dispatcher.BeginInvoke(
                () => ScrollSelectedIntoView(retries: 12),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }

        if (e.PropertyName == nameof(LiveTvViewModel.Categories))
        {
            EnsureCategoryView();
            _categoriesView?.Refresh();
        }

        if (e.PropertyName == nameof(LiveTvViewModel.Timeline))
        {
            // Timeline yenilendi -> stride’ı yeniden ölç
            _timelineStride = 0;
            Dispatcher.BeginInvoke(
                () => ScrollSelectedIntoView(retries: 12),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    // -----------------------
    // Category search (View)
    // -----------------------
    private void EnsureCategoryView()
    {
        if (CategoriesList is null) return;

        _categoriesView = CollectionViewSource.GetDefaultView(CategoriesList.ItemsSource);
        if (_categoriesView is null) return;

        _categoriesView.Filter = obj =>
        {
            var q = CategorySearchBox?.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(q)) return true;

            var s = obj?.ToString() ?? "";
            return s.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
        };
    }

    private void CategorySearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_categoriesView is null) EnsureCategoryView();
        _categoriesView?.Refresh();
    }

    // ----------------------------------------
    // Timeline: selected card -> align to left
    // ----------------------------------------
    private void ScrollSelectedIntoView(int retries)
    {
        if (TimelineScroll is null || TimelineItems is null) return;
        if (DataContext is not LiveTvViewModel vm) return;

        var selected = vm.SelectedTimelineItem;
        if (selected is null) return;
        if (vm.Timeline is null || vm.Timeline.Count == 0) return;

        // Stride bilinmiyorsa, ilk container’dan ölçmeyi dene
        if (_timelineStride <= 1)
        {
            TimelineItems.UpdateLayout();

            var firstContainer = TimelineItems.ItemContainerGenerator.ContainerFromIndex(0) as FrameworkElement;
            if (firstContainer is not null)
            {
                var w = firstContainer.ActualWidth;
                var m = firstContainer.Margin;
                var stride = w + m.Left + m.Right;
                if (stride > 1)
                    _timelineStride = stride;
            }
        }

        // Hâlâ stride yoksa, biraz daha sonra tekrar dene
        if (_timelineStride <= 1)
        {
            if (retries > 0)
            {
                Dispatcher.BeginInvoke(
                    () => ScrollSelectedIntoView(retries - 1),
                    System.Windows.Threading.DispatcherPriority.Loaded);
            }
            return;
        }

        var index = vm.Timeline.IndexOf(selected);
        if (index < 0) return;

        var leftInset = TimelineScroll.Padding.Left;
        var target = index * _timelineStride; // kart indexine göre offset
        target -= leftInset;

        target = Clamp(target, 0, TimelineScroll.ScrollableWidth);
        TimelineScroll.ScrollToHorizontalOffset(target);
    }

    private static double Clamp(double v, double min, double max)
        => v < min ? min : (v > max ? max : v);

    // ----------------------------------------
    // Card-by-card navigation (no sliding)
    // ----------------------------------------
    private bool SelectRelativeTimelineItem(int delta)
    {
        if (DataContext is not LiveTvViewModel vm) return false;
        if (vm.Timeline is null || vm.Timeline.Count == 0) return false;

        var current = vm.SelectedTimelineItem;
        var currentIndex = current is null ? -1 : vm.Timeline.IndexOf(current);
        var nextIndex = currentIndex + delta;

        if (nextIndex < 0) nextIndex = 0;
        if (nextIndex >= vm.Timeline.Count) nextIndex = vm.Timeline.Count - 1;

        if (nextIndex == currentIndex) return false;

        var nextItem = vm.Timeline[nextIndex];

        // Önce komutu kullan (VM içi mantığı bozmamak için)
        if (vm.SelectTimelineItemCommand is not null && vm.SelectTimelineItemCommand.CanExecute(nextItem))
        {
            vm.SelectTimelineItemCommand.Execute(nextItem);
        }
        else
        {
            vm.SelectedTimelineItem = nextItem;
        }

        // Her seçim sonrası hizalamayı dene
        Dispatcher.BeginInvoke(
            () => ScrollSelectedIntoView(retries: 8),
            System.Windows.Threading.DispatcherPriority.Loaded);

        return true;
    }

    private void BtnTimelineLeft_Click(object sender, RoutedEventArgs e) => SelectRelativeTimelineItem(-1);
    private void BtnTimelineRight_Click(object sender, RoutedEventArgs e) => SelectRelativeTimelineItem(+1);

    private void TimelineScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Delta < 0) SelectRelativeTimelineItem(+1);
        else SelectRelativeTimelineItem(-1);

        e.Handled = true;
    }

    // ----------------------------
    // Drag-to-scroll + snap-to-card
    // ----------------------------
    private void TimelineScroll_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (TimelineScroll is null) return;

        _isDragging = true;
        _suppressClickAfterDrag = false;

        _dragStartPoint = e.GetPosition(TimelineScroll);
        _dragStartOffset = TimelineScroll.HorizontalOffset;

        TimelineScroll.CaptureMouse();
        e.Handled = true;
    }

    private void TimelineScroll_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging || TimelineScroll is null) return;

        var current = e.GetPosition(TimelineScroll);
        var dx = current.X - _dragStartPoint.X;

        if (Math.Abs(dx) >= DragThreshold)
            _suppressClickAfterDrag = true;

        var target = _dragStartOffset - dx;
        target = Clamp(target, 0, TimelineScroll.ScrollableWidth);

        TimelineScroll.ScrollToHorizontalOffset(target);
        e.Handled = true;
    }

    private void TimelineScroll_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (TimelineScroll is null) return;

        if (_isDragging)
        {
            _isDragging = false;
            TimelineScroll.ReleaseMouseCapture();

            if (_suppressClickAfterDrag)
            {
                SnapTimelineToNearestCard();
                e.Handled = true;
                _suppressClickAfterDrag = false;
                return;
            }

            _suppressClickAfterDrag = false;
        }
    }

    private void SnapTimelineToNearestCard()
    {
        if (TimelineScroll is null || TimelineItems is null) return;

        // Stride yoksa ölçmeye çalış
        if (_timelineStride <= 1 && TimelineItems.Items.Count > 0)
        {
            TimelineItems.UpdateLayout();
            var first = TimelineItems.ItemContainerGenerator.ContainerFromIndex(0) as FrameworkElement;
            if (first is not null)
            {
                var w = first.ActualWidth;
                var m = first.Margin;
                var stride = w + m.Left + m.Right;
                if (stride > 1)
                    _timelineStride = stride;
            }
        }

        if (_timelineStride <= 1) return;

        var offset = TimelineScroll.HorizontalOffset;
        var snapped = Math.Round(offset / _timelineStride) * _timelineStride;
        snapped = Clamp(snapped, 0, TimelineScroll.ScrollableWidth);

        TimelineScroll.ScrollToHorizontalOffset(snapped);
    }

    // ----------------------------
    // Player Controls
    // ----------------------------
    private void BtnPlay_Click(object sender, RoutedEventArgs e)
    {
        try { VideoHost?.MediaPlayer?.Play(); } catch { }
    }

    private void BtnPause_Click(object sender, RoutedEventArgs e)
    {
        try { VideoHost?.MediaPlayer?.Pause(); } catch { }
    }

    private void BtnStop_Click(object sender, RoutedEventArgs e)
    {
        try { VideoHost?.MediaPlayer?.Stop(); } catch { }
    }

    private void BtnMute_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (VideoHost?.MediaPlayer is null) return;
            VideoHost.MediaPlayer.Mute = !VideoHost.MediaPlayer.Mute;
        }
        catch { }
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        try
        {
            var vol = (int)Math.Round(e.NewValue);
            if (VolumeValueText is not null) VolumeValueText.Text = vol.ToString();
            if (VideoHost?.MediaPlayer is not null) VideoHost.MediaPlayer.Volume = vol;
        }
        catch { }
    }

    private void VideoFitModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VideoHost?.MediaPlayer is null) return;

        try
        {
            var combo = (ComboBox)sender;
            var item = combo.SelectedItem as ComboBoxItem;
            var tag = item?.Tag as string ?? "Fit";

            var mode = tag switch
            {
                "Fill" => VideoScaleMode.Fill,
                "Original" => VideoScaleMode.Original,
                _ => VideoScaleMode.Fit
            };

            ApplyVideoScaleMode(mode);
        }
        catch
        {
            // yut
        }
    }

    private void ApplyVideoScaleMode(VideoScaleMode mode)
    {
        if (VideoHost?.MediaPlayer is null) return;

        try
        {
            switch (mode)
            {
                case VideoScaleMode.Fit:
                    // Pencereye oran korunarak sığdır
                    VideoHost.MediaPlayer.AspectRatio = null; // otomatik
                    VideoHost.MediaPlayer.Scale = 0;          // fit to window
                    break;

                case VideoScaleMode.Fill:
                    // 16:9 doldurucu görünüm (bazı içeriklerde crop hissi olabilir)
                    VideoHost.MediaPlayer.AspectRatio = "16:9";
                    VideoHost.MediaPlayer.Scale = 0;
                    break;

                case VideoScaleMode.Original:
                    // Kaynağın kendi çözünürlüğü
                    VideoHost.MediaPlayer.AspectRatio = null;
                    VideoHost.MediaPlayer.Scale = 1;          // 1:1 scale
                    break;
            }
        }
        catch
        {
            // platform/driver kaynaklı edge case'ler sessiz geçilsin
        }
    }
}