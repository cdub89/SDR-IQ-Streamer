using SDRIQStreamer.CWSkimmer;

namespace SDRIQStreamer.CWSkimmer.Tests;

/// <summary>
/// Fake IAudioDeviceFinder for unit tests — no real audio hardware required.
/// </summary>
internal sealed class FakeAudioDeviceFinder : IAudioDeviceFinder
{
    private readonly Dictionary<string, int> _devices;

    public FakeAudioDeviceFinder(Dictionary<string, int> devices) => _devices = devices;

    public int FindCaptureDeviceIndex(string nameFragment)
    {
        foreach (var (key, idx) in _devices)
            if (key.Contains(nameFragment, StringComparison.OrdinalIgnoreCase))
                return idx;
        return -1;
    }

    public IReadOnlyList<(int CwSkimmerIndex, string Name)> ListAllCaptureDevices()
        => _devices.Select(kv => (kv.Value, kv.Key)).ToList();
}

public sealed class CwSkimmerIniWriterTests
{
    private static CwSkimmerConfig DefaultConfig() => new()
    {
        Callsign   = "WX7V",
        TelnetPort = 7310,
    };

    private static CwSkimmerIniModel MakeModel(
        int wdmSignal    = 8,
        int wdmAudio     = 14,
        int sampleRate   = 48000,
        long centerFreq  = 14_048_441L,
        CwSkimmerConfig? cfg = null)
        => new(wdmSignal, wdmAudio, sampleRate, centerFreq, cfg ?? DefaultConfig());

    // ── [Audio] section ───────────────────────────────────────────────────────

    [Fact]
    public void Write_DoesNotInjectWindowsSection_WhenMissing()
    {
        var text = WriteToString(MakeModel());

        Assert.DoesNotContain("[Windows]", text);
        Assert.DoesNotContain("Colors=1", text);
    }

    [Fact]
    public void Write_AudioSection_ContainsExpectedDeviceIndices()
    {
        var text = WriteToString(MakeModel(wdmSignal: 8, wdmAudio: 14));

        Assert.Contains("[Audio]",          text);
        Assert.Contains("WdmSignalDev=8",   text);
        Assert.Contains("WdmAudioDev=14",   text);
        Assert.Contains("MmeSignalDev=0",   text);
        Assert.Contains("MmeAudioDev=0",    text);
        Assert.Contains("UseWdm=1",         text);
    }

    // [Radio], [sdrSR], and [Recorder] are CW Skimmer-owned and intentionally preserved.

    // ── [Telnet] section ──────────────────────────────────────────────────────

    [Fact]
    public void Write_TelnetSection_DefaultsMatchReference()
    {
        var text = WriteToString(MakeModel());

        Assert.Contains("[Telnet]",            text);
        Assert.Contains("Port=7310",           text);
        Assert.Contains("PasswordRequired=0",  text);
        Assert.Contains("Password=",           text);
        Assert.Contains("AnnUserOnly=0",       text);
        Assert.Contains("AnnUser=",            text);
        Assert.Contains("TelnetSrvEnabled=1",  text);
        Assert.Contains("UdpEnabled=0",        text);
    }

    [Fact]
    public void Write_TelnetSection_NoPassword_WhenDisabled()
    {
        var cfg  = DefaultConfig() with { TelnetPasswordRequired = false };
        var text = WriteToString(MakeModel(cfg: cfg));

        Assert.Contains("PasswordRequired=0", text);
        Assert.Contains("AnnUserOnly=0",       text);
    }

    // ── Section ordering ──────────────────────────────────────────────────────

    [Fact]
    public void Write_SectionOrder_AudioBeforeTelnet()
    {
        var text  = WriteToString(MakeModel());
        int audio = text.IndexOf("[Audio]", StringComparison.Ordinal);
        int telnet = text.IndexOf("[Telnet]", StringComparison.Ordinal);

        Assert.True(audio < telnet, "[Audio] should precede [Telnet]");
    }

