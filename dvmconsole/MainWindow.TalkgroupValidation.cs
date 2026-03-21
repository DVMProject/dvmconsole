// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Desktop Dispatch Console
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Desktop Dispatch Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2026 C. Lovell, K7CBL
*/
using System.Windows;
using System.Linq;
using dvmconsole.Controls;

namespace dvmconsole
{
    public partial class MainWindow
    {
        private const string TG_UNAVAILABLE_MESSAGE = "Target TG unavailable on FNE";

        private bool CanStartChannelPtt(ChannelBox channel)
        {
            if (channel == null || channel.SystemName == PLAYBACKSYS || channel.ChannelName == PLAYBACKCHNAME || channel.DstId == PLAYBACKTG)
                return true;

            Codeplug.Channel cpgChannel = Codeplug?.GetChannelByName(channel.ChannelName);
            if (cpgChannel == null)
                return false;

            Codeplug.System system = Codeplug?.Systems?.FirstOrDefault(s => s.Name == cpgChannel.System);
            if (system == null)
                return false;

            PeerSystem fne = fneSystemManager.GetFneSystem(system.Name);
            if (fne == null)
                return false;

            return ValidateTalkgroupAvailability(fne, cpgChannel);
        }

        private bool ValidateTalkgroupAvailability(PeerSystem fne, Codeplug.Channel cpgChannel)
        {
            if (fne == null || cpgChannel == null)
                return false;

            if (fne.IsTalkgroupAvailable(cpgChannel))
                return true;

            Log.WriteWarning(
                $"TG validation blocked on FNE '{fne.SystemName}' for channel '{cpgChannel.Name}' " +
                $"(mode={cpgChannel.Mode}, tgid={cpgChannel.Tgid}, slot={cpgChannel.Slot}): " +
                fne.DescribeTalkgroupAvailability(cpgChannel));

            Dispatcher.Invoke(() =>
            {
                MessageBox.Show(TG_UNAVAILABLE_MESSAGE, "Unavailable Talkgroup", MessageBoxButton.OK, MessageBoxImage.Warning);
            });
            return false;
        }

        private bool ValidateTalkgroupAvailability(PeerSystem fne, Codeplug.Channel cpgChannel, ChannelBox channel, Action<ChannelBox> rollback)
        {
            if (ValidateTalkgroupAvailability(fne, cpgChannel))
                return true;

            if (channel != null && rollback != null)
                Dispatcher.Invoke(() => rollback(channel));

            return false;
        }
    }
}
