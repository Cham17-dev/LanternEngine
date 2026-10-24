using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using BloodborneLauncher.Core;

namespace BloodborneLauncher.Views
{
    /// <summary>
    /// The trophy notification shown over the game: a small borderless window that never takes
    /// focus and lets every click through, placed in a corner of the monitor the game is on. It
    /// shows over windowed and borderless fullscreen games (SDL's fullscreen is borderless);
    /// unlocks arriving together are shown one after another.
    /// </summary>
    public sealed class TrophyToast : Window
    {
        private static readonly Queue<Trophy> Pending = new();
        private static bool _showing;

        public static Color GradeColor(Grade grade) => grade switch
        {
            Grade.Platinum => Color.FromRgb(0xB9, 0xD3, 0xEE),
            Grade.Gold => Color.FromRgb(0xE3, 0xB3, 0x41),
            Grade.Silver => Color.FromRgb(0xC4, 0xC8, 0xD0),
            _ => Color.FromRgb(0xC8, 0x84, 0x4B),
        };

        /// <summary>Queues a notification (and its sound) for an unlocked trophy.</summary>
        public static void Announce(Trophy trophy)
        {
            Pending.Enqueue(trophy);
            if (!_showing) Next();
        }

        private static void Next()
        {
            if (Pending.Count == 0)
            {
                _showing = false;
                return;
            }
            _showing = true;
            var trophy = Pending.Dequeue();
            var s = Workspace.Current.Settings;
            if (s.Bool("trophy_sound"))
                Chime.Play(s.Int("trophy_volume"), s.Str("trophy_sound_file"));
            new TrophyToast(trophy, s.Str("trophy_position")).Show();
        }

        private readonly string _corner;
        private readonly Border _bar;

        private TrophyToast(Trophy trophy, string corner)
        {
            _corner = corner;
            SystemDecorations = SystemDecorations.None;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Background = Brushes.Transparent;
            Topmost = true;
            ShowActivated = false;
            ShowInTaskbar = false;
            CanResize = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            Focusable = false;
            IsHitTestVisible = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint(-32000, -32000); // placed once its size is known
            this.SetResourceReference(FontFamilyProperty, "UiFont");

            // As the PS4 shows it: a slim dark bar with a soft sheen and a hairline border, a grey
            // tile with a white trophy, the trophy's name after a small cup in its grade's colour,
            // and "You have earned a trophy!" under it.
            var color = GradeColor(trophy.Grade);

            var tile = new Border
            {
                Width = 46,
                Height = 46,
                CornerRadius = new CornerRadius(2),
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.FromRgb(0x5A, 0x5D, 0x62), 0), new GradientStop(Color.FromRgb(0x33, 0x35, 0x39), 1) },
                },
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                Child = new SymbolIcon(SymbolRegular.Trophy24)
                {
                    Filled = true, FontSize = 24, Foreground = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
                VerticalAlignment = VerticalAlignment.Center,
            };
            // The trophy's own picture from the game dump, as the PS4 shows it.
            if (TrophyIcons.For(Workspace.Current.Settings, trophy.Id) is { } file &&
                SettingRow.Picture(file, 92) is { } picture)
            {
                tile.Background = Brushes.Black;
                tile.Child = new Image { Source = picture, Stretch = Stretch.UniformToFill };
            }

            var cup = new SymbolIcon(SymbolRegular.Trophy24)
            {
                Filled = true,
                FontSize = 15,
                Foreground = new SolidColorBrush(color),
                Margin = new Thickness(0, 1, 7, 0),
            };
            var name = new TextBlock
            {
                Text = trophy.Name,
                FontSize = 17,
                Foreground = new SolidColorBrush(Color.FromRgb(0xF4, 0xF4, 0xF4)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 330,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var title = new StackPanel { Orientation = Orientation.Horizontal };
            title.Children.Add(cup);
            title.Children.Add(name);

            var earned = new TextBlock
            {
                Text = L.T("You have earned a trophy!"),
                FontSize = 13.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xCB, 0xCF)),
                Margin = new Thickness(0, 2, 0, 0),
            };
            var text = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(title);
            text.Children.Add(earned);

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(tile);
            row.Children.Add(text);

