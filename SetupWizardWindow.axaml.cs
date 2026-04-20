using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace SDRIQStreamer.App;

public partial class SetupWizardWindow : Window
{
    private readonly IReadOnlyList<WizardPage> _pages;
    private readonly string _guideDirectory;
    private int _currentIndex;

    private readonly TextBlock _wizardTitleText;
    private readonly TextBlock _wizardStepText;
    private readonly TextBlock _wizardHintText;
    private readonly StackPanel _contentPanel;
    private readonly Button _backButton;
    private readonly Button _nextButton;

    public SetupWizardWindow(string guidePath)
    {
        InitializeComponent();

        _wizardTitleText = RequireControl<TextBlock>("WizardTitleText");
        _wizardStepText = RequireControl<TextBlock>("WizardStepText");
        _wizardHintText = RequireControl<TextBlock>("WizardHintText");
        _contentPanel = RequireControl<StackPanel>("ContentPanel");
        _backButton = RequireControl<Button>("BackButton");
        _nextButton = RequireControl<Button>("NextButton");

        _backButton.Click += OnBackClicked;
        _nextButton.Click += OnNextClicked;

        if (!string.IsNullOrWhiteSpace(guidePath) && File.Exists(guidePath))
        {
            _guideDirectory = Path.GetDirectoryName(guidePath) ?? string.Empty;
            _pages = ParseWizardPages(File.ReadAllText(guidePath));
            _wizardHintText.Text = $"Loaded: {Path.GetFileName(guidePath)}";
        }
        else
        {
            _guideDirectory = string.Empty;
            _pages = [new WizardPage("Setup guide not found", "Expected file: SETUP_GUIDE_WIZARD.md")];
            _wizardHintText.Text = "Guide file is missing.";
        }

        RenderCurrentPage();
    }

    private T RequireControl<T>(string name) where T : Control
    {
        var control = this.FindControl<T>(name);
        if (control is null)
            throw new InvalidOperationException($"SetupWizardWindow is missing required control: {name}");
        return control;
    }

    private void OnBackClicked(object? sender, RoutedEventArgs e)
    {
        if (_currentIndex <= 0)
            return;

        _currentIndex--;
        RenderCurrentPage();
    }

    private void OnNextClicked(object? sender, RoutedEventArgs e)
    {
        if (_currentIndex >= _pages.Count - 1)
        {
            Close();
            return;
        }

        _currentIndex++;
        RenderCurrentPage();
    }

    private void RenderCurrentPage()
    {
        var page = _pages[_currentIndex];
        _wizardTitleText.Text = page.Title;
        _wizardStepText.Text = $"Step {_currentIndex + 1} of {_pages.Count}";
        _backButton.IsEnabled = _currentIndex > 0;
        _nextButton.IsEnabled = true;
        _nextButton.Content = _currentIndex < _pages.Count - 1 ? "Next" : "Done";

        _contentPanel.Children.Clear();
        BuildFormattedContent(page.Body, _contentPanel, _guideDirectory);
    }

    private static IReadOnlyList<WizardPage> ParseWizardPages(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var pages = new List<WizardPage>();
        var currentTitle = string.Empty;
        var bodyBuilder = new StringBuilder();

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                if (!string.IsNullOrWhiteSpace(currentTitle))
                    pages.Add(new WizardPage(currentTitle, bodyBuilder.ToString().Trim()));

                currentTitle = line[3..].Trim();
                bodyBuilder.Clear();
                continue;
            }

