// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Desktop Dispatch Console
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Desktop Dispatch Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2025 Caleb, K4PHP
*   Copyright (C) 2025 Bryan Biedenkapp, N2PLL
*   Copyright (C) 2026 Lorenzo L. Romero, K2LLR
*   Copyright (C) 2026 C. Lovell, K7CBL
*
*/

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MaterialDesignThemes.Wpf;

namespace dvmconsole
{
    /// <summary>
    /// Interaction logic for PatchGroupsWindow.xaml.
    /// </summary>
    public partial class PatchGroupsWindow : Window
    {
        public sealed class PatchGroupPttEventArgs : EventArgs
        {
            public string GroupName { get; set; } = string.Empty;
            public bool IsActive { get; set; }
            public List<SettingsManager.PatchTalkgroupMember> Members { get; set; } = new List<SettingsManager.PatchTalkgroupMember>();
        }

        public event Action<Dictionary<string, List<SettingsManager.PatchTalkgroupMember>>> MembershipsCommitted;
        public event Action<Dictionary<string, bool>> GroupModesCommitted;
        public event Action<Dictionary<string, bool>> GroupEnabledStatesCommitted;
        public event EventHandler<PatchGroupPttEventArgs> PatchPttStateChanged;

        public enum PatchTalkgroupState
        {
            Idle = 0,
            Receiving = 1,
            Transmitting = 2
        }

        public const string CHANNEL_DRAG_FORMAT = "dvmconsole/channel-drag";
        private const string PATCH_EDIT_PTT_BLOCKED_MESSAGE = "PTT is disabled while patch editing is active.";

        public sealed class ChannelDragData
        {
            public string ChannelName { get; set; } = string.Empty;
            public string SystemName { get; set; } = string.Empty;
            public string Tgid { get; set; } = string.Empty;
        }

        private sealed class ChannelIdentity
        {
            public string ChannelName { get; set; } = string.Empty;
            public string SystemName { get; set; } = string.Empty;
            public string Tgid { get; set; } = string.Empty;

            public string Key => BuildIdentityKey(SystemName, Tgid);
        }

        private sealed class PatchTabContext
        {
            public string GroupName { get; set; } = string.Empty;
            public string GroupType { get; set; } = "patch";
            public Image PttIcon { get; set; }
            public Button PttButton { get; set; }
            public TextBlock PttText { get; set; }
            public Button EditButton { get; set; }
            public Image EditIcon { get; set; }
            public TextBlock EditText { get; set; }
            public CheckBox OneWayToggle { get; set; }
            public CheckBox PatchEnabledToggle { get; set; }
            public TextBlock MemberOrderHint { get; set; }
            public Border StatusBorder { get; set; }
            public TextBlock StatusText { get; set; }
            public ListBox TalkgroupListBox { get; set; }
            public List<ChannelIdentity> Members { get; set; } = new List<ChannelIdentity>();
            public bool IsEditing { get; set; }
            public bool IsPttActive { get; set; }
            public bool IsOneWay { get; set; }
            public bool IsPatchEnabled { get; set; }
        }

