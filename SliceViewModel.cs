using CommunityToolkit.Mvvm.ComponentModel;
using SDRIQStreamer.FlexRadio;

namespace SDRIQStreamer.App;

/// <summary>
/// Presentation wrapper around <see cref="SliceInfo"/>.
/// </summary>
public partial class SliceViewModel : ObservableObject
{
    public SliceInfo Slice { get; private set; }
    public int DaxIqChannel { get; private set; }

    public string DisplayLabel  => Slice.DisplayLabel;
    public string ClientStation => Slice.ClientStation;
    public bool HasDaxIqChannel => DaxIqChannel > 0;

    [ObservableProperty]
    private bool _isSkimmerRunning;

    public SliceViewModel(SliceInfo slice, int daxIqChannel)
    {
        Slice = slice;
        DaxIqChannel = daxIqChannel;
    }

    public void Update(SliceInfo updated)
    {
        Slice = updated;
        OnPropertyChanged(nameof(DisplayLabel));
    }

    public void UpdateDaxIqChannel(int daxIqChannel)
    {
        if (DaxIqChannel == daxIqChannel)
            return;

        DaxIqChannel = daxIqChannel;
        OnPropertyChanged(nameof(HasDaxIqChannel));
    }
}
