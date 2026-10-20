using System;
using System.IO;
using BloodborneLauncher.Core;

var root = Path.Combine(Path.GetTempPath(), "psychov30-paths-" + Guid.NewGuid());
try
{
    foreach (var nested in new[] { false, true })
    {
        var package = Path.Combine(root, nested ? "nested" : "flat");
        var engine = nested ? Path.Combine(package, "engine") : package;
        Directory.CreateDirectory(Path.Combine(engine, "out"));
        Directory.CreateDirectory(Path.Combine(engine, "scripts"));
        File.WriteAllText(Path.Combine(engine, "out", "bb-probe.exe"), "test");
        File.WriteAllText(Path.Combine(engine, "scripts", "run_windows.py"), "test");
        Paths.PortDir = package;
        Check(Paths.IsPortDir(package), "package detection");
        Check(Paths.EngineDir == engine, "engine directory");
        Check(Paths.BinariesDir == Path.Combine(engine, "out"), "binary directory");
        Check(Paths.Python == Path.Combine(engine, "tools", "python", "python.exe"), "Python directory");
        Check(Paths.IniPath == Path.Combine(package, "bbport.ini"), "settings stay with package");
        File.Delete(Path.Combine(engine, "scripts", "run_windows.py"));
        Check(!Paths.IsPortDir(package), "reject missing preparation runtime");
    }
    Console.WriteLine("Launcher paths: flat/nested layouts and missing-runtime checks passed");
}
finally { Directory.Delete(root, recursive: true); }

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
