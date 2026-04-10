using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace SDRIQStreamer.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = new AppServices();
            var viewModel = services.CreateMainWindowViewModel();
            desktop.MainWindow = new MainWindow(services.SettingsSession) { DataContext = viewModel };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