            // The sheen: lighter at the top, darker at the bottom, a little see-through.
            var background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0xEE, 0x4A, 0x4D, 0x52), 0),
                    new GradientStop(Color.FromArgb(0xEE, 0x2C, 0x2E, 0x32), 0.45),
                    new GradientStop(Color.FromArgb(0xF2, 0x1C, 0x1D, 0x20), 1),
                },
            };

            var from = corner.EndsWith("right") ? 60 : -60;
            _bar = new Border
            {
                Margin = new Thickness(18),
                Padding = new Thickness(9, 9, 26, 9),
                CornerRadius = new CornerRadius(3),
                Background = background,
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xC8, 0xCC, 0xD2)),
                BorderThickness = new Thickness(1),
                BoxShadow = new BoxShadows(new BoxShadow { Blur = 14, OffsetY = 2, Color = Color.FromArgb(0x99, 0, 0, 0) }),
                Child = row,
                MinWidth = 380,
                RenderTransform = TransformOperations.Parse($"translateX({from}px)"),
                Transitions = new Transitions
                {
                    new TransformOperationsTransition
                    {
                        Property = Visual.RenderTransformProperty,
                        Duration = TimeSpan.FromMilliseconds(320),
                        Easing = new CubicEaseOut(),
                    },
                },
            };
            Content = _bar;
            Opacity = 0;
            Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(300) } };

            Opened += (_, _) =>
            {
                if (OperatingSystem.IsWindows()) ClickThrough();
                // Placed once laid out, at its real size.
                Dispatcher.UIThread.Post(() =>
                {
                    Place();
                    Animate();
                }, DispatcherPriority.Loaded);
            };
        }

        /// <summary>Click-through, never activated, no Alt+Tab entry; kept above a fullscreen game.</summary>
        private void ClickThrough()
        {
            if (TryGetPlatformHandle()?.Handle is not { } handle || handle == IntPtr.Zero) return;
            SetWindowLong(handle, GWL_EXSTYLE, GetWindowLong(handle, GWL_EXSTYLE) |
                WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_LAYERED);
            // SDL makes a focused fullscreen game window topmost too, and raises it again on its
            // own: the toast went under it (its sound played, nothing showed). Back on top while
            // it shows, without taking focus.
            var raise = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            raise.Tick += (_, _) => SetWindowPos(handle, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            raise.Start();
            Closed += (_, _) => raise.Stop();
        }

        /// <summary>The monitor the game is on (the primary one without a game), in device pixels.</summary>
        private PixelRect Area()
        {
            if (OperatingSystem.IsWindows())
            {
                var window = IntPtr.Zero;
                foreach (var probe in Process.GetProcessesByName("bb-probe"))
                {
                    if (probe.MainWindowHandle != IntPtr.Zero) window = probe.MainWindowHandle;
                    probe.Dispose();
                }
                var monitor = window != IntPtr.Zero
                    ? MonitorFromWindow(window, MONITOR_DEFAULTTONEAREST)
                    : MonitorFromPoint(new Point32(), MONITOR_DEFAULTTOPRIMARY);
                var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
                // The whole monitor: a fullscreen game covers the taskbar too.
                if (GetMonitorInfo(monitor, ref info))
                    return new PixelRect(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top);
            }
            return (Screens.Primary ?? Screens.All.FirstOrDefault())?.Bounds ?? new PixelRect(0, 0, 1920, 1080);
        }

        private void Place()
        {
            var area = Area();
            var scale = DesktopScaling;
            var width = (int)Math.Ceiling(Bounds.Width * scale);
            var height = (int)Math.Ceiling(Bounds.Height * scale);
            var gap = (int)(12 * scale);
            var right = _corner.EndsWith("right");
            var bottom = _corner.StartsWith("bottom");
            Position = new PixelPoint(right ? area.Right - width - gap : area.X + gap, bottom ? area.Bottom - height - gap : area.Y + gap);
        }

        private void Animate()
        {
            Opacity = 1;
            _bar.RenderTransform = TransformOperations.Parse("translateX(0px)");
            DispatcherTimer.RunOnce(() =>
            {
                Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(380) } };
                Opacity = 0;
                DispatcherTimer.RunOnce(() =>
                {
                    Close();
                    Next();
                }, TimeSpan.FromMilliseconds(400));
            }, TimeSpan.FromSeconds(5.5));
        }

        // ---------------------------------------------------------------- Win32

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x8000000;
        private const uint MONITOR_DEFAULTTOPRIMARY = 1, MONITOR_DEFAULTTONEAREST = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct Point32 { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect32 { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int Size;
            public Rect32 Monitor, Work;
            public uint Flags;
        }

        private static readonly IntPtr HWND_TOPMOST = new(-1);
        private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040;

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr window, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr window, int index, int value);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(Point32 point, uint flags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    }
}
