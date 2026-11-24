// Claude Code status-line shim for Battery.
//
// Claude Code pipes a JSON blob to the status-line command on every update. This program saves that
// blob to %AppData%\Battery\claude-status.json (written to a temp file, then swapped in, so a reader
// never sees half a file) and then, if a status-line command was already configured before Battery
// was connected (%AppData%\Battery\statusline-chain.txt), runs it with the same input and passes its
// output through, so the user's own status line keeps working. It prints nothing of its own, reads
// no credentials, and always exits 0.

using System.Diagnostics;

static class Program
{
    const int ChainTimeoutMs = 5000;

    static int Main()
    {
        try
        {
            var input = ReadAll(Console.OpenStandardInput());
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Battery");

            try { Save(dir, input); } catch { /* saving must never break the status line */ }
            try { Chain(dir, input); } catch { /* nor must a broken chained command */ }
        }
        catch
        {
            // Never fail: Claude Code only shows status-line output from a command that exits 0.
        }
        return 0;
    }

    static byte[] ReadAll(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    static void Save(string dir, byte[] input)
    {
        if (input.Length == 0) return;
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, "claude-status.json");
        var tmp = $"{target}.{Environment.ProcessId}.tmp";   // several Claude Code windows may run at once
        try
        {
            File.WriteAllBytes(tmp, input);
            File.Move(tmp, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    static void Chain(string dir, byte[] input)
    {
        var chainFile = Path.Combine(dir, "statusline-chain.txt");
        if (!File.Exists(chainFile)) return;
        var command = File.ReadAllText(chainFile).Trim().TrimStart('﻿');
        if (command.Length == 0 || command.Contains("Battery.StatusLine", StringComparison.OrdinalIgnoreCase)) return;

        // Run it the way Claude Code would have: Git Bash when we were started from it, else PowerShell.
        var psi = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
        };
        var bash = Environment.GetEnvironmentVariable("MSYSTEM") is { Length: > 0 } ? FindOnPath("bash.exe") : null;
        if (bash is not null)
        {
            psi.FileName = bash;
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(command);
        }
        else
        {
            psi.FileName = "powershell.exe";
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(command);
        }

        using var process = Process.Start(psi);
        if (process is null) return;

        var output = Task.Run(() => ReadAll(process.StandardOutput.BaseStream));
        try
        {
            process.StandardInput.BaseStream.Write(input, 0, input.Length);
            process.StandardInput.Close();
        }
        catch (IOException) { /* it did not want the input */ }

        if (!process.WaitForExit(ChainTimeoutMs))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return;
        }
        if (output.Wait(1000))
        {
            using var stdout = Console.OpenStandardOutput();
            stdout.Write(output.Result, 0, output.Result.Length);
            stdout.Flush();
        }
    }

    /// <summary>
    /// Resolves through PATH ourselves: CreateProcess looks in System32 first, where bash.exe is WSL's.
    /// </summary>
    static string? FindOnPath(string exe)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (dir.Length == 0) continue;
            try
            {
                var candidate = Path.Combine(dir.Trim(), exe);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException) { }
        }
        return null;
    }
}
