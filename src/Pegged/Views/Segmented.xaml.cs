using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using WpfRadioButton = System.Windows.Controls.RadioButton;

namespace Pegged.Views
{
    /// <summary>
    /// A Fluent segmented control: one row of mutually exclusive pills where the
    /// selected pill is filled with <c>Brush.Card</c> and gets semi-bold text
    /// (showcase 5 · 设置中心).
    /// <para>
    /// More than <see cref="VisibleCount"/> entries do not squeeze. The first
    /// <c>VisibleCount - 1</c> stay as pills and the rest move under a trailing
    /// "…" pill that opens a menu, the way Windows Settings handles a long
    /// option set. Language has ten entries; keep-on-line has four, so it fits.
    /// </para>
    /// </summary>
    public partial class Segmented : UserControl
    {
        private readonly List<WpfRadioButton> _pills = new();
        private WpfRadioButton? _overflow;
        private bool _syncing;

        /// <summary>Raised after the selection has changed.</summary>
        public event SelectionChangedEventHandler? SelectionChanged;

        public Segmented() => InitializeComponent();

        /// <summary>The entries, in display order.</summary>
        public IEnumerable? Items
        {
            get => (IEnumerable?)GetValue(ItemsProperty);
            set => SetValue(ItemsProperty, value);
        }

        public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
            nameof(Items), typeof(IEnumerable), typeof(Segmented),
            new PropertyMetadata(null, (d, _) => ((Segmented)d).Build()));

        /// <summary>The selected entry, or null when the list is empty.</summary>
        public object? SelectedValue
        {
            get => GetValue(SelectedValueProperty);
            set => SetValue(SelectedValueProperty, value);
        }

        public static readonly DependencyProperty SelectedValueProperty = DependencyProperty.Register(
            nameof(SelectedValue), typeof(object), typeof(Segmented),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnSelectedValueChanged));

        private static void OnSelectedValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((Segmented)d).Sync();
        }

        /// <summary>How many pills are shown before the "…" overflow appears.</summary>
        public int VisibleCount { get; set; } = 4;

        /// <summary>Label of the overflow pill.</summary>
        public string OverflowLabel { get; set; } = "…";

        private List<object> All => Items?.OfType<object>().ToList() ?? new List<object>();

        // ---- building the row ----------------------------------------------

        private void Build()
        {
            var entries = All;

            _pills.Clear();
            ItemsHost.Children.Clear();
            _overflow = null;

            var showAll = entries.Count <= VisibleCount;
            var shown = showAll ? entries : entries.Take(Math.Max(1, VisibleCount - 1)).ToList();

            foreach (var entry in shown)
            {
                var pill = CreatePill(entry);
                ItemsHost.Children.Add(pill);
                _pills.Add(pill);
            }

            if (!showAll)
            {
                var hidden = entries.Skip(shown.Count).ToList();
                // The "…" opens a menu; it is not itself one of the choices, so
                // it carries no entry and no selection handler.
                var overflow = CreatePill(OverflowLabel, choice: false);
                overflow.Tag = hidden;
                overflow.ToolTip = string.Join(" · ", hidden.Select(Text));
                overflow.Checked += (_, _) => OpenOverflow(hidden);
                ItemsHost.Children.Add(overflow);
                _overflow = overflow;
            }

            Sync();
        }

        private WpfRadioButton CreatePill(object entry, bool choice = true)
        {
            var style = TryFindResource("SegItem");
            var pill = new WpfRadioButton
            {
                Content = Text(entry),
                Style = style as Style,
                GroupName = "Seg-" + Guid.NewGuid().ToString("N"),
                FocusVisualStyle = null,
                Tag = choice ? entry : null,
            };
            if (choice) pill.Checked += OnPillChecked;
            return pill;
        }

        private static string Text(object entry) => entry?.ToString() ?? "";

        private void OnPillChecked(object sender, RoutedEventArgs e)
        {
            if (_syncing) return;
            if (sender is not WpfRadioButton pill) return;
            if (pill.Tag is not object entry) return;

            var previous = SelectedValue;
            SetCurrentValue(SelectedValueProperty, entry);
            SelectionChanged?.Invoke(this, new SelectionChangedEventArgs(
                Selector.SelectionChangedEvent,
                previous is null ? Array.Empty<object>() : new[] { previous },
                new[] { entry }));
        }

        /// <summary>The entries that live under the "…" pill.</summary>
        private void OpenOverflow(List<object> hidden)
        {
            var menu = new ContextMenu { PlacementTarget = this, Placement = PlacementMode.Bottom };

            foreach (var entry in hidden)
            {
                var item = new MenuItem
                {
                    Header = Text(entry),
                    IsCheckable = true,
                    IsChecked = Equals(entry, SelectedValue),
                };
                item.Click += (_, _) =>
                {
                    SetCurrentValue(SelectedValueProperty, entry);
                    SelectionChanged?.Invoke(this, new SelectionChangedEventArgs(
                        Selector.SelectionChangedEvent,
                        Array.Empty<object>(), new[] { entry }));
                    Sync();
                };
                menu.Items.Add(item);
            }

            menu.IsOpen = true;

            // The pill was checked to get here; the call above may have changed
            // which entry is selected, so the checked state is redone by Sync.
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input,
                new Action(Sync));
        }

        // ---- keeping the row in step ---------------------------------------

        private void Sync()
        {
            _syncing = true;
            try
            {
                var selected = SelectedValue;
                foreach (var pill in _pills)
                {
                    pill.IsChecked = pill.Tag is object entry && Equals(entry, selected);
                }

                if (_overflow is not null)
                {
                    _overflow.IsChecked = false;
                    var hidden = _overflow.Tag as List<object>;
                    _overflow.ToolTip = selected is not null && hidden is not null &&
                                      !hidden.Contains(selected)
                        ? Text(selected) + " · …"
                        : string.Join(" · ", (hidden ?? new List<object>()).Select(Text));
                }
            }
            finally
            {
                _syncing = false;
            }
        }
    }
}
