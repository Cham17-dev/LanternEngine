using System.Diagnostics;
using System.Text;
using BloodborneLauncher.Build;

// Build Launcher: keeps "Bloodborne Launcher.exe" in step with its sources.
//
//   1. fingerprints launcher/windows/BloodborneLauncher (+ Shared) and compares it with
//      LauncherApp/build-stamp.json, written by the last build;
//   2. if they differ, the exe is missing, or --force was given: publishes the launcher and
//      installs it over LauncherApp/Bloodborne Launcher.exe (the launcher must be closed);
//   3. does the same for this builder's own sources, replacing itself (a running exe can be
//      renamed, so the old one is moved aside and removed on the next run);
//   4. starts the launcher.
//
// Run without arguments and with nothing changed, it offers a forced rebuild for a few seconds.
//
//   --force        rebuild even if nothing changed
//   --check        only report whether a rebuild is needed (exit code 2 if it is)
//   --no-launch    do not start the launcher afterwards
//   --no-pause     do not wait for Enter at the end
//   --wait-pid N   wait for process N to exit first (the launcher's own "Rebuild" button)

namespace BloodborneLauncher.Builder;

internal static class Program
{
    private const string LauncherProcess = "Bloodborne Launcher";

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.Title = "Build Launcher";
        var noPause = args.Contains("--no-pause");
        var ok = false;
        try
        {
            var code = Run(args);
            ok = code == 0;
            return code;
        }
        catch (StepFailed e)
        {
            Bad(e.Message);
            return 1;
        }
        catch (Exception e)
        {
            Bad("Unexpected error: " + e.Message);
            return 1;
        }
        finally
        {
            // A successful run that starts the launcher closes by itself; anything else waits so
            // the messages can be read.
            if (!noPause && !(ok && !args.Contains("--no-launch") && !args.Contains("--check")))
            {
                Console.WriteLine();
                Console.Write("Press Enter to close.");
                Console.ReadLine();
            }
        }
    }

    private static int Run(string[] args)
    {
        var force = args.Contains("--force");
        var checkOnly = args.Contains("--check");
        var noLaunch = args.Contains("--no-launch");
        var interactive = args.Length == 0;

        var root = SourceStamp.LocateSources(AppContext.BaseDirectory)
                   ?? SourceStamp.FindSourceRoot(Environment.CurrentDirectory)
                   ?? throw new StepFailed("Could not find the launcher sources (launcher/windows/BloodborneLauncher above this exe).");
        var appDir = SourceStamp.AppDirOf(root);
        Directory.CreateDirectory(appDir);
        CleanUpOldSelf();

        Title("Bloodborne Launcher builder");
        Info($"  sources  {root}");
        Info($"  install  {appDir}");

        var pidAt = Array.IndexOf(args, "--wait-pid");
        if (pidAt >= 0 && pidAt + 1 < args.Length && int.TryParse(args[pidAt + 1], out var pid))
            WaitForExit(pid);

        Step("Checking for changes");
        var stamp = SourceStamp.Read(appDir);
        var launcherHash = SourceStamp.Compute(root, SourceStamp.LauncherProject);
        var builderHash = SourceStamp.Compute(root, SourceStamp.BuilderProject);
        var launcherExe = Path.Combine(appDir, SourceStamp.LauncherExe);
        var builderExe = Path.Combine(appDir, SourceStamp.BuilderExe);

        var launcherChanged = stamp.Launcher != launcherHash || !File.Exists(launcherExe);
        var builderChanged = stamp.Builder != builderHash || !File.Exists(builderExe);
        Info(launcherChanged ? "  launcher: changed since the last build" : "  launcher: up to date");
        Info(builderChanged ? "  builder:  changed since the last build" : "  builder:  up to date");

        if (checkOnly)
            return launcherChanged || builderChanged ? 2 : 0;

        if (!launcherChanged && !builderChanged && !force && interactive && AskForce())
            force = true;

        if (launcherChanged || force)
        {
            EnsureLauncherClosed(launcherExe, args.Contains("--no-pause"));
            Publish(root, SourceStamp.LauncherProject, SourceStamp.LauncherExe, launcherExe);
            stamp.Launcher = launcherHash;
            stamp.Source = root;
            stamp.Built = DateTime.UtcNow;
            SourceStamp.Write(appDir, stamp);
            Good($"Installed {launcherExe}");
        }

        if (builderChanged || force)
        {
            Publish(root, SourceStamp.BuilderProject, SourceStamp.BuilderExe, builderExe);
            stamp.Builder = builderHash;
            stamp.Source = root;
            SourceStamp.Write(appDir, stamp);
            Good($"Installed {builderExe}");
        }

        WriteForceScript(appDir);

        if (!noLaunch)
        {
            if (!File.Exists(launcherExe))
                throw new StepFailed("The launcher is not built yet.");
            Step("Starting the launcher");
            Process.Start(new ProcessStartInfo(launcherExe) { UseShellExecute = true, WorkingDirectory = appDir });
        }
        Good("Done.");
        return 0;
    }

    // ==================================================================== steps

    /// <summary>Nothing changed: a few seconds to ask for a rebuild anyway, then carry on.</summary>
    private static bool AskForce()
    {
        if (Console.IsInputRedirected)
            return false;
        Good("Everything is up to date.");
        Console.Write("Press F to force a rebuild, any other key to start the launcher now (starting in 5 s) ");
        var until = DateTime.Now.AddSeconds(5);
        while (DateTime.Now < until)
        {
            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(intercept: true).Key;
                Console.WriteLine();
                return key == ConsoleKey.F;
            }
            Thread.Sleep(50);
        }
        Console.WriteLine();
        return false;
    }

    private static void WaitForExit(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            Info($"  waiting for the launcher (pid {pid}) to close…");
            if (!process.WaitForExit(30000))
                throw new StepFailed("The launcher did not close; close it and run the builder again.");
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
    }

    /// <summary>Only the copy being replaced matters; a launcher run from elsewhere (a dev build) is left alone.</summary>
    private static Process[] Running(string exe) =>
        Process.GetProcessesByName(LauncherProcess).Where(p =>
        {
            try { return string.Equals(p.MainModule?.FileName, exe, StringComparison.OrdinalIgnoreCase); }
            catch { return true; } // cannot tell: assume it is ours
        }).ToArray();

    private static void EnsureLauncherClosed(string exe, bool noPause)
    {
        while (Running(exe).Length > 0)
        {
            if (noPause || Console.IsInputRedirected)
                throw new StepFailed("Bloodborne Launcher is open. Close it and run the builder again.");
            Warn("Bloodborne Launcher is open and its exe cannot be replaced.");
            Console.Write("Close it and press Enter, type k to close it for you, or q to quit: ");
            var answer = (Console.ReadLine() ?? "q").Trim().ToLowerInvariant();
            if (answer == "q")
                throw new StepFailed("Cancelled.");
            if (answer == "k")
                foreach (var p in Running(exe))
                {
                    try { p.CloseMainWindow(); p.WaitForExit(5000); if (!p.HasExited) p.Kill(); } catch { /* gone */ }
                }
        }
    }

    private static void Publish(string root, string project, string exeName, string target)
    {
        Step($"Building {exeName}");
        RequireDotnet();
        var output = Path.Combine(Path.GetTempPath(), "lantern-engine-publish", project);
        if (Directory.Exists(output))
            Directory.Delete(output, recursive: true);

        var csproj = Path.Combine(root, project, project + ".csproj");
        var (code, text) = Exec("dotnet", root,
            "publish", csproj, "-c", "Release", "-r", "win-x64", "--self-contained", "false",
            "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true", "-p:DebugType=none",
            "-o", output, "--nologo", "-v", "q");
        if (code != 0)
            throw new StepFailed($"Building {exeName} failed:\n" + Tail(text, 30));

        var built = Path.Combine(output, exeName);
        if (!File.Exists(built))
            throw new StepFailed($"The build did not produce {exeName}.");

        // A running exe cannot be overwritten, but it can be renamed: move this builder aside.
        var self = Environment.ProcessPath;
        if (self != null && string.Equals(Path.GetFullPath(self), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
        {
            var aside = target + ".old";
            if (File.Exists(aside)) File.Delete(aside);
            File.Move(target, aside);
        }
        File.Copy(built, target, overwrite: true);
    }

    private static void CleanUpOldSelf()
    {
        var self = Environment.ProcessPath;
        if (self == null) return;
        try
        {
            var aside = self + ".old";
            if (File.Exists(aside)) File.Delete(aside);
        }
        catch
        {
            // Still in use by an older instance; removed next time.
        }
    }

    private static void WriteForceScript(string appDir)
    {
        var script = Path.Combine(appDir, "Force Rebuild Launcher.cmd");
        const string text = "@echo off\r\nrem Rebuilds Bloodborne Launcher.exe even when its sources did not change.\r\n" +
                            "\"%~dp0Build Launcher.exe\" --force\r\n";
        if (!File.Exists(script) || File.ReadAllText(script) != text)
            File.WriteAllText(script, text);
    }

    private static void RequireDotnet()
    {
        try
        {
            var (code, text) = Exec("dotnet", Environment.CurrentDirectory, "--list-sdks");
            if (code == 0 && text.Split('\n').Any(l => l.TrimStart().StartsWith("8.") || l.TrimStart().StartsWith("9.") || l.TrimStart().StartsWith("10.")))
                return;
        }
        catch
        {
            // Not on PATH.
        }
        throw new StepFailed("The .NET 8 SDK (or newer) is needed to build the launcher: https://dotnet.microsoft.com/download");
    }

    // ==================================================================== plumbing

    private sealed class StepFailed : Exception
    {
        public StepFailed(string message) : base(message) { }
    }

    private static (int Code, string Output) Exec(string file, string workingDirectory, params string[] args)
    {
        var info = new ProcessStartInfo(file)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) info.ArgumentList.Add(a);
        using var process = Process.Start(info) ?? throw new StepFailed($"Could not start {file}.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, stdout.Result + stderr.Result);
    }

    private static string Tail(string text, int lines) =>
        string.Join('\n', text.Replace("\r\n", "\n").Split('\n').Where(l => l.Trim().Length > 0).TakeLast(lines));

    private static void Title(string text) => Write(text, ConsoleColor.White);
    private static void Step(string text) => Write("» " + text, ConsoleColor.Cyan);
    private static void Info(string text) => Write(text, ConsoleColor.Gray);
    private static void Good(string text) => Write(text, ConsoleColor.Green);
    private static void Warn(string text) => Write(text, ConsoleColor.Yellow);
    private static void Bad(string text) => Write(text, ConsoleColor.Red);

    private static void Write(string text, ConsoleColor color)
    {
        var old = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ForegroundColor = old;
    }
}
