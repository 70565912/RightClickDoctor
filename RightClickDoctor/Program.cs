using System.Text;
using System.Text.Json;
using RightClickDoctor.Probing;
using RightClickDoctor.Registry;
using RightClickDoctor.Reports;
using RightClickDoctor.UI;

namespace RightClickDoctor;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            TrySetConsoleEncoding();

            if (args.Length > 0 && args[0].Equals("--probe", StringComparison.OrdinalIgnoreCase))
            {
                return ProbeChild.Run(args.Length > 1 ? args[1] : string.Empty);
            }

            if (args.Length > 0 && args[0].Equals("--scan-json", StringComparison.OrdinalIgnoreCase))
            {
                var entries = new ShellEntryScanner().Scan();
                var json = JsonSerializer.Serialize(entries, ReportWriter.JsonOptions);
                if (args.Length > 1)
                {
                    File.WriteAllText(args[1], json, Encoding.UTF8);
                }
                else
                {
                    Console.WriteLine(json);
                }

                return 0;
            }

            if (args.Length > 0 && args[0].Equals("--probe-report", StringComparison.OrdinalIgnoreCase))
            {
                var outputPath = args.Length > 1 ? args[1] : "right-click-probe-report.json";
                var timeoutSeconds = args.Length > 2 && double.TryParse(args[2], out var parsedTimeout)
                    ? Math.Clamp(parsedTimeout, 1, 60)
                    : 8;
                var samplePath = args.Length > 3
                    ? args[3]
                    : Path.Combine(Path.GetTempPath(), "RightClickDoctor sample.txt");
                if (!Directory.Exists(samplePath) && !File.Exists(samplePath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(samplePath)) ?? Path.GetTempPath());
                    File.WriteAllText(samplePath, "RightClickDoctor context menu probe sample.", Encoding.UTF8);
                }

                var entries = new ShellEntryScanner().Scan().ToList();
                var candidates = entries.Where(entry => entry.CanProbe).ToList();
                var progress = new Progress<ProbeProgress>(probeProgress =>
                {
                    Console.Error.WriteLine($"[{probeProgress.Completed}/{probeProgress.Total}] {probeProgress.Entry.DisplayName}: {probeProgress.Entry.Risk} {probeProgress.Entry.TotalMsText} ms");
                });

                new ProbeRunner()
                    .ProbeAsync(candidates, new[] { samplePath }, TimeSpan.FromSeconds(timeoutSeconds), progress, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                ReportWriter.Write(outputPath, entries);
                return 0;
            }

            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            return 0;
        }
        catch (Exception ex)
        {
            if (args.Length == 0 && Environment.UserInteractive)
            {
                MessageBox.Show(ex.ToString(), "RightClickDoctor", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else
            {
                Console.Error.WriteLine(ex);
            }

            return 1;
        }
    }

    private static void TrySetConsoleEncoding()
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (IOException)
        {
            // WinExe launches can have no attached console handle.
        }
    }
}
