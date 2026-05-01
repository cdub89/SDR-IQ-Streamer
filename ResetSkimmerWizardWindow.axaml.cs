using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace SDRIQStreamer.App;

public partial class ResetSkimmerWizardWindow : Window
{
    private const int TotalSteps = 4;
    private static readonly string ResourceAssemblyName =
        typeof(ResetSkimmerWizardWindow).Assembly.GetName().Name ?? "SDRIQStreamer";

    private readonly MainWindowViewModel _viewModel;
    private readonly AppSettings _settings;

    private int _currentStep = 1;

    private readonly TextBlock _wizardTitleText;
    private readonly TextBlock _wizardStepText;
    private readonly Button _backButton;
    private readonly Button _nextButton;
    private readonly Button _cancelButton;
    private readonly StackPanel _step1Panel;
    private readonly StackPanel _step2Panel;
    private readonly StackPanel _step3Panel;
    private readonly StackPanel _step4Panel;
    private readonly Button _launchSkimmerButton;
    private readonly TextBlock _launchStatusText;
    private readonly Image _streamerAppImage;
    private readonly Image _radioTabImage;
    private readonly Image _mmeListImage;
    private readonly Image _wdmListImage;
    private readonly TextBox _ch1Mme, _ch2Mme, _ch3Mme, _ch4Mme;
    private readonly TextBox _ch1Wdm, _ch2Wdm, _ch3Wdm, _ch4Wdm;
    private readonly TextBlock _step3ValidationText;
    private readonly RadioButton _driverModeMmeRadio;
    private readonly RadioButton _driverModeWdmRadio;
    private readonly TextBlock _step4GateText;

    // Parameterless ctor exists only to satisfy Avalonia's resource loader; never
    // invoked at runtime — production code always uses the (vm, settings) overload.
    public ResetSkimmerWizardWindow()
        : this(null!, null!)
    {
    }

    public ResetSkimmerWizardWindow(MainWindowViewModel viewModel, AppSettings settings)
    {
        _viewModel = viewModel;
        _settings = settings;
        InitializeComponent();

        _wizardTitleText = Require<TextBlock>("WizardTitleText");
        _wizardStepText = Require<TextBlock>("WizardStepText");
        _backButton = Require<Button>("BackButton");
        _nextButton = Require<Button>("NextButton");
        _cancelButton = Require<Button>("CancelButton");
        _step1Panel = Require<StackPanel>("Step1Panel");
        _step2Panel = Require<StackPanel>("Step2Panel");
        _step3Panel = Require<StackPanel>("Step3Panel");
        _step4Panel = Require<StackPanel>("Step4Panel");
        _launchSkimmerButton = Require<Button>("LaunchSkimmerButton");
        _launchStatusText = Require<TextBlock>("LaunchStatusText");
        _streamerAppImage = Require<Image>("StreamerAppImage");
        _radioTabImage = Require<Image>("RadioTabImage");
        _mmeListImage = Require<Image>("MmeListImage");
        _wdmListImage = Require<Image>("WdmListImage");
        _ch1Mme = Require<TextBox>("Ch1MmeText");
        _ch2Mme = Require<TextBox>("Ch2MmeText");
        _ch3Mme = Require<TextBox>("Ch3MmeText");
        _ch4Mme = Require<TextBox>("Ch4MmeText");
        _ch1Wdm = Require<TextBox>("Ch1WdmText");
        _ch2Wdm = Require<TextBox>("Ch2WdmText");
        _ch3Wdm = Require<TextBox>("Ch3WdmText");
        _ch4Wdm = Require<TextBox>("Ch4WdmText");
        _step3ValidationText = Require<TextBlock>("Step3ValidationText");
        _driverModeMmeRadio = Require<RadioButton>("DriverModeMmeRadio");
        _driverModeWdmRadio = Require<RadioButton>("DriverModeWdmRadio");
        _step4GateText = Require<TextBlock>("Step4GateText");

        _backButton.Click += (_, _) => GoBack();
        _nextButton.Click += (_, _) => GoNext();
        _cancelButton.Click += (_, _) => Close();
        _launchSkimmerButton.Click += OnLaunchSkimmer;

        LoadImages();
        PrefillFromSettings();
        RenderCurrentStep();
    }

