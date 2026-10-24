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

using System.IO;
using System.Windows;
using Keys = System.Windows.Forms.Keys;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using dvmconsole.Controls;

namespace dvmconsole
{
    public partial class MainWindow
    {
        private List<PttKeyBinding> fixedPttBindings = new();
        private readonly Dictionary<int, List<ChannelBox>> keyboardPttTargets = new();
        private readonly Dictionary<Keys, (int Slot, bool Toggle)> keyboardPttDown = new();
        private readonly HashSet<Keys> ignoredHeldPttKeys = new();
        private readonly object[] keyboardPttOwners = Enumerable.Range(0, PttBindingPalette.SlotCount).Select(_ => new object()).ToArray();
        private readonly object globalMousePttOwner = new();
        private readonly List<ChannelBox> globalMousePttTargets = new();
        private DispatcherTimer keyboardPttWatchdog;
        private bool editingPttKeybinds;

        private string PttKeybindContext => string.IsNullOrWhiteSpace(Codeplug?.SourcePath)
            ? string.Empty : Path.GetFullPath(Codeplug.SourcePath);

        private List<ChannelBox> GetPttResourceCards() => GetAllCanvases()
            .SelectMany(canvas => canvas.Children.OfType<ChannelBox>()).Distinct().ToList();

        private List<ChannelBox> ResolvePrimaryPttTargets()
        {
            ChannelBox primary = selectedChannelsManager.PrimaryChannel;
            return primary != null ? new List<ChannelBox> { primary } :
                settingsManager.GlobalPTTKeysAllChannels ? selectedChannelsManager.GetSelectedChannels().ToList() : new();
        }

        private void InitializeKeyboardShortcuts()
        {
            ReleaseKeyboardPttSlots();
            fixedPttBindings = new();
            if (settingsManager.FixedPttBindingsByCodeplug != null &&
                settingsManager.FixedPttBindingsByCodeplug.TryGetValue(PttKeybindContext, out List<PttKeyBinding> saved))
                fixedPttBindings = PttBindingPalette.Normalize(saved, settingsManager.GlobalPTTShortcut);
            var keys = fixedPttBindings.Select(binding => binding.Key).ToList();
            if (PttBindingPalette.IsValidKey(settingsManager.GlobalPTTShortcut))
                keys.Add(settingsManager.GlobalPTTShortcut);
            // Reconfiguring while a key is held must not create a fresh transmission.
            ignoredHeldPttKeys.Clear();
            ignoredHeldPttKeys.UnionWith(keys.Where(IsShortcutKeyPhysicallyDown));
            keyboardManager.SetListenKeys(keys.Distinct().ToList());
            keyboardManager.OnKeyEvent -= KeyboardManagerOnKeyEvent;
            keyboardManager.OnKeyEvent += KeyboardManagerOnKeyEvent;
            if (ignoredHeldPttKeys.Count > 0)
                StartKeyboardPttWatchdog();
            RefreshPttBindingBorders();
        }

        private void RefreshPttBindingBorders()
        {
            foreach (ChannelBox card in GetPttResourceCards())
            {
                PttKeyBinding binding = fixedPttBindings.FirstOrDefault(item => item.Matches(card.ConfiguredChannel));
                card.FixedPttBorder = binding == null ? null : PttBindingPalette.BrushFor(binding.Slot);
            }
        }

