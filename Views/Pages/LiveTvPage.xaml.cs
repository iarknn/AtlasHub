using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using AtlasHub.Services;
using AtlasHub.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace AtlasHub.Views.Pages;

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

        HookVm();
        EnsureCategoryView();

        Dispatcher.BeginInvoke(
            () => ScrollSelectedIntoView(retries: 8),
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
            Dispatcher.BeginInvoke(
                () => ScrollSelectedIntoView(retries: 8),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }

        if (e.PropertyName == nameof(LiveTvViewModel.Categories))
        {
            EnsureCategoryView();
            _categoriesView?.Refresh();
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

        TimelineItems.UpdateLayout();
        TimelineScroll.UpdateLayout();

        var container = TimelineItems.ItemContainerGenerator.ContainerFromItem(selected) as FrameworkElement;
        if (container is null)
        {
            if (retries > 0)
            {
                Dispatcher.BeginInvoke(
                    () => ScrollSelectedIntoView(retries - 1),
                    System.Windows.Threading.DispatcherPriority.Loaded);
            }
            return;
        }

        var viewport = FindDescendant<ScrollContentPresenter>(TimelineScroll);
        if (viewport is null)
        {
            if (retries > 0)
            {
                Dispatcher.BeginInvoke(
                    () => ScrollSelectedIntoView(retries - 1),
                    System.Windows.Threading.DispatcherPriority.Loaded);
            }
            return;
        }

        container.UpdateLayout();
        viewport.UpdateLayout();

        try
        {
            var p = container.TransformToAncestor(viewport).Transform(new Point(0, 0));
            var leftInset = TimelineScroll.Padding.Left;

            var target = TimelineScroll.HorizontalOffset + p.X - leftInset;

            if (target < 0) target = 0;
            if (target > TimelineScroll.ScrollableWidth) target = TimelineScroll.ScrollableWidth;

            TimelineScroll.ScrollToHorizontalOffset(target);
        }
        catch
        {
            if (retries > 0)
            {
                Dispatcher.BeginInvoke(
                    () => ScrollSelectedIntoView(retries - 1),
                    System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) return t;

            var deeper = FindDescendant<T>(child);
            if (deeper is not null) return deeper;
        }
        return null;
    }

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

        if (vm.SelectTimelineItemCommand is not null && vm.SelectTimelineItemCommand.CanExecute(nextItem))
        {
            vm.SelectTimelineItemCommand.Execute(nextItem);
            return true;
        }

        vm.SelectedTimelineItem = nextItem;
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
    // Drag-to-scroll (free, no snap)
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

        if (target < 0) target = 0;
        if (target > TimelineScroll.ScrollableWidth) target = TimelineScroll.ScrollableWidth;

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
                e.Handled = true;
                _suppressClickAfterDrag = false;
            }
        }
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
}
