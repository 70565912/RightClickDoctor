using System.Diagnostics;
using System.Text.Json;
using RightClickDoctor.Models;
using RightClickDoctor.Reports;

namespace RightClickDoctor.Probing;

public sealed class ProbeRunner
{
    public async Task ProbeAsync(
        IReadOnlyList<ShellEntry> entries,
        IReadOnlyList<string> samplePaths,
        TimeSpan timeout,
        IProgress<ProbeProgress>? progress,
        CancellationToken cancellationToken)
    {
        var total = entries.Count;
        for (var i = 0; i < entries.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = entries[i];
            var result = await ProbeOneAsync(entry, samplePaths, timeout, cancellationToken).ConfigureAwait(false);
            entry.ApplyProbe(result);
            progress?.Report(new ProbeProgress(i + 1, total, entry, result));
        }
    }

    private static async Task<ProbeResult> ProbeOneAsync(
        ShellEntry entry,
        IReadOnlyList<string> samplePaths,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (!entry.Clsid.HasValue)
        {
            return new ProbeResult
            {
                Name = entry.DisplayName,
                Success = false,
                Error = "No CLSID to probe."
            };
        }

        var request = new ProbeRequest
        {
            Clsid = entry.Clsid.Value,
            Name = entry.DisplayName,
            SamplePaths = samplePaths.ToArray()
        };

        var requestPath = Path.Combine(Path.GetTempPath(), $"RightClickDoctor-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(request, ReportWriter.JsonOptions), cancellationToken).ConfigureAwait(false);

        try
        {
            var hostPath = Environment.ProcessPath
                ?? throw new InvalidOperationException("Cannot determine current executable path.");
            var managedEntryPath = Environment.GetCommandLineArgs().FirstOrDefault() ?? string.Empty;
            var hostName = Path.GetFileName(hostPath);
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(hostPath)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = AppContext.BaseDirectory
                },
                EnableRaisingEvents = true
            };
            if (hostName.Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase)
                || hostName.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(managedEntryPath))
                {
                    throw new InvalidOperationException("Cannot determine managed entry assembly path for dotnet-hosted probe.");
                }

                process.StartInfo.ArgumentList.Add(managedEntryPath);
            }

            process.StartInfo.ArgumentList.Add("--probe");
            process.StartInfo.ArgumentList.Add(requestPath);

            var started = process.Start();
            if (!started)
            {
                throw new InvalidOperationException("Failed to start probe child process.");
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            var exitedTask = process.WaitForExitAsync(cancellationToken);
            var completed = await Task.WhenAny(exitedTask, Task.Delay(timeout, cancellationToken)).ConfigureAwait(false);
            if (completed != exitedTask)
            {
                TryKill(process);
                return new ProbeResult
                {
                    Clsid = entry.Clsid.Value,
                    Name = entry.DisplayName,
                    TimedOut = true,
                    Success = false,
                    Error = $"Probe exceeded {timeout.TotalSeconds:0.#} seconds."
                };
            }

            await exitedTask.ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);
            var jsonLine = ExtractJsonObject(output);
            ProbeResult? result = null;
            if (!string.IsNullOrWhiteSpace(jsonLine))
            {
                result = JsonSerializer.Deserialize<ProbeResult>(jsonLine, ReportWriter.JsonOptions);
            }

            result ??= new ProbeResult
            {
                Clsid = entry.Clsid.Value,
                Name = entry.DisplayName,
                Success = false,
                Error = string.IsNullOrWhiteSpace(error) ? "Probe child returned no JSON result." : error.Trim()
            };

            result.ExitCode = process.ExitCode;
            if (process.ExitCode != 0 && result.Success)
            {
                result.Success = false;
                result.Error = $"Probe child exited with code {process.ExitCode}.";
            }

            return result;
        }
        finally
        {
            try
            {
                File.Delete(requestPath);
            }
            catch
            {
                // Temporary files are best effort.
            }
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // The process may have exited between checks.
        }
    }

    private static string ExtractJsonObject(string output)
    {
        var start = output.IndexOf('{');
        var end = output.LastIndexOf('}');
        return start >= 0 && end >= start
            ? output[start..(end + 1)]
            : string.Empty;
    }
}

public sealed record ProbeProgress(int Completed, int Total, ShellEntry Entry, ProbeResult Result);
