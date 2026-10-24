// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Desktop Dispatch Console
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Desktop Dispatch Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2026 C. Lovell, Dev_Ranger
*
*/

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;
using Keys = System.Windows.Forms.Keys;

namespace dvmconsole
{
    public partial class PttKeybindWindow : Window
    {
        private sealed class BindingRow
        {
            public int Slot;
            public Keys Key;
            public Button KeyButton;
            public Button ResourcePicker;
            public PttKeyBinding Target;
        }

        private readonly List<BindingRow> rows = new();
        private BindingRow capturing;
        private BindingRow pickingResource;
        internal int? RequestedResourceSlot => pickingResource?.Slot;
        public Keys PrimaryKey { get; private set; }
        public List<PttKeyBinding> FixedBindings { get; private set; } = new();

        public PttKeybindWindow(Keys primaryKey, IEnumerable<PttKeyBinding> bindings, IEnumerable<Codeplug.Channel> channels)
        {
            InitializeComponent();
            var resources = channels.ToList();

            for (int slot = 1; slot <= PttBindingPalette.SlotCount; slot++)
            {
                PttKeyBinding saved = bindings.FirstOrDefault(binding => binding.Slot == slot);
                var row = new BindingRow { Slot = slot, Key = slot == 1 ? primaryKey : saved?.Key ?? Keys.None };
                var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                foreach (GridLength width in new[] { new GridLength(112), new GridLength(125), new GridLength(74), new GridLength(1, GridUnitType.Star) })
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
                var label = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                label.Children.Add(new Rectangle { Width = 14, Height = 14, Fill = PttBindingPalette.BrushFor(slot), Margin = new Thickness(0, 0, 8, 0) });
                label.Children.Add(new TextBlock { Text = $"{slot}. {PttBindingPalette.Names[slot - 1]}" });
                grid.Children.Add(label);
                row.KeyButton = new Button { Content = KeyText(row.Key), Margin = new Thickness(0, 0, 8, 0), ToolTip = "Set Key" };
                row.KeyButton.Click += (_, _) => BeginCapture(row);
                Grid.SetColumn(row.KeyButton, 1); grid.Children.Add(row.KeyButton);
                var clear = new Button { Content = "Clear", Margin = new Thickness(0, 0, 8, 0) };
                clear.Click += (_, _) => { CancelCapture(); row.Key = Keys.None; row.KeyButton.Content = KeyText(row.Key); };
                Grid.SetColumn(clear, 2); grid.Children.Add(clear);
                if (slot == 1)
                {
                    var target = new TextBlock { Text = "Ctrl + click selection", VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(target, 3); grid.Children.Add(target);
                }
                else
                {
                    row.Target = saved?.Copy();
                    row.ResourcePicker = new Button { HorizontalContentAlignment = HorizontalAlignment.Left,
                        ToolTip = "Click to edit, then click a resource on the console" };
                    UpdateResourceButton(row, saved == null ? null : resources.FirstOrDefault(saved.Matches));
                    row.ResourcePicker.Click += (_, _) =>
                    {
                        CancelCapture();
                        pickingResource = row;
                        // Hiding ends the modal wait without discarding the unsaved editor rows.
                        Hide();
                    };
                    Grid.SetColumn(row.ResourcePicker, 3); grid.Children.Add(row.ResourcePicker);
                }
                rows.Add(row); BindingRows.Children.Add(grid);
            }
        }

        private static void UpdateResourceButton(BindingRow row, Codeplug.Channel channel)
        {
            string label = channel != null
                ? $"{channel.Name} ({channel.System} / TG {channel.Tgid} / {channel.GetChannelMode()}{(channel.GetChannelMode() == Codeplug.ChannelMode.DMR ? $" TS{channel.Slot}" : "")})"
                : row.Target == null ? "Not assigned" : $"Unavailable: {row.Target.SystemName} / TG {row.Target.Tgid} / {row.Target.Mode}";
            row.ResourcePicker.Content = new TextBlock { Text = $"Click to edit: {label}", TextTrimming = TextTrimming.CharacterEllipsis };
            row.ResourcePicker.ToolTip = $"{label}\nClick to edit, then click a resource on the console";
        }

        internal void CompleteResourcePick(Codeplug.Channel channel)
        {
            if (pickingResource == null)
                return;
            if (channel != null && !channel.RxOnly)
            {
                pickingResource.Target = new PttKeyBinding { SystemName = channel.System, Tgid = channel.Tgid,
                    Mode = channel.GetChannelMode(), Timeslot = Math.Max(1, channel.Slot) };
                UpdateResourceButton(pickingResource, channel);
                ValidationMessage.Text = string.Empty;
                CaptureStatus.Text = "Resource selected. Save to apply changes.";
            }
            else
                CaptureStatus.Text = "Resource selection cancelled. Previous assignment kept.";
            pickingResource = null;
        }

        private static string KeyText(Keys key) => key == Keys.None ? "Set Key" : key.ToString();

        private void BeginCapture(BindingRow row)
        {
            CancelCapture();
            capturing = row;
            row.KeyButton.Content = "Press key...";
            CaptureStatus.Text = $"Choose a key for slot {row.Slot}. Esc cancels. Shortcuts use a single key, not a key combination.";
        }

        private void CancelCapture()
        {
            if (capturing != null)
                capturing.KeyButton.Content = KeyText(capturing.Key);
            capturing = null;
        }

        private void CaptureKey_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (capturing == null)
                return;
            e.Handled = true;
            Key key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
            if (key == System.Windows.Input.Key.Escape)
            {
                CancelCapture();
                CaptureStatus.Text = "Key capture cancelled.";
                return;
            }
            Keys code = (Keys)KeyInterop.VirtualKeyFromKey(key);
            if (!PttBindingPalette.IsValidKey(code))
                return;
            capturing.Key = code;
            CancelCapture();
            CaptureStatus.Text = "Key captured. Save to apply changes.";
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            CancelCapture();
            var usedKeys = new HashSet<Keys>();
            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<PttKeyBinding>();
            foreach (BindingRow row in rows)
            {
                if (row.Key == Keys.None)
                    continue;
                if (!PttBindingPalette.IsValidKey(row.Key) || !usedKeys.Add(row.Key))
                {
                    ValidationMessage.Text = "Each slot needs a different, valid single key.";
                    return;
                }
                if (row.Slot == 1)
                    continue;
                if (row.Target == null)
                {
                    ValidationMessage.Text = $"Choose a resource for slot {row.Slot}, or clear its key.";
                    return;
                }
                if (!targets.Add(row.Target.TargetKey))
                {
                    ValidationMessage.Text = "A resource can only have one fixed binding. Green selection may still overlap it.";
                    return;
                }
                PttKeyBinding binding = row.Target.Copy();
                binding.Slot = row.Slot; binding.Key = row.Key;
                result.Add(binding);
            }
            PrimaryKey = rows[0].Key;
            FixedBindings = result;
            DialogResult = true;
        }
    }
}
