// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Desktop Dispatch Console
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Desktop Dispatch Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2024-2025 Caleb, K4PHP
*   Copyright (C) 2025 Bryan Biedenkapp, N2PLL
*
*/

using fnecore.P25;

namespace dvmconsole
{
    /// <summary>
    /// Codeplug object used to configure the console.
    /// </summary>
    public class Codeplug
    {
        /*
        ** Properties
        */

        /// <summary>
        /// 
        /// </summary>
        public List<System> Systems { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public List<Zone> Zones { get; set; }

        /*
        ** Classes
        */

        /// <summary>
        /// 
        /// </summary>
        public class System
        {
            /*
            ** Properties
            */
            
            /// <summary>
            /// 
            /// </summary>
            public string Name { get; set; }
            /// <summary>
            /// 
            /// </summary>
            public string Identity { get; set; }
            /// <summary>
            /// 
            /// </summary>
            public string Address { get; set; }
            /// <summary>
            /// 
            /// </summary>
            public string Password { get; set; }
            /// <summary>
            /// 
            /// </summary>
            public string PresharedKey { get; set; }
            /// <summary>
            /// 
            /// </summary>
            public bool Encrypted { get; set; }
            /// <summary>
            /// 
            /// </summary>
            public uint PeerId { get; set; }
            /// <summary>
            /// 
            /// </summary>
            public int Port { get; set; }
            /// <summary>
            /// 
            /// </summary>
            public string Rid { get; set; }
            /// <summary>
            /// 
            /// </summary>
            public string AliasPath { get; set; } = "./alias.yml";
            /// <summary>
            /// 
            /// </summary>
            public List<RadioAlias> RidAlias { get; set; } = null;

            /*
            ** Methods
            */

            /// <summary>
            /// 
            /// </summary>
            /// <returns></returns>
            public override string ToString()
            {
                return Name;
            }
        } // public class System

        /// <summary>
        /// 
        /// </summary>
        public class Zone
        {
            /*
            ** Properties
            */

            /// <summary>
            /// 
            /// </summary>
            public string Name { get; set; }
            /// <summary>
            /// 
            /// </summary>
            public List<Channel> Channels { get; set; }
        } // public class Zone

        /// <summary>
        /// 
        /// </summary>
        public class Channel
        {
            /*
            ** Properties
            */

            /// <summary>
            /// 
            /// </summary>
            public string Name { get; set; }
            /// <summary>
            /// 
            /// </summary>
            public string System { get; set; }
            /// <summary>
            /// 
            /// </summary>
            public string Tgid { get; set; }
            /// <summary>
            /// 
            /// </summary>
            public string EncryptionKey { get; set; }
            /// <summary>
            /// 
            /// </summary>
            public string Algo { get; set; } = "none";
            /// <summary>
            /// 
            /// </summary>
            public string KeyId { get; set; }
            /// <summary>
            /// 
            /// </summary>
            public string Mode { get; set; } = "p25";

            /*
            ** Methods
            */

            /// <summary>
            /// 
            /// </summary>
            /// <returns></returns>
            public ushort GetKeyId()
            {
                return Convert.ToUInt16(KeyId, 16);
            }

            /// <summary>
            /// 
            /// </summary>
            /// <returns></returns>
            public byte GetAlgoId()
            {
                switch (Algo.ToLowerInvariant())
                {
                    case "aes":
                        return P25Defines.P25_ALGO_AES;
                    case "arc4":
                        return P25Defines.P25_ALGO_ARC4;
                    default:
                        return P25Defines.P25_ALGO_UNENCRYPT;
                }
            }

            /// <summary>
            /// 
            /// </summary>
            /// <returns></returns>
            public byte[] GetEncryptionKey()
            {
                if (EncryptionKey == null)
                    return [];

                return EncryptionKey.Split(',').Select(s => Convert.ToByte(s.Trim(), 16)).ToArray();
            }

            /// <summary>
            /// 
            /// </summary>
            /// <returns></returns>
            public ChannelMode GetChannelMode()
            {
                if (Enum.TryParse(typeof(ChannelMode), Mode, ignoreCase: true, out var result))
                {
                    return (ChannelMode)result;
                }

                return ChannelMode.P25;
            }
        } // public class Channel

        /// <summary>
        /// 
        /// </summary>
        public enum ChannelMode
        {
            DMR = 0,
            NXDN = 1,
            P25 = 2
        } // public enum ChannelMode

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
                    return Systems.FirstOrDefault(s => s.Name == channel.System);
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
                    return channel;
            }

            return null;
        }
    } //public class Codeplug
} // namespace dvmconsole