        private static readonly BitmapImage TRANSMIT_OUT_PATCH_ICON = new BitmapImage(new Uri("pack://application:,,,/dvmconsole;component/Assets/transmit_out_patch.png"));
        private static readonly BitmapImage TRANSMIT_IN_PATCH_ICON = new BitmapImage(new Uri("pack://application:,,,/dvmconsole;component/Assets/transmit_in_patch.png"));
        private static readonly BitmapImage PATCH_EDIT_OFF_ICON = new BitmapImage(new Uri("pack://application:,,,/dvmconsole;component/Assets/patch_edit_off.png"));
        private static readonly BitmapImage PATCH_EDIT_ON_ICON = new BitmapImage(new Uri("pack://application:,,,/dvmconsole;component/Assets/patch_edit_on.png"));
        private static readonly BitmapImage TRANSMIT_OUT_MSEL_ICON = new BitmapImage(new Uri("pack://application:,,,/dvmconsole;component/Assets/transmit_out_msel.png"));
        private static readonly BitmapImage TRANSMIT_IN_MSEL_ICON = new BitmapImage(new Uri("pack://application:,,,/dvmconsole;component/Assets/transmit_in_msel.png"));
        private static readonly BitmapImage MSEL_EDIT_OFF_ICON = new BitmapImage(new Uri("pack://application:,,,/dvmconsole;component/Assets/msel_inactive.png"));
        private static readonly BitmapImage MSEL_EDIT_ON_ICON = new BitmapImage(new Uri("pack://application:,,,/dvmconsole;component/Assets/msel_active.png"));
        private static readonly BitmapImage STATUS_RECEIVING_ICON = new BitmapImage(new Uri("pack://application:,,,/dvmconsole;component/Assets/ind_transmit_busy.png"));
        private static readonly BitmapImage STATUS_TRANSMITTING_ICON = new BitmapImage(new Uri("pack://application:,,,/dvmconsole;component/Assets/ind_transmit_select.png"));
        private static readonly BitmapImage STATUS_IDLE_ICON = new BitmapImage(new Uri("pack://application:,,,/dvmconsole;component/Assets/ind_transmit_callback_select.png"));
        private static readonly Brush BUTTON_IDLE_BACKGROUND_DARK = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
        private static readonly Brush BUTTON_IDLE_BACKGROUND_LIGHT = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2));
        private static readonly Brush PANEL_IDLE_BACKGROUND_DARK = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B));
        private static readonly Brush PANEL_IDLE_BACKGROUND_LIGHT = new SolidColorBrush(Color.FromRgb(0xF7, 0xF7, 0xF7));
        private static readonly Brush EDIT_STATUS_BACKGROUND_DARK = new SolidColorBrush(Color.FromRgb(0x0D, 0x47, 0x6B));
        private static readonly Brush EDIT_STATUS_BACKGROUND_LIGHT = new SolidColorBrush(Color.FromRgb(0xD8, 0xEC, 0xFB));
        private static readonly Brush INFO_TEXT_BRUSH_DARK = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6));
        private static readonly Brush INFO_TEXT_BRUSH_LIGHT = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
        private static readonly Brush MUTED_TEXT_BRUSH_DARK = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0));
        private static readonly Brush MUTED_TEXT_BRUSH_LIGHT = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
        private static readonly Brush LIST_BACKGROUND_DARK = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x28));
        private static readonly Brush LIST_BACKGROUND_LIGHT = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
        private static readonly Brush LIST_BORDER_DARK = Brushes.DimGray;
        private static readonly Brush LIST_BORDER_LIGHT = new SolidColorBrush(Color.FromRgb(0xB8, 0xB8, 0xB8));

        private readonly SettingsManager settingsManager;
        private readonly Func<string, string, PatchTalkgroupState> talkgroupStateResolver;
        private readonly Dictionary<string, PatchTabContext> tabContexts = new Dictionary<string, PatchTabContext>();
        private Dictionary<string, ChannelIdentity> validChannelsByKey = new Dictionary<string, ChannelIdentity>();
        private Dictionary<string, List<SettingsManager.PatchTalkgroupMember>> lastPersistedMemberships = new Dictionary<string, List<SettingsManager.PatchTalkgroupMember>>();
        private Dictionary<string, bool> lastPersistedModes = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, bool> lastPersistedEnabledStates = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private string membershipContextKey = string.Empty;

        private bool IsDarkTheme => settingsManager?.DarkMode == true;
        private Brush ButtonIdleBackground => IsDarkTheme ? BUTTON_IDLE_BACKGROUND_DARK : BUTTON_IDLE_BACKGROUND_LIGHT;
        private Brush PanelIdleBackground => IsDarkTheme ? PANEL_IDLE_BACKGROUND_DARK : PANEL_IDLE_BACKGROUND_LIGHT;
        private Brush EditStatusBackground => IsDarkTheme ? EDIT_STATUS_BACKGROUND_DARK : EDIT_STATUS_BACKGROUND_LIGHT;
        private Brush InfoTextBrush => IsDarkTheme ? INFO_TEXT_BRUSH_DARK : INFO_TEXT_BRUSH_LIGHT;
        private Brush MutedTextBrush => IsDarkTheme ? MUTED_TEXT_BRUSH_DARK : MUTED_TEXT_BRUSH_LIGHT;
        private Brush ListBackground => IsDarkTheme ? LIST_BACKGROUND_DARK : LIST_BACKGROUND_LIGHT;
        private Brush ListBorderBrush => IsDarkTheme ? LIST_BORDER_DARK : LIST_BORDER_LIGHT;

        /// <summary>
        /// Gets whether any patch group is currently in edit mode.
        /// </summary>
        public bool IsAnyGroupEditing => tabContexts.Values.Any(c => c.IsEditing);

        /// <summary>
        /// Gets the in-memory memberships for the current session.
        /// </summary>
        public Dictionary<string, List<SettingsManager.PatchTalkgroupMember>> GetCurrentMemberships()
        {
            return CloneMemberships(tabContexts.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.Members
                    .Select(m => new SettingsManager.PatchTalkgroupMember
                    {
                        SystemName = m.SystemName,
                        Tgid = m.Tgid
                    })
                    .ToList()));
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PatchGroupsWindow"/> class.
        /// </summary>
        public PatchGroupsWindow(SettingsManager settingsManager, Func<string, string, PatchTalkgroupState> talkgroupStateResolver)
        {
            InitializeComponent();
            this.settingsManager = settingsManager;
            this.talkgroupStateResolver = talkgroupStateResolver;
            patchGroupTabs.SelectionChanged += PatchGroupTabs_SelectionChanged;
        }

        /// <summary>
        /// Prevents the window from being destroyed when closed.
        /// </summary>
        /// <param name="e"></param>
        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }

        /// <summary>
        /// Sets membership context used for persisted patch memberships.
        /// </summary>
        /// <param name="contextKey"></param>
        public void SetMembershipContext(string contextKey)
        {
            membershipContextKey = contextKey ?? string.Empty;
        }

        /// <summary>
        /// Rebuilds tabs from the configured patch groups and valid channel names.
        /// </summary>
        /// <param name="patchGroups"></param>
        /// <param name="channels"></param>
        public void SetPatchGroups(IEnumerable<Codeplug.Group> patchGroups, IEnumerable<Codeplug.Channel> channels)
        {
            patchGroupTabs.Items.Clear();
            tabContexts.Clear();

            validChannelsByKey = (channels ?? Enumerable.Empty<Codeplug.Channel>())
                .Where(c => !string.IsNullOrWhiteSpace(c?.System) && !string.IsNullOrWhiteSpace(c?.Tgid))
                .Select(c => new ChannelIdentity
                {
                    ChannelName = c.Name ?? string.Empty,
                    SystemName = c.System.Trim(),
                    Tgid = c.Tgid.Trim()
                })
                .GroupBy(c => c.Key)
                .ToDictionary(g => g.Key, g => g.First());

            if (patchGroups == null)
                return;

            Dictionary<string, List<SettingsManager.PatchTalkgroupMember>> persistedMemberships = settingsManager.GetPatchGroupMemberships(membershipContextKey);
            Dictionary<string, bool> persistedModes = settingsManager.GetPatchGroupModes(membershipContextKey);
            Dictionary<string, bool> persistedEnabledStates = settingsManager.RetainPatchStateOnStartup
                ? settingsManager.GetPatchGroupEnabledStates(membershipContextKey)
                : new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            lastPersistedMemberships = CloneMemberships(persistedMemberships);
            lastPersistedModes = new Dictionary<string, bool>(persistedModes ?? new Dictionary<string, bool>(), StringComparer.OrdinalIgnoreCase);
            lastPersistedEnabledStates = new Dictionary<string, bool>(persistedEnabledStates ?? new Dictionary<string, bool>(), StringComparer.OrdinalIgnoreCase);

            foreach (Codeplug.Group patchGroup in patchGroups.Where(pg => !string.IsNullOrWhiteSpace(pg?.Name)))
            {
                bool isMultiSelect = patchGroup.IsMultiselectGroup();
                bool isOneWay = !isMultiSelect &&
                    persistedModes.TryGetValue(patchGroup.Name.Trim(), out bool persistedOneWay) &&
                    persistedOneWay;
                bool isPatchEnabled = isMultiSelect || (persistedEnabledStates.TryGetValue(patchGroup.Name.Trim(), out bool persistedEnabled) && persistedEnabled);
                PatchTabContext context = new PatchTabContext
                {
                    GroupName = patchGroup.Name,
                    GroupType = isMultiSelect ? "multiselect" : "patch",
                    IsOneWay = isOneWay,
                    IsPatchEnabled = isPatchEnabled
                };

                Image pttIcon = new Image
                {
                    Source = isMultiSelect ? TRANSMIT_OUT_MSEL_ICON : TRANSMIT_OUT_PATCH_ICON,
                    Width = 24,
                    Height = 24,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                TextBlock pttText = new TextBlock
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = FontWeights.SemiBold,
                    Text = isMultiSelect ? "Multi-Select PTT" : "Patch PTT"
                };
                Button pttButton = new Button
                {
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Children = { pttIcon, pttText }
                    },
                    Height = 56,
                    Margin = new Thickness(6, 0, 0, 8),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Padding = new Thickness(12, 6, 12, 6),
                    Background = ButtonIdleBackground,
                    Foreground = InfoTextBrush,
                    BorderThickness = new Thickness(1),
                    BorderBrush = GetAccentBrush(context),
                    ToolTip = string.Empty,
                    Uid = "PatchPtt",
                    Tag = context
                };
                pttButton.Click += PatchPttButton_Click;
                context.PttButton = pttButton;
                context.PttIcon = pttIcon;
                context.PttText = pttText;

                Image editIcon = new Image
                {
                    Source = isMultiSelect ? MSEL_EDIT_OFF_ICON : PATCH_EDIT_OFF_ICON,
                    Width = 24,
                    Height = 24,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                TextBlock editText = new TextBlock
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = FontWeights.SemiBold,
                    Text = "Edit Members"
                };
                Button editButton = new Button
                {
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Children = { editIcon, editText }
                    },
                    Height = 56,
                    Margin = new Thickness(0, 0, 6, 8),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Padding = new Thickness(12, 6, 12, 6),
                    Background = ButtonIdleBackground,
                    Foreground = InfoTextBrush,
                    BorderThickness = new Thickness(1),
                    BorderBrush = GetAccentBrush(context),
                    ToolTip = string.Empty,
                    Uid = "PatchEdit",
                    Tag = context
                };
                editButton.Click += PatchEditButton_Click;
                context.EditButton = editButton;
                context.EditIcon = editIcon;
                context.EditText = editText;

                Grid contentGrid = new Grid
                {
                    Margin = new Thickness(8)
                };
                contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                Grid buttonGrid = new Grid();
                buttonGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                buttonGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                Grid.SetColumn(editButton, 0);
                Grid.SetColumn(pttButton, 1);
                buttonGrid.Children.Add(pttButton);
                buttonGrid.Children.Add(editButton);

                TextBlock statusText = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = InfoTextBrush
                };
                Border statusBorder = new Border
                {
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(10, 8, 10, 8),
                    Margin = new Thickness(0, 0, 0, 8),
                    Background = PanelIdleBackground,
                    Child = statusText
                };
                context.StatusBorder = statusBorder;
                context.StatusText = statusText;

                CheckBox oneWayToggle = new CheckBox
                {
                    Content = "Enable One-Way Patch",
                    Margin = new Thickness(6, 0, 6, 2),
                    IsChecked = context.IsOneWay,
                    Foreground = InfoTextBrush,
                    Visibility = isMultiSelect ? Visibility.Collapsed : Visibility.Visible,
                    Tag = context
                };
                oneWayToggle.Checked += OneWayToggle_Changed;
                oneWayToggle.Unchecked += OneWayToggle_Changed;
                context.OneWayToggle = oneWayToggle;

                CheckBox patchEnabledToggle = new CheckBox
                {
                    Content = "Patch Enabled",
                    Margin = new Thickness(6, 0, 6, 4),
                    IsChecked = context.IsPatchEnabled,
                    Foreground = InfoTextBrush,
                    Visibility = isMultiSelect ? Visibility.Collapsed : Visibility.Visible,
                    Tag = context
                };
                patchEnabledToggle.Checked += PatchEnabledToggle_Changed;
                patchEnabledToggle.Unchecked += PatchEnabledToggle_Changed;
                context.PatchEnabledToggle = patchEnabledToggle;

                TextBlock memberOrderHint = new TextBlock
                {
                    Margin = new Thickness(26, 2, 6, 8),
                    Text = "Member Order: 1 = Source, 2+ = Destinations",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = MutedTextBrush,
                    FontStyle = FontStyles.Italic,
                    Visibility = Visibility.Collapsed
                };
                context.MemberOrderHint = memberOrderHint;

                StackPanel oneWayPanel = new StackPanel();
                oneWayPanel.Children.Add(patchEnabledToggle);
                oneWayPanel.Children.Add(oneWayToggle);
                oneWayPanel.Children.Add(memberOrderHint);

                ListBox talkgroupListBox = new ListBox
                {
                    Margin = new Thickness(0),
                    AllowDrop = true,
                    Background = ListBackground,
                    BorderThickness = new Thickness(1),
                    BorderBrush = ListBorderBrush,
                    Tag = context
                };
                talkgroupListBox.DragOver += TalkgroupListBox_DragOver;
                talkgroupListBox.Drop += TalkgroupListBox_Drop;
                context.TalkgroupListBox = talkgroupListBox;

                Grid.SetRow(buttonGrid, 0);
                Grid.SetRow(statusBorder, 1);
                Grid.SetRow(oneWayPanel, 2);
                Grid.SetRow(talkgroupListBox, 3);
                contentGrid.Children.Add(buttonGrid);
                contentGrid.Children.Add(statusBorder);
                contentGrid.Children.Add(oneWayPanel);
                contentGrid.Children.Add(talkgroupListBox);

                if (persistedMemberships.TryGetValue(context.GroupName, out List<SettingsManager.PatchTalkgroupMember> savedMembers))
                {
                    context.Members = savedMembers
                        .Select(m => BuildIdentity(m.SystemName, m.Tgid))
                        .Where(m => validChannelsByKey.ContainsKey(m.Key))
                        .GroupBy(m => m.Key)
                        .Select(g => validChannelsByKey[g.Key])
                        .ToList();
                }

                TabItem tab = new TabItem
                {
                    Header = context.GroupName,
                    Content = contentGrid,
                    MinWidth = 88,
                    MaxWidth = 124,
                    Height = 40,
                    Margin = new Thickness(0, 0, 4, 4),
                    Foreground = Brushes.White
                };
                ColorZoneAssist.SetMode(tab, ColorZoneMode.Custom);
                ColorZoneAssist.SetBackground(tab, (SolidColorBrush)GetAccentBrush(context));

                patchGroupTabs.Items.Add(tab);
                tabContexts[context.GroupName] = context;
                UpdateContextVisualState(context);
                RebuildTalkgroupList(context);
            }

            if (patchGroupTabs.Items.Count > 0)
                patchGroupTabs.SelectedIndex = 0;
            PersistAllModes();
            lastPersistedEnabledStates = BuildPatchEnabledMap();
            GroupModesCommitted?.Invoke(BuildGroupModesMap());
            GroupEnabledStatesCommitted?.Invoke(BuildPatchEnabledMap());
        }

        /// <summary>
        /// Refreshes member status icons for all patch tabs.
        /// </summary>
        public void RefreshMemberStatusIcons()
        {
            foreach (PatchTabContext context in tabContexts.Values)
                RefreshMemberStatusIcons(context);
        }

        /// <summary>
        /// Reapplies dynamic light/dark theme brushes to all group controls.
        /// </summary>
        public void RefreshTheme()
        {
            foreach (PatchTabContext context in tabContexts.Values)
                RebuildTalkgroupList(context);
        }

        /// <summary>
        /// Resets patch control states when switching tabs.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void PatchGroupTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ReferenceEquals(sender, patchGroupTabs))
                return;
            if (!ReferenceEquals(e.OriginalSource, patchGroupTabs))
                return;

            foreach (PatchTabContext context in tabContexts.Values)
                DeactivateContext(context, commitChanges: true);
        }

        /// <summary>
        /// Resets patch control buttons back to their default inactive state.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="commitChanges"></param>
        private void DeactivateContext(PatchTabContext context, bool commitChanges)
        {
            context.IsEditing = false;
            bool wasPttActive = context.IsPttActive;
            context.IsPttActive = false;
            if (context.PttButton != null)
                context.PttButton.Tag = context;
            UpdateContextVisualState(context);
            if (wasPttActive)
                RaisePatchPttStateChanged(context, false);
            RebuildTalkgroupList(context);
            if (commitChanges)
                PersistAllMemberships();
        }

        /// <summary>
        /// Toggles the patch PTT button icon between active and inactive states.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void PatchPttButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not PatchTabContext context)
                return;
            if (context.GroupType.Equals("patch", StringComparison.OrdinalIgnoreCase) && !context.IsPatchEnabled)
                return;
            if (!context.IsPttActive && IsAnyGroupEditing)
            {
                MessageBox.Show(PATCH_EDIT_PTT_BLOCKED_MESSAGE, "Patch Editing Active", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            context.IsPttActive = !context.IsPttActive;
            UpdateContextVisualState(context);
            RaisePatchPttStateChanged(context, context.IsPttActive);
        }

        /// <summary>
        /// Toggles the patch edit button icon between active and inactive states.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void PatchEditButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not PatchTabContext context)
                return;

            bool isActive = !context.IsEditing;
            foreach (PatchTabContext otherContext in tabContexts.Values.Where(c => c != context))
                DeactivateContext(otherContext, commitChanges: true);

            if (isActive && context.IsPttActive)
            {
                context.IsPttActive = false;
                RaisePatchPttStateChanged(context, false);
            }

            context.IsEditing = isActive;
            UpdateAllContextVisualStates();
            RebuildTalkgroupList(context);
            if (!isActive)
                PersistAllMemberships();
        }

        /// <summary>
        /// Handles one-way mode changes for patch groups.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OneWayToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox checkBox || checkBox.Tag is not PatchTabContext context)
                return;
            if (!context.GroupType.Equals("patch", StringComparison.OrdinalIgnoreCase))
                return;

            context.IsOneWay = checkBox.IsChecked == true;
            UpdateContextVisualState(context);
            PersistAllModes();
            GroupModesCommitted?.Invoke(BuildGroupModesMap());
        }

        /// <summary>
        /// Handles patch enabled state changes for patch groups.
        /// </summary>
        private void PatchEnabledToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox checkBox || checkBox.Tag is not PatchTabContext context)
                return;
            if (!context.GroupType.Equals("patch", StringComparison.OrdinalIgnoreCase))
                return;

            context.IsPatchEnabled = checkBox.IsChecked == true;
            if (!context.IsPatchEnabled && context.IsPttActive)
            {
                context.IsPttActive = false;
                RaisePatchPttStateChanged(context, false);
            }

            UpdateContextVisualState(context);
            PersistAllEnabledStates();
            GroupEnabledStatesCommitted?.Invoke(BuildPatchEnabledMap());
        }

        /// <summary>
        /// Handles drag over for patch member listboxes.
        /// </summary>
        private static void TalkgroupListBox_DragOver(object sender, DragEventArgs e)
        {
            if (sender is not ListBox listBox || listBox.Tag is not PatchTabContext context)
            {
                e.Effects = DragDropEffects.None;
                return;
            }

            if (!context.IsEditing || !e.Data.GetDataPresent(CHANNEL_DRAG_FORMAT))
            {
                e.Effects = DragDropEffects.None;
                return;
            }

            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }

        /// <summary>
        /// Handles drop add operations from main channel widgets.
        /// </summary>
        private void TalkgroupListBox_Drop(object sender, DragEventArgs e)
        {
            if (sender is not ListBox listBox || listBox.Tag is not PatchTabContext context)
                return;
            if (!context.IsEditing || !e.Data.GetDataPresent(CHANNEL_DRAG_FORMAT))
                return;
            if (e.Data.GetData(CHANNEL_DRAG_FORMAT) is not ChannelDragData payload)
                return;

            ChannelIdentity identity = BuildIdentity(payload.SystemName, payload.Tgid);
            if (!validChannelsByKey.TryGetValue(identity.Key, out ChannelIdentity canonicalIdentity))
                return;
            if (context.Members.Any(m => m.Key == canonicalIdentity.Key))
                return;

            context.Members.Add(canonicalIdentity);
            RebuildTalkgroupList(context);
            e.Handled = true;
        }

        /// <summary>
        /// Rebuilds the patch member list for a context.
        /// </summary>
        /// <param name="context"></param>
        private void RebuildTalkgroupList(PatchTabContext context)
        {
            context.TalkgroupListBox.Items.Clear();
            UpdateContextVisualState(context);

            if (context.Members.Count == 0)
            {
                context.TalkgroupListBox.Items.Add(new ListBoxItem
                {
                    Content = new TextBlock
                    {
                        Text = context.IsEditing
                            ? "Drag channels here from the main console to add them to this group."
                            : "No members yet. Click Edit Members to start building this group.",
                        Foreground = MutedTextBrush,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(8)
                    },
                    IsEnabled = false
                });

                return;
            }

            foreach (ChannelIdentity member in context.Members)
            {
                Image statusIcon = new Image
                {
                    Source = STATUS_IDLE_ICON,
                    Width = 16,
                    Height = 16,
                    Margin = new Thickness(0, 0, 8, 0)
                };

                TextBlock nameText = new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(member.SystemName)
                        ? member.ChannelName
                        : $"{member.ChannelName} ({member.SystemName})",
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };

                Button removeButton = new Button
                {
                    Content = new TextBlock
                    {
                        Text = "Remove",
                        Foreground = InfoTextBrush,
                        FontWeight = FontWeights.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center
                    },
                    MinWidth = 70,
                    Height = 24,
                    Padding = new Thickness(8, 0, 8, 0),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = ButtonIdleBackground,
                    Foreground = InfoTextBrush,
                    BorderBrush = GetAccentBrush(context),
                    BorderThickness = new Thickness(1),
                    Visibility = context.IsEditing ? Visibility.Visible : Visibility.Collapsed,
                    Tag = member.Key,
                    ToolTip = $"Remove {member.ChannelName} from {context.GroupName}"
                };
                removeButton.Click += (s, e) =>
                {
                    if (s is Button btn && btn.Tag is string key)
                        RemoveTalkgroupMember(context, key);
                };

                Grid rowGrid = new Grid();
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                Grid.SetColumn(statusIcon, 0);
                Grid.SetColumn(nameText, 1);
                Grid.SetColumn(removeButton, 2);
                rowGrid.Children.Add(statusIcon);
                rowGrid.Children.Add(nameText);
                rowGrid.Children.Add(removeButton);

                ListBoxItem item = new ListBoxItem
                {
                    Content = rowGrid,
                    Tag = member,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch
                };

                context.TalkgroupListBox.Items.Add(item);
            }

            RefreshMemberStatusIcons(context);
        }

        /// <summary>
        /// Removes a member from the patch group.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="memberKey"></param>
        private void RemoveTalkgroupMember(PatchTabContext context, string memberKey)
        {
            if (!context.IsEditing || string.IsNullOrWhiteSpace(memberKey))
                return;

            ChannelIdentity toRemove = context.Members.FirstOrDefault(m => m.Key == memberKey);
            if (toRemove == null)
                return;

            context.Members.Remove(toRemove);
            RebuildTalkgroupList(context);
        }

        /// <summary>
        /// Refreshes patch member status icons for a context.
        /// </summary>
        /// <param name="context"></param>
        private void RefreshMemberStatusIcons(PatchTabContext context)
        {
            foreach (ListBoxItem item in context.TalkgroupListBox.Items.OfType<ListBoxItem>())
            {
                if (item.Tag is not ChannelIdentity member || item.Content is not Grid rowGrid)
                    continue;

                Image statusIcon = rowGrid.Children.OfType<Image>().FirstOrDefault();
                if (statusIcon == null)
                    continue;

                PatchTalkgroupState state = talkgroupStateResolver?.Invoke(member.SystemName, member.Tgid) ?? PatchTalkgroupState.Idle;
                switch (state)
                {
                    case PatchTalkgroupState.Receiving:
                        statusIcon.Source = STATUS_RECEIVING_ICON;
                        break;
                    case PatchTalkgroupState.Transmitting:
                        statusIcon.Source = STATUS_TRANSMITTING_ICON;
                        break;
                    default:
                        statusIcon.Source = STATUS_IDLE_ICON;
                        break;
                }
            }
        }

        /// <summary>
        /// Persists all patch memberships to settings.
        /// </summary>
        private void PersistAllMemberships()
        {
            Dictionary<string, List<SettingsManager.PatchTalkgroupMember>> memberships = new Dictionary<string, List<SettingsManager.PatchTalkgroupMember>>();
            foreach (PatchTabContext context in tabContexts.Values)
            {
                memberships[context.GroupName] = context.Members
                    .Select(m => new SettingsManager.PatchTalkgroupMember
                    {
                        SystemName = m.SystemName,
                        Tgid = m.Tgid
                    })
                    .ToList();
            }

            if (MembershipsEqual(lastPersistedMemberships, memberships))
                return;

            settingsManager.SavePatchGroupMemberships(membershipContextKey, memberships);
            lastPersistedMemberships = CloneMemberships(memberships);
            MembershipsCommitted?.Invoke(CloneMemberships(memberships));
        }

        /// <summary>
        /// Persists one-way mode state for patch groups.
        /// </summary>
        private void PersistAllModes()
        {
            Dictionary<string, bool> modes = BuildGroupModesMap();
            if (ModeMapsEqual(lastPersistedModes, modes))
                return;

            settingsManager.SavePatchGroupModes(membershipContextKey, modes);
            lastPersistedModes = new Dictionary<string, bool>(modes, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Persists enabled state for patch groups.
        /// </summary>
        private void PersistAllEnabledStates()
        {
            Dictionary<string, bool> states = BuildPatchEnabledMap();
            if (ModeMapsEqual(lastPersistedEnabledStates, states))
                return;

            settingsManager.SavePatchGroupEnabledStates(membershipContextKey, states);
            lastPersistedEnabledStates = new Dictionary<string, bool>(states, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Clones a memberships dictionary.
        /// </summary>
        /// <param name="memberships"></param>
        /// <returns></returns>
        private static Dictionary<string, List<SettingsManager.PatchTalkgroupMember>> CloneMemberships(Dictionary<string, List<SettingsManager.PatchTalkgroupMember>> memberships)
        {
            Dictionary<string, List<SettingsManager.PatchTalkgroupMember>> copy = new Dictionary<string, List<SettingsManager.PatchTalkgroupMember>>();
            foreach (KeyValuePair<string, List<SettingsManager.PatchTalkgroupMember>> kvp in memberships ?? new Dictionary<string, List<SettingsManager.PatchTalkgroupMember>>())
            {
                copy[kvp.Key] = (kvp.Value ?? new List<SettingsManager.PatchTalkgroupMember>())
                    .Select(m => new SettingsManager.PatchTalkgroupMember
                    {
                        SystemName = m.SystemName,
                        Tgid = m.Tgid
                    })
                    .ToList();
            }

            return copy;
        }

        /// <summary>
        /// Determines whether two membership dictionaries are equal.
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        private static bool MembershipsEqual(Dictionary<string, List<SettingsManager.PatchTalkgroupMember>> left, Dictionary<string, List<SettingsManager.PatchTalkgroupMember>> right)
        {
            left ??= new Dictionary<string, List<SettingsManager.PatchTalkgroupMember>>();
            right ??= new Dictionary<string, List<SettingsManager.PatchTalkgroupMember>>();
            if (left.Count != right.Count)
                return false;

            foreach (KeyValuePair<string, List<SettingsManager.PatchTalkgroupMember>> kvp in left)
            {
                if (!right.TryGetValue(kvp.Key, out List<SettingsManager.PatchTalkgroupMember> rightMembers))
                    return false;

                HashSet<string> leftSet = new HashSet<string>((kvp.Value ?? new List<SettingsManager.PatchTalkgroupMember>())
                    .Select(m => BuildIdentityKey(m.SystemName, m.Tgid)));
                HashSet<string> rightSet = new HashSet<string>((rightMembers ?? new List<SettingsManager.PatchTalkgroupMember>())
                    .Select(m => BuildIdentityKey(m.SystemName, m.Tgid)));
                if (!leftSet.SetEquals(rightSet))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Determines whether two mode maps are equal.
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        private static bool ModeMapsEqual(Dictionary<string, bool> left, Dictionary<string, bool> right)
        {
            left ??= new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            right ??= new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            if (left.Count != right.Count)
                return false;

            foreach (KeyValuePair<string, bool> kvp in left)
            {
                if (!right.TryGetValue(kvp.Key, out bool rightValue))
                    return false;
                if (kvp.Value != rightValue)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Raises patch PTT state changes to the host window.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="isActive"></param>
        private void RaisePatchPttStateChanged(PatchTabContext context, bool isActive)
        {
            PatchPttStateChanged?.Invoke(this, new PatchGroupPttEventArgs
            {
                GroupName = context.GroupName,
                IsActive = isActive,
                Members = context.Members.Select(m => new SettingsManager.PatchTalkgroupMember
                {
                    SystemName = m.SystemName,
                    Tgid = m.Tgid
                }).ToList()
            });
        }

        /// <summary>
        /// Returns one-way mode map for patch groups.
        /// </summary>
        /// <returns></returns>
        public Dictionary<string, bool> GetPatchGroupModes()
        {
            return BuildGroupModesMap();
        }

        /// <summary>
        /// Returns enabled-state map for patch groups.
        /// </summary>
        public Dictionary<string, bool> GetPatchGroupEnabledStates()
        {
            return BuildPatchEnabledMap();
        }

        /// <summary>
        /// Builds normalized identity key.
        /// </summary>
        private static string BuildIdentityKey(string systemName, string tgid)
        {
            string system = systemName?.Trim().ToLowerInvariant() ?? string.Empty;
            string tg = tgid?.Trim() ?? string.Empty;
            return $"{system}|{tg}";
        }

        /// <summary>
        /// Creates a member identity object.
        /// </summary>
        private static ChannelIdentity BuildIdentity(string systemName, string tgid)
        {
            return new ChannelIdentity
            {
                SystemName = systemName?.Trim() ?? string.Empty,
                Tgid = tgid?.Trim() ?? string.Empty
            };
        }

        private Dictionary<string, bool> BuildGroupModesMap()
        {
            Dictionary<string, bool> modes = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (PatchTabContext context in tabContexts.Values)
            {
                if (!context.GroupType.Equals("patch", StringComparison.OrdinalIgnoreCase))
                    continue;
                modes[context.GroupName] = context.IsOneWay;
            }

            return modes;
        }

        private Dictionary<string, bool> BuildPatchEnabledMap()
        {
            Dictionary<string, bool> states = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (PatchTabContext context in tabContexts.Values)
            {
                if (!context.GroupType.Equals("patch", StringComparison.OrdinalIgnoreCase))
                    continue;
                states[context.GroupName] = context.IsPatchEnabled;
            }

            return states;
        }

        private void UpdateContextVisualState(PatchTabContext context)
        {
            if (context == null)
                return;

            bool isMultiSelect = context.GroupType.Equals("multiselect", StringComparison.OrdinalIgnoreCase);
            string groupKind = isMultiSelect ? "multi-select" : "patch";
            bool patchAvailable = isMultiSelect || context.IsPatchEnabled;

            if (context.EditIcon != null)
                context.EditIcon.Source = context.IsEditing ? GetEditActiveIcon(context) : GetEditInactiveIcon(context);
            if (context.PttIcon != null)
                context.PttIcon.Source = context.IsPttActive ? GetPttActiveIcon(context) : GetPttInactiveIcon(context);

            if (context.EditText != null)
                context.EditText.Text = context.IsEditing ? "Stop Editing" : "Edit Members";
            if (context.PttText != null)
                context.PttText.Text = context.IsPttActive
                    ? (isMultiSelect ? "Stop Multi-Select PTT" : "Stop Patch PTT")
                    : (isMultiSelect ? "Multi-Select PTT" : "Patch PTT");

            if (context.EditButton != null)
            {
                context.EditButton.Background = context.IsEditing ? GetAccentBrush(context) : ButtonIdleBackground;
                context.EditButton.Foreground = InfoTextBrush;
                context.EditButton.ToolTip = context.IsEditing
                    ? $"Editing {context.GroupName}. Drag channels from the main console into this group, use Remove to take them out, then click Stop Editing."
                    : $"Edit members for {context.GroupName}. Click to start editing, then drag channels from the main console into this group.";
            }

            if (context.PttButton != null)
            {
                context.PttButton.Background = context.IsPttActive ? GetAccentBrush(context) : ButtonIdleBackground;
                context.PttButton.Foreground = InfoTextBrush;
                bool pttAvailable = patchAvailable && (!IsAnyGroupEditing || context.IsPttActive);
                context.PttButton.IsEnabled = pttAvailable;
                context.PttButton.Opacity = pttAvailable ? 1.0 : 0.55;
                context.PttButton.ToolTip = !patchAvailable
                    ? $"Enable {context.GroupName} before using Patch PTT."
                    : IsAnyGroupEditing && !context.IsPttActive
                        ? "PTT is disabled while patch editing is active. Click Stop Editing before transmitting."
                    : context.IsPttActive
                        ? $"Transmitting to every member in {context.GroupName}. Click again to stop."
                        : $"Transmit to every member in {context.GroupName}. Use this when you want to talk to the whole {groupKind} at once.";
            }

            if (context.StatusBorder != null)
            {
                context.StatusBorder.Background = context.IsEditing ? EditStatusBackground : PanelIdleBackground;
                context.StatusBorder.Visibility = context.IsEditing ? Visibility.Visible : Visibility.Collapsed;
            }

            if (context.StatusText != null)
            {
                if (context.IsEditing)
                {
                    context.StatusText.Text = "Editing is active. Drag channels from the main console into this list. Use Remove to take channels out, then click Stop Editing when you are done.";
                }
                else
                {
                    context.StatusText.Text = string.Empty;
                }
                context.StatusText.Foreground = InfoTextBrush;
            }

            if (context.MemberOrderHint != null)
            {
                context.MemberOrderHint.Visibility = context.IsOneWay ? Visibility.Visible : Visibility.Collapsed;
                context.MemberOrderHint.Foreground = MutedTextBrush;
            }

            if (context.OneWayToggle != null)
            {
                context.OneWayToggle.Foreground = InfoTextBrush;
                context.OneWayToggle.ToolTip = context.IsOneWay
                    ? "Disable one-way patch mode."
                    : "Enable one-way patch mode.";
            }

            if (context.PatchEnabledToggle != null)
            {
                context.PatchEnabledToggle.Foreground = InfoTextBrush;
                context.PatchEnabledToggle.ToolTip = context.IsPatchEnabled
                    ? $"Disable {context.GroupName}. Members stay assigned, but the patch becomes inactive."
                    : $"Enable {context.GroupName}. Members stay assigned and traffic can be forwarded again.";
            }

            if (context.TalkgroupListBox != null)
            {
                context.TalkgroupListBox.Background = ListBackground;
                context.TalkgroupListBox.BorderBrush = context.IsEditing ? GetAccentBrush(context) : ListBorderBrush;
                context.TalkgroupListBox.ToolTip = context.IsEditing
                    ? "Editing is active. Drag channels here from the main console."
                    : "Group members appear here.";
            }
        }

        private void UpdateAllContextVisualStates()
        {
            foreach (PatchTabContext context in tabContexts.Values)
                UpdateContextVisualState(context);
        }

        private static Brush GetAccentBrush(PatchTabContext context)
        {
            return context.GroupType.Equals("multiselect", StringComparison.OrdinalIgnoreCase)
                ? new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32))
                : new SolidColorBrush(Color.FromRgb(0x15, 0x65, 0xC0));
        }

        private static BitmapImage GetPttInactiveIcon(PatchTabContext context) => context.GroupType == "multiselect" ? TRANSMIT_OUT_MSEL_ICON : TRANSMIT_OUT_PATCH_ICON;
        private static BitmapImage GetPttActiveIcon(PatchTabContext context) => context.GroupType == "multiselect" ? TRANSMIT_IN_MSEL_ICON : TRANSMIT_IN_PATCH_ICON;
        private static BitmapImage GetEditInactiveIcon(PatchTabContext context) => context.GroupType == "multiselect" ? MSEL_EDIT_OFF_ICON : PATCH_EDIT_OFF_ICON;
        private static BitmapImage GetEditActiveIcon(PatchTabContext context) => context.GroupType == "multiselect" ? MSEL_EDIT_ON_ICON : PATCH_EDIT_ON_ICON;
    }
}
