// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - DVMConsole
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / DVM Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2025 Caleb, K4PHP
*
*/

using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization;
using System.Diagnostics;

namespace DVMConsole
{
    public static class AliasTools
    {
        public static List<RadioAlias> LoadAliases(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("Alias file not found.", filePath);
            }

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            var yamlText = File.ReadAllText(filePath);
            return deserializer.Deserialize<List<RadioAlias>>(yamlText);
        }

        public static string GetAliasByRid(List<RadioAlias> aliases, int rid)
        {
            if (aliases == null || aliases.Count == 0)
                return string.Empty;

            var match = aliases.FirstOrDefault(a => a.Rid == rid);
            return match?.Alias ?? string.Empty;
        }
    }

    public class RadioAlias
    {
        public string Alias { get; set; }
        public int Rid { get; set; }
    }
}
