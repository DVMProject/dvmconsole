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
*
*/

using dvmconsole.Controls;

namespace dvmconsole
{
    /// <summary>
    /// 
    /// </summary>
    public class SelectedChannelsManager
    {
        private readonly HashSet<ChannelBox> selectedChannels;

        public IReadOnlyCollection<ChannelBox> GetSelectedChannels() => selectedChannels;

        /*
        ** Events
        */

        /// <summary>
        /// 
        /// </summary>
        public event Action SelectedChannelsChanged;

        /*
        ** Methods
        */

        /// <summary>
        /// Initializes a new instance of the <see cref="SelectedChannelsManager"/> class.
        /// </summary>
        public SelectedChannelsManager()
        {
            selectedChannels = new HashSet<ChannelBox>();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="channel"></param>
        public void AddSelectedChannel(ChannelBox channel)
        {
            if (selectedChannels.Add(channel))
            {
                channel.IsSelected = true;
                SelectedChannelsChanged.Invoke();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="channel"></param>
        public void RemoveSelectedChannel(ChannelBox channel)
        {
            if (selectedChannels.Remove(channel))
            {
                channel.IsSelected = false;
                SelectedChannelsChanged.Invoke();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public void ClearSelections()
        {
            foreach (var channel in selectedChannels)
                channel.IsSelected = false;

            selectedChannels.Clear();
            SelectedChannelsChanged.Invoke();
        }
    } // public class SelectedChannelsManager
} // namespace dvmconsole
