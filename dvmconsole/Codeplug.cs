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
*   Copyright (C) 2025 Steven Jennison, KD8RHO
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
        /// <summary>
        /// Enumeration of channel modes.
        /// </summary>
        public enum ChannelMode
        {
            DMR = 0,
            NXDN = 1,
            P25 = 2
        } // public enum ChannelMode

        /*
        ** Properties
        */

        /// <summary>
        /// The location of the YAML keyfile
        /// </summary>
        public string KeyFile { get; set; } = null;
        /// <summary>
        /// List of systems.
        /// </summary>
        public List<System> Systems { get; set; }
        /// <summary>
        /// List of zones.
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
            /// Textual name for system.
            /// </summary>
            public string Name { get; set; }

            /// <summary>
            /// Textual identity string reported to the FNE core.
            /// </summary>
            public string Identity { get; set; }
            /// <summary>
            /// IP/hostname of the FNE.
            /// </summary>
            public string Address { get; set; }
            /// <summary>
            /// Port number for the FNE connection.
            /// </summary>
            public int Port { get; set; }
            /// <summary>
            /// Authentication password.
            /// </summary>
            public string Password { get; set; }

            /// <summary>
            /// Preshared Encryption key.
            /// </summary>
            public string PresharedKey { get; set; }
            /// <summary>
            /// Flag indicating whether or not the connection to the FNE is encrypted.
            /// </summary>
            public bool Encrypted { get; set; }

            /// <summary>
            /// Unique Peer ID.
            /// </summary>
            public uint PeerId { get; set; }

            /// <summary>
            /// Unique Radio ID.
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
        /// Data structure representation of the data for a zone.
        /// </summary>
        public class Zone
        {
            /*
            ** Properties
            */

            /// <summary>
            /// Textual name for zone.
            /// </summary>
            public string Name { get; set; }
            /// <summary>
            /// List of channels in the zone.
            /// </summary>
            public List<Channel> Channels { get; set; }
        } // public class Zone

        /// <summary>
        /// Data structure representation of the data for a channel.
        /// </summary>
        public class Channel
        {
            /*
            ** Properties
            */

            /// <summary>
            /// Textual name for channel.
            /// </summary>
            public string Name { get; set; }
            /// <summary>
            /// Textual name for system channel is a member of.
            /// </summary>
            public string System { get; set; }
            /// <summary>
            /// Talkgroup ID.
            /// </summary>
            public string Tgid { get; set; }
            /// <summary>
            /// DMR Timeslot.
            /// </summary>
            public int Slot { get; set; }
            /// <summary>
            /// Textual algorithm name.
            /// </summary>
            public string Algo { get; set; } = "none";
            /// <summary>
            /// Encryption Key ID.
            /// </summary>
            public string KeyId { get; set; }
            /// <summary>
            /// Digital Voice Mode.
            /// </summary>
            public string Mode { get; set; } = "p25";

            /*
            ** Methods
            */

            /// <summary>
            /// Helper to return the key ID as a numeric value from a string.
            /// </summary>
            /// <returns></returns>
            public ushort GetKeyId()
            {
                return Convert.ToUInt16(KeyId, 16);
            }

            /// <summary>
            /// Helper to return the algorithm ID from the configured algorithm type string.
            /// </summary>
            /// <returns></returns>
            public byte GetAlgoId()
            {
                switch (Algo.ToLowerInvariant())
                {
                    case "aes":
                        return P25Defines.P25_ALGO_AES;
                    case "des":
                        return P25Defines.P25_ALGO_DES;
                    case "arc4":
                        return P25Defines.P25_ALGO_ARC4;
                    default:
                        return P25Defines.P25_ALGO_UNENCRYPT;
                }
            }

            /// <summary>
            /// Helper to return the channel mode.
            /// </summary>
            /// <returns></returns>
            public ChannelMode GetChannelMode()
            {
                if (Enum.TryParse(typeof(ChannelMode), Mode, ignoreCase: true, out var result))
                    return (ChannelMode)result;

                return ChannelMode.P25;
            }
        } // public class Channel

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
            foreach (Zone zone in Zones)
            {
                Channel channel = zone.Channels.FirstOrDefault(c => c.Name == channelName);
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
            foreach (Zone zone in Zones)
            {
                Channel channel = zone.Channels.FirstOrDefault(c => c.Name == channelName);
                if (channel != null)
                    return channel;
            }

            return null;
        }
    } //public class Codeplug
} // namespace dvmconsole