    private T Require<T>(string name) where T : Control
    {
        var c = this.FindControl<T>(name);
        if (c is null)
            throw new InvalidOperationException($"ResetSkimmerWizardWindow is missing control: {name}");
        return c;
    }

    private void LoadImages()
    {
        TrySetImage(_streamerAppImage, "Assets/SetupWizard/StreamerApp.png");
        TrySetImage(_radioTabImage, "Assets/SetupWizard/image-8d3c7eef-09f5-4652-8c05-93bf3fe4a9bd.png");
        TrySetImage(_mmeListImage, "Assets/SetupWizard/MMEAudioList.png");
        TrySetImage(_wdmListImage, "Assets/SetupWizard/WDMAudioChoices.png");
    }

    private static void TrySetImage(Image target, string relativePath)
    {
        try
        {
            var uri = new Uri($"avares://{ResourceAssemblyName}/{relativePath}");
            if (!AssetLoader.Exists(uri))
                return;
            using var stream = AssetLoader.Open(uri);
            target.Source = new Bitmap(stream);
        }
        catch
        {
            // Image is decorative; missing asset is non-fatal.
        }
    }

    private void PrefillFromSettings()
    {
        _ch1Mme.Text = FormatIndex(_settings.MmeDeviceIndexCh1);
        _ch2Mme.Text = FormatIndex(_settings.MmeDeviceIndexCh2);
        _ch3Mme.Text = FormatIndex(_settings.MmeDeviceIndexCh3);
        _ch4Mme.Text = FormatIndex(_settings.MmeDeviceIndexCh4);
        _ch1Wdm.Text = FormatIndex(_settings.WdmDeviceIndexCh1);
        _ch2Wdm.Text = FormatIndex(_settings.WdmDeviceIndexCh2);
        _ch3Wdm.Text = FormatIndex(_settings.WdmDeviceIndexCh3);
        _ch4Wdm.Text = FormatIndex(_settings.WdmDeviceIndexCh4);

        var isWdm = string.Equals(_settings.SkimmerSoundcardDriverMode, "WDM", StringComparison.OrdinalIgnoreCase);
        _driverModeWdmRadio.IsChecked = isWdm;
        _driverModeMmeRadio.IsChecked = !isWdm;
    }

    private static string FormatIndex(int? value) =>
        value is int v && v > 0 ? v.ToString(CultureInfo.InvariantCulture) : string.Empty;

    private void GoBack()
    {
        if (_currentStep <= 1) return;
        _currentStep--;
        RenderCurrentStep();
    }

    private void GoNext()
    {
        if (_currentStep == 3 && !TryValidateStep3())
            return;

        if (_currentStep >= TotalSteps)
        {
            if (!CheckSkimmerProcessClosedGate())
                return;

            CommitAndReset();
            return;
        }

        _currentStep++;
        RenderCurrentStep();
    }

    private bool CheckSkimmerProcessClosedGate()
    {
        if (!IsAnyCwSkimmerProcessRunning())
        {
            _step4GateText.IsVisible = false;
            _step4GateText.Text = string.Empty;
            return true;
        }

        _step4GateText.Text =
            "CW Skimmer is still running. Close it first using File / Exit, or click the X button " +
            "on its window, then click Reset and Done again.";
        _step4GateText.IsVisible = true;
        return false;
    }