    [Fact]
    public void Write_PreservesWindowsAndRecorderSections_WhenAlreadyPresent()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """
[Windows]
Left=418
Top=601
Width=546
Height=416
Colors=1

[Recorder]
WavCall=OLDCALL
WavOper=Old Name

[Audio]
WdmSignalDev=1
WdmAudioDev=2
""");

            var writer = new CwSkimmerIniWriter();
            writer.Write(MakeModel(), path);
            var text = File.ReadAllText(path);

            Assert.Contains("[Windows]", text);
            Assert.Contains("Left=418", text);
            Assert.Contains("Top=601", text);
            Assert.Contains("Width=546", text);
            Assert.Contains("Height=416", text);

            Assert.Contains("[Recorder]", text);
            Assert.Contains("WavCall=OLDCALL", text);
            Assert.Contains("WavOper=Old Name", text);

            Assert.Contains("WdmSignalDev=8", text);
            Assert.Contains("WdmAudioDev=14", text);
            Assert.DoesNotContain("[sdrSR]", text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static string WriteToString(CwSkimmerIniModel model)
    {
        var path   = Path.GetTempFileName();
        var writer = new CwSkimmerIniWriter();
        try
        {
            writer.Write(model, path);
            return File.ReadAllText(path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

/// <summary>
/// Tests for CwSkimmerTelnetClient's click-event parser.
/// No network connection required.
/// </summary>
public sealed class CwSkimmerTelnetClientTests
{
    [Theory]
    [InlineData(@"To ALL de SKIMMER 1234 : Clicked on ""W1AW"" at 14012.5",  14012.5)]
    [InlineData(@"To ALL de SKIMMER 9999 : Clicked on ""VK2GR"" at 7025.0",   7025.0)]
    [InlineData(@"To ALL de SKIMMER 0001 : Clicked on ""K1DBO"" at 3512.75",  3512.75)]
    public void ParseClickedOn_ValidLine_ReturnsFreqKhz(string line, double expectedKhz)
    {
        var result = CwSkimmerTelnetClient.ParseClickedOn(line);
        Assert.NotNull(result);
        Assert.Equal(expectedKhz, result!.Value, precision: 3);
    }

    [Theory]
    [InlineData("To ALL de SKIMMER 1234 : DX de W1AW 14012.5")]   // no "Clicked on"
    [InlineData("")]
    [InlineData("SKIMMER/LO_FREQ 7060000")]
    public void ParseClickedOn_NonClickLine_ReturnsNull(string line)
    {
        Assert.Null(CwSkimmerTelnetClient.ParseClickedOn(line));
    }

    [Fact]
    public void ParseDxSpot_ValidLine_ReturnsStructuredSpot()
    {
        const string line = "DX de K1ABC-#: 14015.3 9A3B 19 dB 25 WPM CQ 1534Z";
        var spot = CwSkimmerTelnetClient.ParseDxSpot(line);

        Assert.NotNull(spot);
        Assert.Equal(14015.3, spot!.FrequencyKhz, 3);
        Assert.Equal("9A3B", spot.Callsign);
        Assert.Equal("K1ABC-#", spot.Spotter);
        Assert.Equal(19, spot.SignalDb);
        Assert.Equal(25, spot.SpeedWpm);
        Assert.Equal("19 dB 25 WPM CQ 1534Z", spot.Comment);
    }

    [Fact]
    public void ParseDxSpot_LeadingWhitespace_ReturnsStructuredSpot()
    {
        const string line = "   DX de K1ABC-#: 7054.95 W7XYZ 22 dB 31 WPM TEST 0910Z";
        var spot = CwSkimmerTelnetClient.ParseDxSpot(line);

        Assert.NotNull(spot);
        Assert.Equal(7054.95, spot!.FrequencyKhz, 3);
        Assert.Equal("W7XYZ", spot.Callsign);
        Assert.Equal(22, spot.SignalDb);
        Assert.Equal(31, spot.SpeedWpm);
    }

    [Fact]
    public void ParseDxSpot_TabDelimitedLine_ReturnsStructuredSpot()
    {
        const string line = "DX de N0CALL-#:\t14015.3\t9A3B\t19 dB\t25 WPM\tCQ\t1534Z";
        var spot = CwSkimmerTelnetClient.ParseDxSpot(line);

        Assert.NotNull(spot);
        Assert.Equal(14015.3, spot!.FrequencyKhz, 3);
        Assert.Equal("9A3B", spot.Callsign);
        Assert.Equal("N0CALL-#", spot.Spotter);
        Assert.Equal(19, spot.SignalDb);
        Assert.Equal(25, spot.SpeedWpm);
    }

    [Theory]
    [InlineData("DX de K1ABC-#: BADFREQ 9A3B 19 dB")]
    [InlineData("DX de K1ABC-#: 14015.3")]
    [InlineData("To ALL de SKIMMER 1234 : Clicked on \"W1AW\" at 14012.5")]
    public void ParseDxSpot_InvalidOrNonSpotLine_ReturnsNull(string line)
    {
        Assert.Null(CwSkimmerTelnetClient.ParseDxSpot(line));
    }
}

public sealed class CwSkimmerIniModelFactoryTests
{
    [Fact]
    public void Build_ResolvesSignalAndAudioDevicesByName()
    {
        var finder = new FakeAudioDeviceFinder(new Dictionary<string, int>
        {
            ["DAX IQ RX 1"]    = 8,
            ["DAX Audio RX 1"] = 14,
        });

        var factory = new CwSkimmerIniModelFactory(finder);
        var model   = factory.Build(daxIqChannel: 1, sampleRateHz: 48000,
                                    centerFreqHz: 14_048_441L, config: new CwSkimmerConfig());

        Assert.Equal(8,           model.WdmSignalDevIndex);
        Assert.Equal(14,          model.WdmAudioDevIndex);
        Assert.Equal(48000,       model.SampleRateHz);
        Assert.Equal(14_048_441L, model.CenterFreqHz);
    }

    [Fact]
    public void Build_Channel2_ResolvesChannel2Devices()
    {
        var finder = new FakeAudioDeviceFinder(new Dictionary<string, int>
        {
            ["DAX IQ RX 2"]    = 9,
            ["DAX Audio RX 2"] = 15,
        });

        var factory = new CwSkimmerIniModelFactory(finder);
        var model   = factory.Build(daxIqChannel: 2, sampleRateHz: 48000,
                                    centerFreqHz: 7_040_000L, config: new CwSkimmerConfig());

        Assert.Equal(9,  model.WdmSignalDevIndex);
        Assert.Equal(15, model.WdmAudioDevIndex);
    }

    [Fact]
    public void Build_ReturnsNegativeOne_WhenDeviceNotFound()
    {
        var finder  = new FakeAudioDeviceFinder(new Dictionary<string, int>());
        var factory = new CwSkimmerIniModelFactory(finder);
        var model   = factory.Build(1, 48000, 14_000_000L, new CwSkimmerConfig());

        Assert.Equal(-1, model.WdmSignalDevIndex);
        Assert.Equal(-1, model.WdmAudioDevIndex);
    }
}

public sealed class FrequencyMathTests
{
    [Theory]
    [InlineData(14.055090, 100, 14.055100)]
    [InlineData(14.055010, 100, 14.055000)]
    [InlineData(14.055490, 100, 14.055500)]
    [InlineData(14.055090, 50, 14.055100)]
    [InlineData(14.055010, 50, 14.055000)]
    [InlineData(14.055025, 50, 14.055050)]
    [InlineData(7.025149, 100, 7.025100)]
    [InlineData(7.025150, 100, 7.025200)]
    public void SnapMHzToStepHz_RoundsToNearestStep(double inputMHz, int stepHz, double expectedMHz)
    {
        var result = FrequencyMath.SnapMHzToStepHz(inputMHz, stepHz);
        Assert.Equal(expectedMHz, result, precision: 6);
    }
}
