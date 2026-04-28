using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
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

    private void OnOpenSetupWizard(object? sender, RoutedEventArgs e)
    {
        var viewer = new SetupWizardWindow();

        if (VisualRoot is Window owner)
            viewer.Show(owner);
        else
            viewer.Show();
    }

    private void OnOpenSupport(object? sender, RoutedEventArgs e)
    {
        const string issuesUrl = "https://github.com/cdub89/SDR-IQ-Streamer/issues";
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = issuesUrl,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore browser launch failures to avoid disrupting app flow.
        }
    }

    private async void OnResetChannelConfigRequested(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        if (vm.IsCwSkimmerRunning)
        {
            await ShowResetBlockedDialogAsync();
            return;
        }

        var confirmed = await ShowResetChannelConfigDialogAsync();
        if (!confirmed)
            return;

        if (vm.ResetCwSkimmerChannelConfigCommand.CanExecute(null))
            vm.ResetCwSkimmerChannelConfigCommand.Execute(null);
    }

    private async Task<bool> ShowResetChannelConfigDialogAsync()
    {
        var result = false;

        var message = new TextBlock
        {
            Text = "Reset streamer channel INI files (ch1-ch4)?\n\nThis removes only generated channel INIs. Your manual CwSkimmer.ini baseline is not changed.\n\nAfter reset, run CW Skimmer manually and review the Audio tab. Ensure DAX IQ RX 1 and DAX Audio RX 1 are selected, then exit CW Skimmer to save calibration.",
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 420
        };

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 80,
            IsDefault = true
        };
        var resetButton = new Button
        {
            Content = "Reset",
            MinWidth = 80,
            IsCancel = true
        };

        var dialog = new Window
        {
            Title = "Confirm Reset",
            Width = 470,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 14,
                Children =
                {
                    message,
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancelButton, resetButton }
                    }
                }
            }
        };

        cancelButton.Click += (_, _) => dialog.Close();
        resetButton.Click += (_, _) =>
        {
            result = true;
            dialog.Close();
        };

        await dialog.ShowDialog(this);

        return result;
    }

    private async Task ShowResetBlockedDialogAsync()
    {
        var message = new TextBlock
        {
            Text = "CW Skimmer is currently running.\n\nStop all CW Skimmer instances before resetting channel INI files.",
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 400
        };

        var okButton = new Button
        {
            Content = "OK",
            MinWidth = 80,
            IsDefault = true
        };

        var dialog = new Window
        {
            Title = "Reset Blocked",
            Width = 440,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 14,
                Children =
                {
                    message,
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Children = { okButton }
                    }
                }
            }
        };

        okButton.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
    }

}
