using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;

namespace BloodborneLauncher.Views
{
    /// <summary>
    /// Type-to-find for drop-downs. Every list jumps to the entry that starts with what is typed
    /// (Avalonia's own text search). Long lists (more than 8 entries) also get a filter box at the
    /// top of the open list: typing (with the list open or closed) hides the entries that don't
    /// contain the text, Enter picks the first one left, Down goes into the list, Esc clears the
    /// filter. Only what is shown changes: an entry is chosen the same way as before.
    /// </summary>
    public static class ComboSearch
    {
        public const int FilterFrom = 9;

        private sealed class State
        {
            public TextBox? Filter;
            public Popup? Popup;
            public bool Injected;
            public string Pending = "";
        }

        private static readonly ConditionalWeakTable<ComboBox, State> States = new();

        public static void Attach(ComboBox box)
        {
            if (States.TryGetValue(box, out _)) return;
            var state = new State();
            States.Add(box, state);
            box.TemplateApplied += (_, e) => state.Popup = e.NameScope.Find<Popup>("PART_Popup");
            box.DropDownOpened += (_, _) => Opened(box);
            box.DropDownClosed += (_, _) => Closed(box);
            box.AddHandler(InputElement.TextInputEvent, (_, e) => Typed(box, e), RoutingStrategies.Tunnel);
        }

        private static bool Long(ComboBox box) => box.Items.Count >= FilterFrom;

        private static string Text(ComboBoxItem item) => item.Content as string ?? item.Content?.ToString() ?? "";

        private static void Typed(ComboBox box, TextInputEventArgs e)
        {
            if (!Long(box) || string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0])) return;
            var state = States.GetValue(box, _ => new State());
            if (state.Filter != null && (ReferenceEquals(e.Source, state.Filter) || state.Filter.IsKeyboardFocusWithin)) return;
            e.Handled = true;
            if (!box.IsDropDownOpen)
            {
                state.Pending += e.Text;
                box.IsDropDownOpen = true;
                return;
            }
            if (state.Filter == null) return;
            state.Filter.Text += e.Text;
            FocusFilter(state.Filter);
        }

        private static void Opened(ComboBox box)
        {
            var state = States.GetValue(box, _ => new State());
            if (!Long(box))
            {
                state.Pending = "";
                return;
            }
            if (!state.Injected)
            {
                state.Injected = true;
                state.Filter = Inject(box, state.Popup);
            }
            if (state.Filter == null)
            {
                state.Pending = "";
                return;
            }
            var filter = state.Filter;
            filter.Text = state.Pending;
            state.Pending = "";
            Apply(box, filter.Text);
            // After the list has focused its chosen entry.
            Dispatcher.UIThread.Post(() => { if (box.IsDropDownOpen) FocusFilter(filter); }, DispatcherPriority.Background);
        }

        private static void Closed(ComboBox box)
        {
            if (!States.TryGetValue(box, out var state)) return;
            state.Pending = "";
            if (state.Filter != null && (state.Filter.Text ?? "").Length > 0) state.Filter.Text = "";
            Apply(box, "");
        }

        private static void FocusFilter(TextBox filter)
        {
            filter.Focus();
            filter.CaretIndex = (filter.Text ?? "").Length;
        }

        /// <summary>Shows the entries containing every word typed, hides the rest.</summary>
        private static void Apply(ComboBox box, string? text)
        {
            var words = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var item in box.Items.OfType<ComboBoxItem>())
                item.IsVisible = words.All(w => Text(item).Contains(w, StringComparison.OrdinalIgnoreCase));
        }

        private static ComboBoxItem? FirstShown(ComboBox box) =>
            box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.IsVisible && i.IsEnabled);

        /// <summary>Puts a filter box above the list in the drop-down's popup. Null when the
        /// template has no list to put it over (the drop-down then only jumps to entries).</summary>
        private static TextBox? Inject(ComboBox box, Popup? popup)
        {
            if (popup is not { Child: Border { Child: Control list } border })
                return null;

            var filter = new TextBox
            {
                Watermark = L.T("Type to filter"),
                Margin = new Thickness(6, 6, 6, 4),
                MinHeight = 0,
                Padding = new Thickness(8, 5, 8, 5),
                InnerLeftContent = new SymbolIcon(SymbolRegular.Search24) { FontSize = 12, Margin = new Thickness(8, 0, 0, 0), Opacity = 0.7 },
            };
            var host = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(filter, Dock.Top);
            border.Child = null;
            host.Children.Add(filter);
            host.Children.Add(list);
            border.Child = host;

            filter.TextChanged += (_, _) => Apply(box, filter.Text);
            filter.KeyDown += (_, e) =>
            {
                switch (e.Key)
                {
                    case Key.Enter:
                        // Nothing typed: keep the choice as it is.
                        if ((filter.Text ?? "").Trim().Length > 0 && FirstShown(box) is { } first) box.SelectedItem = first;
                        box.IsDropDownOpen = false;
                        e.Handled = true;
                        break;
                    case Key.Down:
                        if (FirstShown(box) is { } item)
                        {
                            item.Focus();
                            item.BringIntoView();
                        }
                        e.Handled = true;
                        break;
                    case Key.Escape when (filter.Text ?? "").Length > 0:
                        filter.Text = "";
                        e.Handled = true;
                        break;
                    case Key.Space:
                        // A space typed in the filter, not the drop-down's open / close key (the
                        // text itself still comes through as text input).
                        e.Handled = true;
                        break;
                }
            };
            return filter;
        }
    }
}