            if (!string.IsNullOrWhiteSpace(currentTitle))
                bodyBuilder.AppendLine(rawLine);
        }

        if (!string.IsNullOrWhiteSpace(currentTitle))
            pages.Add(new WizardPage(currentTitle, bodyBuilder.ToString().Trim()));

        if (pages.Count > 0)
            return pages;

        return [new WizardPage("Setup Guide", markdown)];
    }

    private static void BuildFormattedContent(string content, Panel target, string guideDirectory)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var inCodeBlock = false;
        var codeBuilder = new StringBuilder();

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();

            if (line.Trim() == "```")
            {
                if (inCodeBlock)
                {
                    AddCodeBlock(target, codeBuilder.ToString().TrimEnd());
                    codeBuilder.Clear();
                    inCodeBlock = false;
                }
                else
                {
                    inCodeBlock = true;
                }
                continue;
            }

            if (inCodeBlock)
            {
                codeBuilder.AppendLine(rawLine);
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                target.Children.Add(new Border { Height = 4 });
                continue;
            }

            if (line == "---")
            {
                target.Children.Add(new Separator { Margin = new Thickness(0, 4, 0, 4) });
                continue;
            }

            if (TryAddImage(target, line, guideDirectory))
                continue;

            if (TryAddExternalLink(target, line))
                continue;

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                target.Children.Add(new TextBlock
                {
                    Text = line[4..].Trim(),
                    FontSize = 15,
                    FontWeight = FontWeight.SemiBold,
                    Margin = new Thickness(0, 8, 0, 2),
                    TextWrapping = TextWrapping.Wrap
                });
                continue;
            }

            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                target.Children.Add(new TextBlock
                {
                    Text = $"• {line[2..].Trim()}",
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(6, 0, 0, 0)
                });
                continue;
            }

            if (Regex.IsMatch(line, @"^\d+\.\s+"))
            {
                target.Children.Add(new TextBlock
                {
                    Text = line,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(6, 0, 0, 0)
                });
                continue;
            }

            target.Children.Add(new TextBlock
            {
                Text = line,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });
        }

        if (inCodeBlock && codeBuilder.Length > 0)
            AddCodeBlock(target, codeBuilder.ToString().TrimEnd());
    }

    private static bool TryAddExternalLink(Panel target, string line)
    {
        var match = Regex.Match(line, @"^\s*(?:-\s+)?\[(?<label>[^\]]+)\]\((?<url>https?://[^)]+)\)$");
        if (!match.Success)
            return false;

        var label = match.Groups["label"].Value.Trim();
        var url = match.Groups["url"].Value.Trim();
        if (string.IsNullOrWhiteSpace(url))
            return false;

        var button = new Button
        {
            Content = string.IsNullOrWhiteSpace(label) ? url : label,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Foreground = Brushes.DodgerBlue,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 2),
            FontSize = 12
        };

        button.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch
            {
                // Ignore launch failure; keep wizard flow uninterrupted.
            }
        };

        target.Children.Add(button);
        return true;
    }

    private static void AddCodeBlock(Panel target, string code)
    {
        target.Children.Add(new Border
        {
            BorderBrush = Brushes.LightGray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 4, 0, 4),
            Background = new SolidColorBrush(Color.Parse("#F7F7F7")),
            Child = new TextBlock
            {
                Text = code,
                FontFamily = FontFamily.Parse("Consolas"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            }
        });
    }

    private static bool TryAddImage(Panel target, string line, string guideDirectory)
    {
        var match = Regex.Match(line, @"^!\[(?<alt>[^\]]*)\]\((?<path>[^)]+)\)$");
        if (!match.Success)
            return false;

        var alt = match.Groups["alt"].Value.Trim();
        var relativePath = match.Groups["path"].Value.Trim();
        var imagePath = ResolveImagePath(relativePath, guideDirectory);

        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            target.Children.Add(new TextBlock
            {
                Text = $"[Image not found] {relativePath}",
                Foreground = Brushes.DarkOrange,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            });
            return true;
        }

        try
        {
            using var fs = File.OpenRead(imagePath);
            var bitmap = new Bitmap(fs);
            target.Children.Add(new Image
            {
                Source = bitmap,
                Stretch = Stretch.Uniform,
                MaxHeight = 420,
                Margin = new Thickness(0, 6, 0, 4)
            });
            if (!string.IsNullOrWhiteSpace(alt))
            {
                target.Children.Add(new TextBlock
                {
                    Text = alt,
                    FontSize = 11,
                    Foreground = Brushes.Gray,
                    Margin = new Thickness(0, 0, 0, 4),
                    TextWrapping = TextWrapping.Wrap
                });
            }
        }
        catch
        {
            target.Children.Add(new TextBlock
            {
                Text = $"[Image load failed] {relativePath}",
                Foreground = Brushes.DarkOrange,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            });
        }

        return true;
    }

    private static string ResolveImagePath(string value, string guideDirectory)
    {
        if (Path.IsPathRooted(value))
            return value;

        if (string.IsNullOrWhiteSpace(guideDirectory))
            return string.Empty;

        return Path.GetFullPath(Path.Combine(guideDirectory, value));
    }

    private sealed record WizardPage(string Title, string Body);
}
