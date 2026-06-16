using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Threading;
using MusicDownloader.ViewModels;

namespace MusicDownloader.Views;

public partial class MainWindow : Window
{
    private ScrollViewer? _logScroller;
    private MainViewModel? _subscribedVm;

    public MainWindow()
    {
        InitializeComponent();
        _logScroller = this.FindControl<ScrollViewer>("LogScroller");
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_subscribedVm is not null)
            _subscribedVm.LogLines.CollectionChanged -= OnLogLinesChanged;

        _subscribedVm = DataContext as MainViewModel;

        if (_subscribedVm is not null)
            _subscribedVm.LogLines.CollectionChanged += OnLogLinesChanged;
    }

    private void OnLogLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_logScroller is null)
            return;
        Dispatcher.UIThread.Post(() => _logScroller.ScrollToEnd(), DispatcherPriority.Background);
    }
}
