// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Desktop Dispatch Console
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Desktop Dispatch Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2024 Caleb, K4PHP
*   Copyright (C) 2025 Steven Jennison, KD8RHO
*   Copyright (C) 2026 C. Lovell, Dev_Ranger
*
*/

using dvmconsole.Controls;
using System.Windows.Threading;

namespace dvmconsole
{
    /// <summary>
    /// Tracks channel selection with UI-owned mutations and thread-safe snapshot reads.
    /// </summary>
    public class SelectedChannelsManager
    {
        private ChannelBox primaryChannel;
        
        private readonly HashSet<ChannelBox> selectedChannels;
        private readonly object selectionLock = new object();
        private readonly Dispatcher dispatcher;

        public ChannelBox PrimaryChannel
        {
            get
            {
                lock (selectionLock)
                    return primaryChannel;
            }
        }

        /// <summary>
        /// Returns a stable membership snapshot. Channel controls remain UI-owned.
        /// </summary>
        public IReadOnlyCollection<ChannelBox> GetSelectedChannels()
        {
            lock (selectionLock)
                return selectedChannels.ToArray();
        }

        /*
        ** Events
        */

        /// <summary>
        /// 
        /// </summary>
        public event Action SelectedChannelsChanged;

        /// <summary>
        /// Triggered when primary channel is changed
        /// </summary>
        public event Action PrimaryChannelChanged;
        /// <summary>
        /// Triggered when a channel selection changes.
        /// </summary>
        public event Action<ChannelBox, bool> ChannelSelectionChanged;
        /*
        ** Methods
        */

        /// <summary>
        /// Initializes a new instance of the <see cref="SelectedChannelsManager"/> class.
        /// Create this manager on the channel controls' UI thread. Mutations marshal to it.
        /// </summary>
        public SelectedChannelsManager()
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            selectedChannels = new HashSet<ChannelBox>();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="channel"></param>
        public void AddSelectedChannel(ChannelBox channel)
        {
            if (!dispatcher.CheckAccess())
            {
                dispatcher.Invoke(() => AddSelectedChannel(channel));
                return;
            }

            lock (selectionLock)
            {
                if (!selectedChannels.Add(channel))
                    return;
            }

            channel.IsSelected = true;
            ChannelSelectionChanged?.Invoke(channel, true);
            SelectedChannelsChanged?.Invoke();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="channel"></param>
        public void RemoveSelectedChannel(ChannelBox channel)
        {
            if (!dispatcher.CheckAccess())
            {
                dispatcher.Invoke(() => RemoveSelectedChannel(channel));
                return;
            }

            bool primaryChanged;
            lock (selectionLock)
            {
                if (!selectedChannels.Remove(channel))
                    return;

                primaryChanged = primaryChannel == channel;
                if (primaryChanged)
                    primaryChannel = null;
            }

            channel.IsPrimary = false;
            channel.IsSelected = false;
            if (primaryChanged)
                PrimaryChannelChanged?.Invoke();
            ChannelSelectionChanged?.Invoke(channel, false);
            SelectedChannelsChanged?.Invoke();
        }

        /// <summary>
        /// 
        /// </summary>
        public void ClearSelections()
        {
            if (!dispatcher.CheckAccess())
            {
                dispatcher.Invoke(ClearSelections);
                return;
            }

            ChannelBox[] channels;
            ChannelBox previousPrimary;
            lock (selectionLock)
            {
                channels = selectedChannels.ToArray();
                selectedChannels.Clear();
                previousPrimary = primaryChannel;
                primaryChannel = null;
            }

            // Finish state and visual cleanup before callbacks can reenter the manager.
            if (previousPrimary != null)
                previousPrimary.IsPrimary = false;
            foreach (var channel in channels)
            {
                channel.IsPrimary = false;
                channel.IsSelected = false;
            }

            if (previousPrimary != null)
                PrimaryChannelChanged?.Invoke();
            foreach (var channel in channels)
                ChannelSelectionChanged?.Invoke(channel, false);
            SelectedChannelsChanged?.Invoke();
        }

        /// <summary>
        /// Sets primary channel to the passed ChannelBox
        /// </summary>
        /// <param name="channel"></param>
        public void SetPrimaryChannel(ChannelBox channel)
        {
            if (!dispatcher.CheckAccess())
            {
                dispatcher.Invoke(() => SetPrimaryChannel(channel));
                return;
            }

            Log.WriteLine($"Setting primary channel to {channel.ChannelName}");
            lock (selectionLock)
                primaryChannel = channel;
            PrimaryChannelChanged?.Invoke();
        }
        
        /// <summary>
        /// Clears the primary channel selection, setting it to null
        /// </summary>
        public void ClearPrimaryChannel()
        {
            if (!dispatcher.CheckAccess())
            {
                dispatcher.Invoke(ClearPrimaryChannel);
                return;
            }

            ChannelBox previousPrimary;
            lock (selectionLock)
            {
                previousPrimary = primaryChannel;
                primaryChannel = null;
            }

            if (previousPrimary != null)
                previousPrimary.IsPrimary = false;
            PrimaryChannelChanged?.Invoke();
        }
    } // public class SelectedChannelsManager
} // namespace dvmconsole
