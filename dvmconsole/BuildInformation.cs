// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Desktop Dispatch Console
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Desktop Dispatch Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Project contributors: DVMProject authors
*
*/

using System.Globalization;
using System.Reflection;

namespace dvmconsole
{
    /// <summary>
    /// Reads build metadata embedded by MSBuild, independent of file copy timestamps.
    /// </summary>
    internal static class BuildInformation
    {
        /// <summary>
        /// Returns the assembly's build time in UTC, or Unknown for older builds.
        /// </summary>
        internal static string GetTimestampText(Assembly assembly)
        {
            string timestamp = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute => attribute.Key == "BuildTimestampUtc")?.Value;
            return DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed)
                ? parsed.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)
                : "Unknown";
        }
    }
}
