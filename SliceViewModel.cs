using CommunityToolkit.Mvvm.ComponentModel;
using SDRIQStreamer.FlexRadio;

namespace SDRIQStreamer.App;

/// <summary>
/// Presentation wrapper around <see cref="SliceInfo"/>.
/// </summary>
public partial class SliceViewModel : ObservableObject
{
    public SliceInfo Slice { get; private set; }

    public string DisplayLabel  => Slice.DisplayLabel;
    public string ClientStation => Slice.ClientStation;

    public SliceViewModel(SliceInfo slice) => Slice = slice;

    public void Update(SliceInfo updated)
    {
        Slice = updated;
        OnPropertyChanged(nameof(DisplayLabel));
    }
}
