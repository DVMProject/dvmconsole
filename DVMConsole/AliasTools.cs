// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Desktop Dispatch Console
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Desktop Dispatch Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2025 Caleb, K4PHP
*
*/

using System.IO;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace dvmconsole
{
    /// <summary>
    /// 
    /// </summary>
    public class RadioAlias
    {
        /// <summary>
        /// 
        /// </summary>
        public string Alias { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int Rid { get; set; }
    } //public class RadioAlias

    /// <summary>
    /// 
    /// </summary>
    public static class AliasTools
    {
        /*
        ** Methods
        */

        /// <summary>
        /// 
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        /// <exception cref="FileNotFoundException"></exception>
        public static List<RadioAlias> LoadAliases(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Alias file not found.", filePath);

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            var yamlText = File.ReadAllText(filePath);
            return deserializer.Deserialize<List<RadioAlias>>(yamlText);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="aliases"></param>
        /// <param name="rid"></param>
        /// <returns></returns>
        public static string GetAliasByRid(List<RadioAlias> aliases, int rid)
        {
            if (aliases == null || aliases.Count == 0)
                return string.Empty;

            var match = aliases.FirstOrDefault(a => a.Rid == rid);
            return match?.Alias ?? string.Empty;
        }
    } //public static class AliasTools
} // namespace DVMConsole
