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
*
*/

namespace dvmconsole
{
    /// <summary>
    /// Builds stable per-resource keys for settings and audio state.
    /// </summary>
    public static class ResourceIdentity
    {
        public static string Build(string systemName, string talkgroupId)
        {
            string system = (systemName ?? string.Empty).Trim().ToLowerInvariant();
            string tgid = (talkgroupId ?? string.Empty).Trim();
            return $"{system}|{tgid}";
        }

        public static bool SystemMatches(string left, string right)
        {
            return string.Equals(
                (left ?? string.Empty).Trim(),
                (right ?? string.Empty).Trim(),
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
