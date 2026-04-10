using System;
using System.Collections.ObjectModel;

namespace SDRIQStreamer.App;

/// <summary>
/// Maintains a bounded footer status log and returns the display text.
/// </summary>
public sealed class FooterStatusBuffer
{
    private readonly ObservableCollection<string> _lines;
    private readonly int _maxLines;

    public FooterStatusBuffer(ObservableCollection<string> lines, int maxLines = 60)
    {
        _lines = lines;
        _maxLines = maxLines;
    }

    public string Add(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return string.Join(Environment.NewLine, _lines);

        _lines.Add($"{DateTime.Now:HH:mm:ss}  {message}");
        while (_lines.Count > _maxLines)
            _lines.RemoveAt(0);

        return string.Join(Environment.NewLine, _lines);
    }
}
