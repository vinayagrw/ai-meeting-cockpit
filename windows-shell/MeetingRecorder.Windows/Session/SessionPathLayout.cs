namespace MeetingRecorder.Windows.Session;

public static class SessionPathLayout
{
    public const string InternalDirectoryName = "_session";

    public static string InternalDirectory(string sessionDirectory)
        => Path.Combine(sessionDirectory, InternalDirectoryName);

    public static string InternalArtifact(string sessionDirectory, string fileName)
        => Path.Combine(InternalDirectory(sessionDirectory), fileName);

    public static string Resolve(string sessionDirectory, string fileName)
    {
        var internalPath = InternalArtifact(sessionDirectory, fileName);
        if (File.Exists(internalPath))
        {
            return internalPath;
        }

        var rootPath = Path.Combine(sessionDirectory, fileName);
        return File.Exists(rootPath) ? rootPath : internalPath;
    }
}
