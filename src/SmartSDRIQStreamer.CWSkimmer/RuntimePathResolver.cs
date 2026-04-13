namespace SDRIQStreamer.CWSkimmer;

internal static class RuntimePathResolver
{
    public static string ResolveCwSkimmerIniDir()
        => Path.Combine(ResolveArtifactsRoot(), "cwskimmer", "ini");

    public static string ResolveLogsDir()
        => Path.Combine(ResolveArtifactsRoot(), "logs");

    private static string ResolveArtifactsRoot()
    {
        var repoRoot = TryFindRepoRoot(new DirectoryInfo(AppContext.BaseDirectory))
            ?? TryFindRepoRoot(new DirectoryInfo(Environment.CurrentDirectory));

        if (repoRoot is not null)
            return Path.Combine(repoRoot.FullName, "artifacts");

        var appDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SDRIQStreamer");
        return Path.Combine(appDataRoot, "artifacts");
    }

    private static DirectoryInfo? TryFindRepoRoot(DirectoryInfo? start)
    {
        var current = start;
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SmartSDRIQStreamer.csproj")) ||
                File.Exists(Path.Combine(current.FullName, "SmartSDRIQStreamer.slnx")))
            {
                return current;
            }

            current = current.Parent;
        }

        return null;
    }
}