    private static bool IsAnyCwSkimmerProcessRunning()
    {
        try
        {
            return Process.GetProcessesByName("CwSkimmer").Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private void RenderCurrentStep()
    {
        _step1Panel.IsVisible = _currentStep == 1;
        _step2Panel.IsVisible = _currentStep == 2;
        _step3Panel.IsVisible = _currentStep == 3;
        _step4Panel.IsVisible = _currentStep == 4;

        _wizardStepText.Text = $"Step {_currentStep} of {TotalSteps}";
        _backButton.IsEnabled = _currentStep > 1;
        _nextButton.Content = _currentStep < TotalSteps ? "Next" : "Reset and Done";

        UpdateLaunchButtonState();
    }

    private void UpdateLaunchButtonState()
    {
        var hasPath = !string.IsNullOrWhiteSpace(_viewModel.CwSkimmerExePath) &&
                      File.Exists(_viewModel.CwSkimmerExePath);
        var alreadyRunning = _viewModel.IsCwSkimmerRunning;

        _launchSkimmerButton.IsEnabled = hasPath && !alreadyRunning;

        if (!hasPath)
            _launchStatusText.Text = "Set the CwSkimmer.exe path on the Config tab before launching.";
        else if (alreadyRunning)
            _launchStatusText.Text = "CW Skimmer is already running. Stop it before resetting.";
        else if (string.IsNullOrEmpty(_launchStatusText.Text))
            _launchStatusText.Text = string.Empty;
    }

    private void OnLaunchSkimmer(object? sender, RoutedEventArgs e)
    {
        var path = _viewModel.CwSkimmerExePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            _launchStatusText.Text = "CwSkimmer.exe path is not set or file not found.";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
            _launchStatusText.Text =
                "CW Skimmer launched. Position and size the window now, then proceed to Step 2.";
        }
        catch (Exception ex)
        {
            _launchStatusText.Text = $"Failed to launch CW Skimmer: {ex.Message}";
        }
    }

    private bool TryValidateStep3()
    {
        (TextBox Box, string Label)[] boxes =
        {
            (_ch1Mme, "ch 1 MME"),
            (_ch2Mme, "ch 2 MME"),
            (_ch3Mme, "ch 3 MME"),
            (_ch4Mme, "ch 4 MME"),
            (_ch1Wdm, "ch 1 WDM"),
            (_ch2Wdm, "ch 2 WDM"),
            (_ch3Wdm, "ch 3 WDM"),
            (_ch4Wdm, "ch 4 WDM"),
        };

        foreach (var entry in boxes)
        {
            var text = entry.Box.Text?.Trim() ?? string.Empty;
            if (text.Length == 0)
                continue;

            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n < 1)
            {
                _step3ValidationText.Text = $"{entry.Label}: enter a positive whole number, or leave blank.";
                _step3ValidationText.IsVisible = true;
                return false;
            }
        }

        _step3ValidationText.Text = string.Empty;
        _step3ValidationText.IsVisible = false;
        return true;
    }

    private void CommitAndReset()
    {
        _settings.MmeDeviceIndexCh1 = ParseIndex(_ch1Mme.Text);
        _settings.MmeDeviceIndexCh2 = ParseIndex(_ch2Mme.Text);
        _settings.MmeDeviceIndexCh3 = ParseIndex(_ch3Mme.Text);
        _settings.MmeDeviceIndexCh4 = ParseIndex(_ch4Mme.Text);
        _settings.WdmDeviceIndexCh1 = ParseIndex(_ch1Wdm.Text);
        _settings.WdmDeviceIndexCh2 = ParseIndex(_ch2Wdm.Text);
        _settings.WdmDeviceIndexCh3 = ParseIndex(_ch3Wdm.Text);
        _settings.WdmDeviceIndexCh4 = ParseIndex(_ch4Wdm.Text);
        _settings.SkimmerSoundcardDriverMode =
            _driverModeWdmRadio.IsChecked == true ? "WDM" : "MME";
        _settings.HasShownSkimmerSetupWizard = true;

        if (_viewModel.ResetCwSkimmerChannelConfigCommand.CanExecute(null))
            _viewModel.ResetCwSkimmerChannelConfigCommand.Execute(null);

        Close();
    }

    private static int? ParseIndex(string? raw)
    {
        var text = raw?.Trim();
        if (string.IsNullOrEmpty(text))
            return null;
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0)
            return n;
        return null;
    }
}
