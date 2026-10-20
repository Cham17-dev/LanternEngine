using System.Diagnostics;
using System.IO;
using System.Text;

namespace BloodborneLauncher.Core
{
    /// <summary>
    /// Starts and stops the game and streams its output. The Windows port's run.py does the
    /// preparation (mods, patches, the linked image) and starts bin\bb-probe.exe; packaged builds
    /// run it inside Bloodborne.exe (--run). A custom command can replace it.
    /// </summary>
    public sealed class GameRunner
    {
        private Process? _process;
        private StreamWriter? _log;

        /// <summary>Everything the last session printed: logs\last_session.log in the engine folder.</summary>
        public static string SessionLog => Path.Combine(Paths.IsPortDir(Paths.PortDir) ? Paths.PortDir : Paths.ConfigDir, "logs", "last_session.log");

        public bool Running => _process != null;
        public DateTime StartedAt { get; private set; }

        public event Action<string>? Output;
        public event Action<int>? Exited;

        public static bool Custom(LauncherSettings s) => s.Str("launch_method") == "custom";

        /// <summary>Starts the game; returns null, or why it could not start.</summary>
        public string? Start(LauncherSettings s)
        {
            if (_process != null)
                return "The game is already running.";
            SdlPad.Stop(); // the game takes the controllers

            ProcessStartInfo info;
            if (Custom(s))
            {
                var command = Paths.Expand(s.Str("custom_command").Trim());
                if (command.Length == 0)
                    return "No custom command is set. Choose one in Settings › Launch.";
                info = new ProcessStartInfo(command) { Arguments = s.Str("custom_args") };
            }
            else
            {
                if (!Paths.IsPortDir(Paths.PortDir))
                    return "The engine folder is not set. Choose it in Settings › Bloodborne PC.";
                // The package's own Python runs the engine's run_windows.py (prepare, patch, start).
                var python = File.Exists(Paths.Python) ? Paths.Python : "py";
                info = new ProcessStartInfo(python);
                if (python == "py") info.ArgumentList.Add("-3");
                info.ArgumentList.Add(Path.Combine(Paths.EngineDir, "scripts", "run_windows.py"));
                info.ArgumentList.Add("--game-dir");
                info.ArgumentList.Add(GameEnvironment.GameDir(s));
            }

            info.WorkingDirectory = Paths.IsPortDir(Paths.PortDir) ? Paths.EngineDir : Environment.CurrentDirectory;
            foreach (var (key, value) in GameEnvironment.Build(s))
                info.Environment[key] = value;
            info.Environment["PYTHONUNBUFFERED"] = "1";
            info.Environment["PYTHONUTF8"] = "1";
            info.Environment["PYTHONIOENCODING"] = "utf-8";
            // Prepared game files live with user data, whereas the custom engine is in engine/out.
            info.Environment["BB_PROBE"] = Path.Combine(Paths.BinariesDir, "bb-probe.exe");
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.StandardOutputEncoding = Encoding.UTF8;
            info.StandardErrorEncoding = Encoding.UTF8;

            try
            {
                var process = new Process { StartInfo = info, EnableRaisingEvents = true };
                OpenLog();
                // The first line of every log, the Output page's export included: which build ran.
                Emit($"Lantern Engine + PsychoV30 {Updater.CurrentLabel}, engine {Updater.LocalEngine() ?? "unknown"}, {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                Emit($"HDR engine: {Path.Combine(Paths.BinariesDir, "bb-probe.exe")}");
                process.OutputDataReceived += (_, e) => { if (e.Data != null) Emit(e.Data); };
                process.ErrorDataReceived += (_, e) => { if (e.Data != null) Emit(e.Data); };
                process.Exited += (_, _) =>
                {
                    var code = -1;
                    try { process.WaitForExit(); code = process.ExitCode; } catch { /* gone */ }
                    _process = null;
                    CloseLog();
                    Exited?.Invoke(code);
                };
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                _process = process;
                StartedAt = DateTime.Now;
                return null;
            }
            catch (Exception e)
            {
                _process = null;
                CloseLog();
                return "Could not start: " + e.Message;
            }
        }

        private void OpenLog()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SessionLog)!);
                // The one before is kept: a crash's log survives starting the game again.
                try
                {
                    if (File.Exists(SessionLog))
                        File.Copy(SessionLog, Path.Combine(Path.GetDirectoryName(SessionLog)!, "previous_session.log"), overwrite: true);
                }
                catch (IOException) { /* still open elsewhere: only this session's is kept */ }
                var stream = new FileStream(SessionLog, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                _log = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
            }
            catch { _log = null; /* read-only folder: the Log tab still has it */ }
        }

        private void Emit(string line)
        {
            lock (this)
            {
                try { _log?.WriteLine(line); } catch { /* disk full */ }
            }
            Output?.Invoke(line);
        }

        private void CloseLog()
        {
            lock (this)
            {
                _log?.Dispose();
                _log = null;
            }
        }

        /// <summary>Asks the game window to close, and kills the process tree after three seconds.</summary>
        public async Task StopAsync()
        {
            var process = _process;
            if (process == null)
                return;

            foreach (var probe in Process.GetProcessesByName("bb-probe"))
            {
                try { probe.CloseMainWindow(); } catch { /* no window */ }
            }
            try { process.CloseMainWindow(); } catch { /* no window */ }

            await Task.WhenAny(process.WaitForExitAsync(), Task.Delay(3000));
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            }
        }
    }
}
