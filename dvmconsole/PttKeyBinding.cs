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

using System.Windows.Forms;
using System.Windows.Media;
using Newtonsoft.Json;

namespace dvmconsole
{
    /// <summary>
    /// A fixed PTT shortcut targets a resource identity, never its display name.
    /// </summary>
    public sealed class PttKeyBinding
    {
        public int Slot { get; set; }
        public Keys Key { get; set; }
        public string SystemName { get; set; }
        public string Tgid { get; set; }
        public Codeplug.ChannelMode Mode { get; set; } = Codeplug.ChannelMode.P25;
        public int Timeslot { get; set; } = 1;

        [JsonIgnore]
        public string TargetKey => $"{ResourceIdentity.Build(SystemName, Tgid)}|{Mode}|{(Mode == Codeplug.ChannelMode.DMR ? Timeslot : 0)}";

        public bool Matches(Codeplug.Channel channel)
        {
            return channel != null && ResourceIdentity.SystemMatches(SystemName, channel.System) &&
                Tgid == channel.Tgid && Mode == channel.GetChannelMode() &&
                (Mode != Codeplug.ChannelMode.DMR || Timeslot == Math.Max(1, channel.Slot));
        }

        public PttKeyBinding Copy() => (PttKeyBinding)MemberwiseClone();
    }

    /// <summary>
    /// Shared slot colors for the shortcut editor and resource borders.
    /// </summary>
    internal static class PttBindingPalette
    {
        internal static readonly string[] Names = { "Green", "Blue", "Amber", "Violet", "Cyan" };
        private static readonly string[] Colors = { "#008000", "#2979FF", "#FFB300", "#AB47BC", "#00BCD4" };
        internal static int SlotCount => Colors.Length;

        internal static Brush BrushFor(int slot)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(Colors[slot - 1]));
            brush.Freeze();
            return brush;
        }

        internal static bool IsValidKey(Keys key) => (int)key >= 1 && (int)key <= 254;

        /// <summary>
        /// Fail closed for invalid or conflicting imported/profile entries.
        /// </summary>
        internal static List<PttKeyBinding> Normalize(IEnumerable<PttKeyBinding> bindings, Keys primaryKey)
        {
            var result = new List<PttKeyBinding>();
            var slots = new HashSet<int>();
            var keys = new HashSet<Keys> { primaryKey };
            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PttKeyBinding binding in bindings ?? Enumerable.Empty<PttKeyBinding>())
            {
                if (binding == null || binding.Slot < 2 || binding.Slot > SlotCount ||
                    !IsValidKey(binding.Key) || string.IsNullOrWhiteSpace(binding.SystemName) ||
                    string.IsNullOrWhiteSpace(binding.Tgid) || !Enum.IsDefined(binding.Mode) ||
                    (binding.Mode == Codeplug.ChannelMode.DMR && binding.Timeslot is not (1 or 2)) ||
                    slots.Contains(binding.Slot) || keys.Contains(binding.Key) || targets.Contains(binding.TargetKey))
                    continue;
                slots.Add(binding.Slot);
                keys.Add(binding.Key);
                targets.Add(binding.TargetKey);
                result.Add(binding.Copy());
            }
            return result;
        }
    }
}
