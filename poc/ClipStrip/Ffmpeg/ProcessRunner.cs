using System.ComponentModel;
using System.Diagnostics;

namespace ClipStrip.Ffmpeg;

public sealed record ProcessResult(int ExitCode, byte[] Output, string Errors);

// Thrown for every way a tool call can fail: tool missing, timed out, or the file rejected.
public sealed class MediaToolException(string message) : Exception(message);

// Runs ffmpeg/ffprobe as a child process. A call can never hang the caller: past the timeout the
// process is killed and a MediaToolException is thrown.
public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(string tool, IEnumerable<string> arguments, TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo(tool)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Win32Exception e)
        {
            throw new MediaToolException($"Cannot start {tool}: {e.Message} Pass --ffmpeg <folder>.");
        }

        // Nothing is ever typed into the tool: a closed input makes any prompt fail instead of wait.
        process.StandardInput.Close();

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        using var output = new MemoryStream();
        var reading = process.StandardOutput.BaseStream.CopyToAsync(output, limit.Token);
        var errors = process.StandardError.ReadToEndAsync(limit.Token);
        try
        {
            await process.WaitForExitAsync(limit.Token);
            await Task.WhenAll(reading, errors);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            cancellationToken.ThrowIfCancellationRequested();
            throw new MediaToolException($"{Path.GetFileName(tool)} gave no answer within {timeout.TotalSeconds:0} s");
        }

        return new ProcessResult(process.ExitCode, output.ToArray(), errors.Result.Trim());
    }
}
