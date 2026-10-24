using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Threading;

namespace BloodborneLauncher;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnMainWindowClose);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<LanternApp>().UsePlatformDetect();
}

/// <summary>The application: theme, the checks made once at start, and the crash log (App.xaml.cs in WPF).</summary>
internal sealed class LanternApp : Application
{
    private static readonly string CrashLog = Path.Combine(Core.Paths.ConfigDir, "crash.log");

    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://Lantern Engine/"))
        {
            Source = new Uri("avares://Lantern Engine/Themes/Styles.axaml"),
        });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        L.Initialize();
        // A UI-thread fault is recoverable; the other two are only logged, so a hard exit still
        // leaves evidence behind.
        Dispatcher.UIThread.UnhandledException += (_, args) =>
        {
            Record("Dispatcher", args.Exception);
            args.Handled = true;
            try
            {
                Views.Prompt.Tell(L.T("Unexpected error"), L.F($"{args.Exception.Message}\n\nLogged to:\n{CrashLog}"));
            }
            catch (Exception)
            {
                // The log has it.
            }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Record("AppDomain", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Record("UnobservedTask", args.Exception);
            args.SetObserved();
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = desktop.Args ?? Array.Empty<string>();
            // The launcher's new name (0.9.34 beta 8): once, before anything else (Windows packages only).
            if (Core.LanternRename.Run(args))
            {
                desktop.Shutdown();
                return;
            }
            string[] failed = Array.Empty<string>();
            // An update to the engine\ layout: the old top-level engine folders go first.
            if (Core.Paths.DetectPortDir() is { } package)
            {
                FixShippedMouseBinds(package);
                failed = Core.LayoutCleanup.Run(package).ToArray();
            }
            var window = new MainWindow();
            desktop.MainWindow = window;
            if (failed.Length > 0)
                window.Opened += (_, _) => Dispatcher.UIThread.Post(() =>
                    Views.Prompt.Tell("Lantern Engine", "These old files could not be removed (in use?) and can be deleted by hand:\n\n" +
                                                        string.Join("\n", failed)), DispatcherPriority.Background);
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Ends the launcher (an update restarts it, a layout change starts it again).</summary>
    public static void Quit()
    {
        if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
        else
            Environment.Exit(0);
    }

    /// <summary>Packages before 0.9.34 beta 9 shipped lock on as Mouse3 and strong attack as Mouse2,
    /// meant as middle and right click; the engine counts buttons as SDL does (2 middle, 3 right),
    /// so they were swapped. Fixed once, and only while both are still those shipped values.</summary>
    private static void FixShippedMouseBinds(string package)
    {
        try
        {
            var ini = Path.Combine(package, "lantern.ini");
            if (!File.Exists(ini)) return;
            var lines = File.ReadAllLines(ini);
            var lockOn = Array.FindIndex(lines, l => l.Trim() == "bind.lock_on=Mouse3");
            var strong = Array.FindIndex(lines, l => l.Trim() == "bind.strong_attack=Mouse2");
            if (lockOn < 0 || strong < 0) return;
            lines[lockOn] = "bind.lock_on=Mouse2";
            lines[strong] = "bind.strong_attack=Mouse3";
            File.WriteAllLines(ini, lines);
        }
        catch (Exception)
        {
            // The binds stay as they were; the launcher's Controls page can change them.
        }
    }

    private static void Record(string source, Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLog)!);
            var entry = new StringBuilder()
                .AppendLine(new string('=', 70))
                .AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  [{source}]")
                .AppendLine(exception?.ToString() ?? L.T("(no exception object)"))
                .AppendLine();
            File.AppendAllText(CrashLog, entry.ToString());
        }
        catch
        {
            // Logging must never be the thing that takes the app down.
        }
    }
}