        private void KeyboardManagerOnKeyEvent(Keys key, GlobalKeyboardHook.KeyboardState state)
        {
            if (editingPttKeybinds || isShuttingDown)
                return;
            bool down = state is GlobalKeyboardHook.KeyboardState.KeyDown or GlobalKeyboardHook.KeyboardState.SysKeyDown;
            bool up = state is GlobalKeyboardHook.KeyboardState.KeyUp or GlobalKeyboardHook.KeyboardState.SysKeyUp;
            if (ignoredHeldPttKeys.Contains(key))
            {
                if (up)
                    ignoredHeldPttKeys.Remove(key);
                return;
            }
            if (up)
            {
                ReleasePttShortcutKey(key);
                return;
            }
            if (!down || keyboardPttDown.ContainsKey(key))
                return;
            int slot = key == settingsManager.GlobalPTTShortcut && key != Keys.None ? 1 :
                fixedPttBindings.FirstOrDefault(binding => binding.Key == key)?.Slot ?? 0;
            if (slot == 0)
                return;

            keyboardPttDown[key] = (slot, settingsManager.TogglePTTMode);
            StartKeyboardPttWatchdog();
            if (settingsManager.TogglePTTMode && keyboardPttTargets.ContainsKey(slot))
            {
                ReleaseKeyboardPttSlot(slot);
                return;
            }

            List<ChannelBox> targets;
            if (slot == 1)
                targets = ResolvePrimaryPttTargets();
            else
            {
                PttKeyBinding binding = fixedPttBindings.Single(item => item.Slot == slot);
                ChannelBox target = GetPttResourceCards().FirstOrDefault(card => card.IsSelected && binding.Matches(card.ConfiguredChannel));
                targets = target == null ? new() : new() { target };
            }
            var started = new List<ChannelBox>();
            foreach (ChannelBox target in targets)
                if (target.SetPttInput(keyboardPttOwners[slot - 1], true))
                    started.Add(target);
            if (started.Count > 0)
                keyboardPttTargets[slot] = started;
            RefreshGlobalPttIndicator();
        }

        private void ReleasePttShortcutKey(Keys key)
        {
            ignoredHeldPttKeys.Remove(key);
            if (keyboardPttDown.Remove(key, out var pressed) && !pressed.Toggle)
                ReleaseKeyboardPttSlot(pressed.Slot);
        }

        private void ReleaseKeyboardPttSlot(int slot)
        {
            if (keyboardPttTargets.Remove(slot, out List<ChannelBox> targets))
                foreach (ChannelBox target in targets)
                    target.SetPttInput(keyboardPttOwners[slot - 1], false);
            RefreshGlobalPttIndicator();
        }

        private void StartKeyboardPttWatchdog()
        {
            keyboardPttWatchdog ??= new DispatcherTimer(KeyboardPttWatchdogInterval,
                DispatcherPriority.Normal, CheckKeyboardPttReleases, Dispatcher);
            keyboardPttWatchdog.Start();
        }

        private void CheckKeyboardPttReleases(object sender, EventArgs e)
        {
            foreach (Keys key in keyboardPttDown.Keys.Concat(ignoredHeldPttKeys).Distinct().ToArray())
                if (!IsShortcutKeyPhysicallyDown(key))
                    ReleasePttShortcutKey(key);
            if (keyboardPttDown.Count == 0 && ignoredHeldPttKeys.Count == 0)
                keyboardPttWatchdog?.Stop();
        }

        private void CancelKeyboardPttWatchdog()
        {
            keyboardPttWatchdog?.Stop();
            keyboardPttDown.Clear();
            ignoredHeldPttKeys.Clear();
        }

        private void ReleaseKeyboardPttSlots()
        {
            CancelKeyboardPttWatchdog();
            foreach (int slot in keyboardPttTargets.Keys.ToArray())
                ReleaseKeyboardPttSlot(slot);
        }

        private void ReleaseAllOperatorPttInputs()
        {
            ReleaseKeyboardPttSlots();
            globalMousePttTargets.Clear();
            globalPttState = false;
            foreach (ChannelBox card in GetPttResourceCards())
                card.ReleaseAllPttInputs();
            RefreshGlobalPttIndicator();
        }

        private void RefreshGlobalPttIndicator()
        {
            bool active = globalMousePttTargets.Any(card => card.IsOperatorTransmitting) ||
                (keyboardPttTargets.TryGetValue(1, out List<ChannelBox> targets) && targets.Any(card => card.IsOperatorTransmitting));
            SetGlobalPttVisualState(active);
        }

        private static bool IsShortcutKeyPhysicallyDown(Keys key) => (GetAsyncKeyState((int)key) & 0x8000) != 0;

