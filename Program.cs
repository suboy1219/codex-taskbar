using System.Text.Json;
using System.Threading;
using System.Windows.Forms;

namespace CodexTaskbar;

internal static class Program
{
    private const string MutexName = "Local\\CodexTaskbar.SingleInstance";

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--check")
        {
            Environment.Exit(RunCheck(args[1]));
            return;
        }

        using var mutex = new Mutex(true, MutexName, out var isNew);
        if (!isNew)
        {
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Application.ThreadException += (_, e) => Log.Write(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write(e.ExceptionObject as Exception);

        var silent = args.Any(a => string.Equals(a, "--silent", StringComparison.OrdinalIgnoreCase));
        Application.Run(new TrayContext(silent));
    }

    private static int RunCheck(string outputPath)
    {
        try
        {
            var snapshot = new UsageService().GetUsageAsync().GetAwaiter().GetResult();
            File.WriteAllText(outputPath, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(outputPath, JsonSerializer.Serialize(new { ok = false, error = ex.Message }, new JsonSerializerOptions { WriteIndented = true }));
            return 1;
        }
    }
}
