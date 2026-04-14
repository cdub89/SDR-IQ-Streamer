namespace SDRIQStreamer.CWSkimmer;

public static class FrequencyMath
{
    public static double SnapMHzToStepHz(double frequencyMHz, int stepHz)
    {
        if (stepHz <= 0)
            return frequencyMHz;

        var hz = frequencyMHz * 1_000_000d;
        var snappedHz = Math.Round(hz / stepHz, MidpointRounding.AwayFromZero) * stepHz;
        return snappedHz / 1_000_000d;
    }
}