        private async void SetGlobalPTTKeybind(object sender, RoutedEventArgs e)
        {
            if (editingPttKeybinds)
                return;
            ReleaseAllOperatorPttInputs();
            editingPttKeybinds = true;
            PttKeybindWindow dialog = null;
            try
            {
                dialog = new PttKeybindWindow(settingsManager.GlobalPTTShortcut, fixedPttBindings,
                    Codeplug?.Zones?.SelectMany(zone => zone.Channels ?? new()).Where(channel => !channel.RxOnly) ?? Enumerable.Empty<Codeplug.Channel>())
                { Owner = this };
                while (!isShuttingDown)
                {
                    bool save = dialog.ShowDialog() == true;
                    if (dialog.RequestedResourceSlot is int slot)
                    {
                        Codeplug.Channel channel = await PickPttBindingResourceAsync(this, slot);
                        dialog.CompleteResourcePick(channel);
                        continue;
                    }
                    if (!save)
                        break;
                    settingsManager.GlobalPTTShortcut = dialog.PrimaryKey;
                    if (!string.IsNullOrEmpty(PttKeybindContext))
                    {
                        settingsManager.FixedPttBindingsByCodeplug ??= new(StringComparer.OrdinalIgnoreCase);
                        settingsManager.FixedPttBindingsByCodeplug[PttKeybindContext] = dialog.FixedBindings;
                    }
                    settingsManager.SaveSettings();
                    break;
                }
            }
            finally
            {
                dialog?.Close();
                editingPttKeybinds = false;
                if (!isShuttingDown)
                    InitializeKeyboardShortcuts();
            }
        }

        /// <summary>
        /// Intercepts the pick before card buttons, selection, or drag handlers can act on it.
        /// </summary>
        private static async Task<Codeplug.Channel> PickPttBindingResourceAsync(Window owner, int slot)
        {
            var picked = new TaskCompletionSource<Codeplug.Channel>(TaskCreationOptions.RunContinuationsAsynchronously);
            string previousTitle = owner.Title;
            var previousCursor = owner.Cursor;
            void MousePick(object sender, MouseButtonEventArgs args)
            {
                ChannelBox card = FindPttPickAncestor<ChannelBox>(args.OriginalSource as DependencyObject);
                if (card == null && args.ChangedButton == MouseButton.Left &&
                    FindPttPickAncestor<TabItem>(args.OriginalSource as DependencyObject) != null)
                    return; // Allow navigation between resource tabs while picking.
                args.Handled = true;
                if (args.RoutedEvent == Mouse.PreviewMouseDownEvent && args.ChangedButton == MouseButton.Left &&
                    card != null && !card.IsLocalPlayback && !card.IsRxOnly)
                    picked.TrySetResult(card.ConfiguredChannel);
            }
            void KeyPick(object sender, System.Windows.Input.KeyEventArgs args)
            {
                args.Handled = true;
                if (args.Key == Key.Escape)
                    picked.TrySetResult(null);
            }
            void WheelPick(object sender, MouseWheelEventArgs args) => args.Handled = true;
            void WindowClosed(object sender, EventArgs args) => picked.TrySetResult(null);
            owner.PreviewMouseDown += MousePick;
            owner.PreviewMouseUp += MousePick;
            owner.PreviewMouseWheel += WheelPick;
            owner.PreviewKeyDown += KeyPick;
            owner.PreviewKeyUp += KeyPick;
            owner.Closed += WindowClosed;
            try
            {
                owner.Title = $"Choose {PttBindingPalette.Names[slot - 1]} PTT resource: click a card, Esc to cancel";
                owner.Cursor = Cursors.Cross;
                owner.Activate();
                return await picked.Task;
            }
            finally
            {
                owner.PreviewMouseDown -= MousePick;
                owner.PreviewMouseUp -= MousePick;
                owner.PreviewMouseWheel -= WheelPick;
                owner.PreviewKeyDown -= KeyPick;
                owner.PreviewKeyUp -= KeyPick;
                owner.Closed -= WindowClosed;
                owner.Title = previousTitle;
                owner.Cursor = previousCursor;
            }
        }

        private static T FindPttPickAncestor<T>(DependencyObject source) where T : DependencyObject
        {
            while (source != null)
            {
                if (source is T match)
                    return match;
                source = source is FrameworkContentElement content ? content.Parent :
                    VisualTreeHelper.GetParent(source) ?? LogicalTreeHelper.GetParent(source);
            }
            return null;
        }
    }
}
