using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace SDRIQStreamer.App;

public partial class MainWindow : Window
{
    private readonly AppSettingsSession _settingsSession;

    public MainWindow()
        : this(new AppSettingsSession(new AppSettingsStore()))
    {
    }

    public MainWindow(AppSettingsSession settingsSession)
    {
        _settingsSession = settingsSession;
        InitializeComponent();
        RestoreWindowPlacement();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        (DataContext as MainWindowViewModel)?.Shutdown();
        SaveWindowPlacement();
        _settingsSession.Save();
        base.OnClosing(e);
    }

    private async void OnBrowseCwSkimmer(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title           = "Select CwSkimmer.exe",
            AllowMultiple   = false,
            FileTypeFilter  =
            [
                new FilePickerFileType("Executable") { Patterns = ["CwSkimmer.exe", "*.exe"] },
                new FilePickerFileType("All files")  { Patterns = ["*"] }
            ]
        });

        if (files.Count > 0 && DataContext is MainWindowViewModel vm)
            vm.CwSkimmerExePath = files[0].Path.LocalPath;
    }

    private void RestoreWindowPlacement()
    {
        var settings = _settingsSession.Settings;

        if (settings.MainWindowWidth is > 0 && settings.MainWindowHeight is > 0)
        {
            SizeToContent = SizeToContent.Manual;
            Width = settings.MainWindowWidth.Value;
            Height = settings.MainWindowHeight.Value;
        }

        if (settings.MainWindowX.HasValue && settings.MainWindowY.HasValue)
        {
            Position = new PixelPoint(
                (int)Math.Round(settings.MainWindowX.Value),
                (int)Math.Round(settings.MainWindowY.Value));
        }
    }

    private void SaveWindowPlacement()
    {
        if (WindowState != WindowState.Normal) return;

        var settings = _settingsSession.Settings;
        settings.MainWindowX = Position.X;
        settings.MainWindowY = Position.Y;
        settings.MainWindowWidth = Bounds.Width;
        settings.MainWindowHeight = Bounds.Height;
    }
}
