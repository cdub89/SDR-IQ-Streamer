using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SDRIQStreamer.FlexRadio;

namespace SDRIQStreamer.App;

/// <summary>A panadapter paired with the slices that belong to it.</summary>
public partial class PanSliceGroup : ObservableObject
{
    [ObservableProperty]
    private PanadapterInfo _pan;

    [ObservableProperty]
    private string _streamSummary = string.Empty;

    public ObservableCollection<SliceViewModel> Slices { get; } = new();

    public PanSliceGroup(PanadapterInfo pan) => _pan = pan;
}

/// <summary>All panadapters (and their slices) belonging to one connected GUI client.</summary>
public sealed class ClientGroup
{
    public string Station { get; }
    public ObservableCollection<PanSliceGroup> Panadapters { get; } = new();

    public ClientGroup(string station) => Station = station;
}
