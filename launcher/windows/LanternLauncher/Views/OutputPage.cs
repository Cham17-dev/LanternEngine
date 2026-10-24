using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using BloodborneLauncher.Core;

namespace BloodborneLauncher.Views
{
    /// <summary>The game's own output (run.py and bb-probe), live while it runs.</summary>
    public sealed class OutputPage : LauncherPage
    {
        private readonly TextBox _output;
        private readonly ToggleSwitch _follow;

        public OutputPage() : base(L.T("Game output"), L.T("What run.py and the game print. Copy or export it when reporting a problem."))
        {
            var bar = new Grid { Margin = new Thickness(0, 18, 0, 8), ColumnDefinitions = new ColumnDefinitions("*,Auto") };

            _follow = new ToggleSwitch { IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
            bar.Children.Add(Row(_follow, new TextBlock { Text = L.T("Follow new output"), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }));

            var buttons = Row(
                SmallButton(L.T("Copy"), SymbolRegular.Copy24, (_, _) => Copy()),
                SmallButton(L.T("Export…"), SymbolRegular.Save24, (_, _) => Export()),
                SmallButton(L.T("Clear"), SymbolRegular.Broom24, (_, _) => Ws.ClearOutput()));
            Grid.SetColumn(buttons, 1);
            bar.Children.Add(buttons);
            Body.Children.Add(bar);

            SettingToggle(SymbolRegular.WindowConsole20, L.T("Show this page when the game starts"), null, "switch_to_output");

            _output = new TextBox { Height = 520, Margin = new Thickness(0, 4, 0, 0) };
            _output.Classes.Add("CodeBox");
            Body.Children.Add(_output);

            // One append per batch; while the page is not on screen nothing is laid out, and it
            // catches up in one go when shown.
            Ws.GameOutput += batch =>
            {
                if (!IsShown)
                {
                    _stale = true;
                    return;
                }
                _lines += batch.Count;
                if (_lines > Workspace.MaxOutputLines + 500)
                {
                    Rebuild();
                    return;
                }
                _output.Text += string.Join("\n", batch) + "\n";
                if (_follow.IsChecked == true)
                    ScrollToEnd();
            };
            Ws.OutputCleared += () =>
            {
                _output.Text = "";
                _lines = 0;
            };
            AttachedToVisualTree += (_, _) => { if (_stale) Rebuild(); };
            Rebuild();
        }

        private int _lines;
        private bool _stale;

        private void ScrollToEnd() =>
            Dispatcher.UIThread.Post(() => _output.CaretIndex = (_output.Text ?? "").Length, DispatcherPriority.Background);

        private void Rebuild()
        {
            _output.Text = Ws.OutputLines.Count > 0 ? string.Join("\n", Ws.OutputLines) + "\n" : "";
            _lines = Ws.OutputLines.Count;
            _stale = false;
            if (_follow.IsChecked == true)
                ScrollToEnd();
        }

        public override void OnShown()
        {
            if (_stale) Rebuild();
            else if (_follow.IsChecked == true)
                ScrollToEnd();
        }

        private void Copy()
        {
            if ((_output.Text ?? "").Length == 0) return;
            if (ClipboardText.Set(_output.Text!))
                Ws.Log(L.T("Game output copied."), LogLevel.Good);
            else
                Ws.Log(L.T("Could not copy: another program is using the clipboard. Try again, or use Export."), LogLevel.Warn);
        }

        private void Export()
        {
            var file = Ui.SaveFile(L.T("Export the game output"), $"lantern-{DateTime.Now:yyyyMMdd_HHmmss}.log",
                L.T("Log files|*.log|Text files|*.txt|All files|*.*"));
            if (file == null) return;
            try
            {
                // The whole session (logs\last_session.log), not the last lines this page keeps: a
                // report trimmed to its end lost the start and what came before a freeze.
                if (File.Exists(GameRunner.SessionLog))
                {
                    using var from = new FileStream(GameRunner.SessionLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var to = File.Create(file);
                    from.CopyTo(to);
                }
                else
                    File.WriteAllText(file, _output.Text);
                Ws.Log(L.T("Game output saved: ") + file, LogLevel.Good);
            }
            catch (Exception e)
            {
                Ws.Log(L.T("Could not save the output: ") + e.Message, LogLevel.Bad);
            }
        }
    }
}
