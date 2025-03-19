// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - DVMConsole
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / DVM Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2024-2025 Caleb, K4PHP
*
*/

using System.Security.Policy;

namespace DVMConsole
{

    /// <summary>
    /// Codeplug object used project wide
    /// </summary>
    public class Codeplug
    {
        public List<System> Systems { get; set; }
        public List<Zone> Zones { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public class System
        {
            public string Name { get; set; }
            public string Identity { get; set; }
            public string Address { get; set; }
            public string Password { get; set; }
            public string PresharedKey { get; set; }
            public bool Encrypted { get; set; }
            public uint PeerId { get; set; }
            public int Port { get; set; }
            public string Rid { get; set; }
            public string AliasPath { get; set; } = "./alias.yml";
            public List<RadioAlias> RidAlias { get; set; } = null;

            public override string ToString()
            {
                return Name;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public class Zone
        {
            public string Name { get; set; }
            public List<Channel> Channels { get; set; }
        }

        /// <summary>
        /// 
        /// </summary>
        public class Channel
        {
            public string Name { get; set; }
            public string System { get; set; }
            public string Tgid { get; set; }
            public string EncryptionKey { get; set; }
            public string AlgoId { get; set; } = "0x80";
            public string KeyId { get; set; }

            public ushort GetKeyId()
            {
                return Convert.ToUInt16(KeyId, 16);
            }

            public byte GetAlgoId()
            {
                return Convert.ToByte(AlgoId, 16);
            }

            public byte[] GetEncryptionKey()
            {
                if (EncryptionKey == null)
                    return [];

                return EncryptionKey
                    .Split(',')
                    .Select(s => Convert.ToByte(s.Trim(), 16))
                    .ToArray();
            }
        }

        /// <summary>
        /// Helper to return a system by looking up a <see cref="Channel"/>
        /// </summary>
        /// <param name="channel"></param>
        /// <returns></returns>
        public System GetSystemForChannel(Channel channel)
        {
            return Systems.FirstOrDefault(s => s.Name == channel.System);
        }

        /// <summary>
        /// Helper to return a system by looking up a channel name
        /// </summary>
        /// <param name="channelName"></param>
        /// <returns></returns>
        public System GetSystemForChannel(string channelName)
        {
            foreach (var zone in Zones)
            {
                var channel = zone.Channels.FirstOrDefault(c => c.Name == channelName);
                if (channel != null)
                {
                    return Systems.FirstOrDefault(s => s.Name == channel.System);
                }
            }
            return null;
        }

        /// <summary>
        /// Helper to return a <see cref="Channel"/> by channel name
        /// </summary>
        /// <param name="channelName"></param>
        /// <returns></returns>
        public Channel GetChannelByName(string channelName)
        {
            foreach (var zone in Zones)
            {
                var channel = zone.Channels.FirstOrDefault(c => c.Name == channelName);
                if (channel != null)
                {
                    return channel;
                }
            }
            return null;
        }
    }
}