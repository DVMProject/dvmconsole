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
*
*/

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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
        public event EventHandler<PatchGroupPttEventArgs> PatchPttStateChanged;

        public enum PatchTalkgroupState
        {
            Idle = 0,
            Receiving = 1,
            Transmitting = 2
        }

        public const string CHANNEL_DRAG_FORMAT = "dvmconsole/channel-drag";

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
            public Image PttIcon { get; set; }
            public Button PttButton { get; set; }
            public Image EditIcon { get; set; }
            public ListBox TalkgroupListBox { get; set; }
            public List<ChannelIdentity> Members { get; set; } = new List<ChannelIdentity>();
            public bool IsEditing { get; set; }
            public bool IsPttActive { get; set; }
        }

        private static readonly BitmapImage TRANSMIT_OUT_PATCH_ICON = new BitmapImage(new Uri("pack://application:,,,/dvmconsole;component/Assets/transmit_out_patch.png"));
        private static readonly BitmapImage TRANSMIT_IN_PATCH_ICON = new BitmapImage(new Uri("pack://application:,,,/dvmconsole;component/Assets/transmit_in_patch.png"));
        private static readonly BitmapImage PATCH_EDIT_OFF_ICON = new BitmapImage(new Uri("pack://application:,,,/dvmconsole;component/Assets/patch_edit_off.png"));
        private static readonly BitmapImage PATCH_EDIT_ON_ICON = new BitmapImage(new Uri("pack://application:,,,/dvmconsole;component/Assets/patch_edit_on.png"));
        private static readonly BitmapImage STATUS_RECEIVING_ICON = new BitmapImage(new Uri("pack://application:,,,/dvmconsole;component/Assets/ind_transmit_busy.png"));
        private static readonly BitmapImage STATUS_TRANSMITTING_ICON = new BitmapImage(new Uri("pack://application:,,,/dvmconsole;component/Assets/ind_transmit_select.png"));
        private static readonly BitmapImage STATUS_IDLE_ICON = new BitmapImage(new Uri("pack://application:,,,/dvmconsole;component/Assets/ind_transmit_callback_select.png"));

        private readonly SettingsManager settingsManager;
        private readonly Func<string, string, PatchTalkgroupState> talkgroupStateResolver;
        private readonly Dictionary<string, PatchTabContext> tabContexts = new Dictionary<string, PatchTabContext>();
        private Dictionary<string, ChannelIdentity> validChannelsByKey = new Dictionary<string, ChannelIdentity>();
        private Dictionary<string, List<SettingsManager.PatchTalkgroupMember>> lastPersistedMemberships = new Dictionary<string, List<SettingsManager.PatchTalkgroupMember>>();
        private string membershipContextKey = string.Empty;

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
        public void SetPatchGroups(IEnumerable<Codeplug.PatchGroup> patchGroups, IEnumerable<Codeplug.Channel> channels)
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
            lastPersistedMemberships = CloneMemberships(persistedMemberships);

            foreach (Codeplug.PatchGroup patchGroup in patchGroups.Where(pg => !string.IsNullOrWhiteSpace(pg?.Name)))
            {
                PatchTabContext context = new PatchTabContext
                {
                    GroupName = patchGroup.Name
                };

                Button pttButton = new Button
                {
                    Content = new Image
                    {
                        Source = TRANSMIT_OUT_PATCH_ICON,
                        Width = 32,
                        Height = 32
                    },
                    Height = 52,
                    Margin = new Thickness(6, 0, 0, 8),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Background = Brushes.Transparent,
                    ToolTip = $"Patch PTT - {patchGroup.Name}",
                    Uid = "PatchPtt",
                    Tag = context
                };
                pttButton.Click += PatchPttButton_Click;
                context.PttButton = pttButton;
                context.PttIcon = pttButton.Content as Image;

                Button editButton = new Button
                {
                    Content = new Image
                    {
                        Source = PATCH_EDIT_OFF_ICON,
                        Width = 32,
                        Height = 32
                    },
                    Height = 52,
                    Margin = new Thickness(0, 0, 6, 8),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Background = Brushes.Transparent,
                    ToolTip = $"Edit Patch Group - {patchGroup.Name}",
                    Uid = "PatchEdit",
                    Tag = context
                };
                editButton.Click += PatchEditButton_Click;
                context.EditIcon = editButton.Content as Image;

                Grid contentGrid = new Grid
                {
                    Margin = new Thickness(8)
                };
                contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                Grid buttonGrid = new Grid();
                buttonGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                buttonGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                Grid.SetColumn(editButton, 0);
                Grid.SetColumn(pttButton, 1);
                buttonGrid.Children.Add(pttButton);
                buttonGrid.Children.Add(editButton);

                ListBox talkgroupListBox = new ListBox
                {
                    Margin = new Thickness(0),
                    AllowDrop = true,
                    Tag = context
                };
                talkgroupListBox.DragOver += TalkgroupListBox_DragOver;
                talkgroupListBox.Drop += TalkgroupListBox_Drop;
                context.TalkgroupListBox = talkgroupListBox;

                Grid.SetRow(buttonGrid, 0);
                Grid.SetRow(talkgroupListBox, 1);
                contentGrid.Children.Add(buttonGrid);
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
                    Content = contentGrid
                };

                patchGroupTabs.Items.Add(tab);
                tabContexts[context.GroupName] = context;
                RebuildTalkgroupList(context);
            }

            if (patchGroupTabs.Items.Count > 0)
                patchGroupTabs.SelectedIndex = 0;

            PersistAllMemberships();
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
            if (context.EditIcon != null)
                context.EditIcon.Source = PATCH_EDIT_OFF_ICON;
            if (context.PttIcon != null)
                context.PttIcon.Source = TRANSMIT_OUT_PATCH_ICON;
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
            if (sender is not Button button || button.Content is not Image icon || button.Tag is not PatchTabContext context)
                return;

            context.IsPttActive = !context.IsPttActive;
            icon.Source = context.IsPttActive ? TRANSMIT_IN_PATCH_ICON : TRANSMIT_OUT_PATCH_ICON;
            RaisePatchPttStateChanged(context, context.IsPttActive);
        }

        /// <summary>
        /// Toggles the patch edit button icon between active and inactive states.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void PatchEditButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Content is not Image icon || button.Tag is not PatchTabContext context)
                return;

            bool isActive = !context.IsEditing;
            foreach (PatchTabContext otherContext in tabContexts.Values.Where(c => c != context))
                DeactivateContext(otherContext, commitChanges: true);

            context.IsEditing = isActive;
            icon.Source = isActive ? PATCH_EDIT_ON_ICON : PATCH_EDIT_OFF_ICON;
            RebuildTalkgroupList(context);
            if (!isActive)
                PersistAllMemberships();
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
            context.TalkgroupListBox.ToolTip = context.IsEditing
                ? "Drag resources from the console into the patch group list to add them, then click Edit button again to save."
                : null;

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
                    Text = member.ChannelName,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };

                Button removeButton = new Button
                {
                    Content = new TextBlock
                    {
                        Text = "X",
                        Foreground = Brushes.Gray,
                        FontWeight = FontWeights.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center
                    },
                    Width = 20,
                    Height = 20,
                    Padding = new Thickness(0),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = Brushes.Transparent,
                    BorderBrush = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Visibility = context.IsEditing ? Visibility.Visible : Visibility.Collapsed,
                    Tag = member.Key,
                    ToolTip = "Remove from patch group"
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
    }
}
