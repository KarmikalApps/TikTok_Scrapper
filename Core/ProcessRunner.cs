using System.Diagnostics;
using System.Text;

namespace TikTokScrapper.Core;

public interface IProcessRunner
{
    Task<int> RunAsync(string executable, IEnumerable<string> arguments, Action<string> output, Action<string> error, CancellationToken cancellationToken);
}

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<int> RunAsync(string executable, IEnumerable<string> arguments, Action<string> output, Action<string> error, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        process.Start();
        using var registration = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        });
        var stdout = PumpAsync(process.StandardOutput, output);
        var stderr = PumpAsync(process.StandardError, error);
        await Task.WhenAll(stdout, stderr, process.WaitForExitAsync()).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return process.ExitCode;
    }
    private static async Task PumpAsync(System.IO.StreamReader reader, Action<string> receive)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line) receive(line);
    }
}
