// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - DVMConsole
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / DVM Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2024 Caleb, K4PHP
*
*/

using DVMConsole.Controls;

namespace DVMConsole
{
    public class SelectedChannelsManager
    {
        private readonly HashSet<ChannelBox> _selectedChannels;

        public event Action SelectedChannelsChanged;

        public SelectedChannelsManager()
        {
            _selectedChannels = new HashSet<ChannelBox>();
        }

        public void AddSelectedChannel(ChannelBox channel)
        {
            if (_selectedChannels.Add(channel))
            {
                channel.IsSelected = true;
                SelectedChannelsChanged.Invoke();
            }
        }

        public void RemoveSelectedChannel(ChannelBox channel)
        {
            if (_selectedChannels.Remove(channel))
            {
                channel.IsSelected = false;
                SelectedChannelsChanged.Invoke();
            }
        }

        public void ClearSelections()
        {
            foreach (var channel in _selectedChannels)
            {
                channel.IsSelected = false;
            }
            _selectedChannels.Clear();
            SelectedChannelsChanged.Invoke();
        }

        public IReadOnlyCollection<ChannelBox> GetSelectedChannels() => _selectedChannels;
    }
}
