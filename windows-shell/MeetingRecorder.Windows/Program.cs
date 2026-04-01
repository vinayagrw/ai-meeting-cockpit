namespace MeetingRecorder.Windows;

static class Program
{
    [STAThread]
    static void Main()
    {
        var startupLogDirectory = Path.Combine(AppContext.BaseDirectory, "meetings-data", "logs");
        var startupTracePath = Path.Combine(startupLogDirectory, "dotnet-startup-trace.log");
        var startupErrorPath = Path.Combine(startupLogDirectory, "dotnet-startup-error.log");

        try
        {
            Directory.CreateDirectory(startupLogDirectory);
            ApplicationConfiguration.Initialize();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, errorArgs) =>
            {
                try
                {
                    Directory.CreateDirectory(startupLogDirectory);
                    File.AppendAllText(
                        startupErrorPath,
                        $"{DateTimeOffset.Now:O}{Environment.NewLine}ThreadException{Environment.NewLine}{errorArgs.Exception}{Environment.NewLine}{Environment.NewLine}");
                }
                catch
                {
                }
            };
            AppDomain.CurrentDomain.UnhandledException += (_, errorArgs) =>
            {
                try
                {
                    Directory.CreateDirectory(startupLogDirectory);
                    File.AppendAllText(
                        startupErrorPath,
                        $"{DateTimeOffset.Now:O}{Environment.NewLine}UnhandledException{Environment.NewLine}{errorArgs.ExceptionObject}{Environment.NewLine}{Environment.NewLine}");
                }
                catch
                {
                }
            };
            File.AppendAllText(startupTracePath, $"{DateTimeOffset.Now:O} Main entered{Environment.NewLine}");
            File.AppendAllText(startupTracePath, $"{DateTimeOffset.Now:O} Application configuration initialized{Environment.NewLine}");
            Application.Run(new Shell.RecorderApplicationContext());
            File.AppendAllText(startupTracePath, $"{DateTimeOffset.Now:O} Application.Run exited cleanly{Environment.NewLine}");
        }
        catch (Exception error)
        {
            try
            {
                Directory.CreateDirectory(startupLogDirectory);
                File.WriteAllText(startupErrorPath, $"{DateTimeOffset.Now:O}{Environment.NewLine}{error}");
            }
            catch
            {
            }

            throw;
        }
    }
}
