using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Controls.Primitives;

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

    private async void OnBrowseCwSkimmerIni(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select cwskimmer.ini",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("INI file") { Patterns = ["*.ini"] },
                new FilePickerFileType("All files") { Patterns = ["*"] }
            ]
        });

        if (files.Count > 0 && DataContext is MainWindowViewModel vm)
            vm.CwSkimmerIniPath = files[0].Path.LocalPath;
    }

    private void OnOpenSpotTextColorMenu(object? sender, RoutedEventArgs e)
    {
        OpenSpotColorMenu(sender as Control, isBackground: false);
    }

    private void OnOpenSpotBackgroundColorMenu(object? sender, RoutedEventArgs e)
    {
        OpenSpotColorMenu(sender as Control, isBackground: true);
    }

    private void OpenSpotColorMenu(Control? anchor, bool isBackground)
    {
        if (anchor is null || DataContext is not MainWindowViewModel vm)
            return;

        var options = isBackground ? vm.SpotBackgroundColorOptions : vm.SpotColorOptions;
        var swatchPanel = new UniformGrid
        {
            Columns = 4,
            Rows = 2,
            Margin = new Thickness(1)
        };

        foreach (var option in options)
        {
            var selectedOption = option;
            var button = new Button
            {
                Content = CreateColorSwatchHeader(selectedOption.Hex),
                Width = 22,
                Height = 20,
                Padding = new Thickness(0),
                Margin = new Thickness(1, 1, 1, 2),
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            button.Click += (_, _) =>
            {
                if (isBackground)
                    vm.SpotSelectedBackgroundColorOption = selectedOption;
                else
                    vm.SpotSelectedColorOption = selectedOption;

                if (FlyoutBase.GetAttachedFlyout(anchor) is Flyout currentFlyout)
                    currentFlyout.Hide();
            };
            swatchPanel.Children.Add(button);
        }

        var flyout = new Flyout
        {
            Content = swatchPanel,
            Placement = PlacementMode.BottomEdgeAlignedLeft
        };
        flyout.FlyoutPresenterClasses.Add("compact-swatch-flyout");
        FlyoutBase.SetAttachedFlyout(anchor, flyout);
        flyout.ShowAt(anchor);
    }

    private static Control CreateColorSwatchHeader(string hex)
    {
        var fill = Color.TryParse(hex, out var parsed)
            ? (IBrush)new SolidColorBrush(parsed)
            : Brushes.Transparent;

        return new Border
        {
            Width = 18,
            Height = 12,
            Background = fill,
            BorderBrush = Brushes.DimGray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };
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
