using System.Diagnostics;
using System.Text;

namespace PCL.Horizon;

/// <summary>一次运行的结果：退出码与完整的 stdout、stderr。</summary>
internal sealed record HorizonProcessResult(int ExitCode, IReadOnlyList<string> Stdout, IReadOnlyList<string> Stderr);

/// <summary>以参数列表（不经 shell）运行 Horizon 程序，UTF-8 读取输出，取消时结束整个进程树。</summary>
internal static class HorizonProcess
{
    internal static readonly UTF8Encoding Utf8 = new(false);

    internal static ProcessStartInfo CreateStartInfo(string exe, IEnumerable<string> args, string workingDirectory)
    {
        var info = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Utf8,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        return info;
    }

    internal static async Task<HorizonProcessResult> RunAsync(
        string exe, IEnumerable<string> args, string workingDirectory,
        Action<string>? onStdoutLine = null, CancellationToken cancellationToken = default)
    {
        using var process = new Process { StartInfo = CreateStartInfo(exe, args, workingDirectory) };
        var stdout = new List<string>();
        var stderr = new List<string>();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (stdout) stdout.Add(e.Data);
            onStdoutLine?.Invoke(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (stderr) stderr.Add(e.Data);
        };
        process.Start();
        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            throw;
        }
        return new HorizonProcessResult(process.ExitCode, stdout, stderr);
    }

    internal static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }
}
