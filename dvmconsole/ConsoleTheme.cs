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
*/

using System.Windows;

namespace dvmconsole
{
    internal static class ConsoleTheme
    {
        public static void Apply(bool darkMode)
        {
            const string prefix = "/dvmconsole;component/Themes/Console";
            string source = prefix + (darkMode ? "Dark.xaml" : "Light.xaml");
            var dictionaries = Application.Current.Resources.MergedDictionaries;
            for (int i = 0; i < dictionaries.Count; i++)
            {
                string current = dictionaries[i].Source?.OriginalString;
                if (current != prefix + "Light.xaml" && current != prefix + "Dark.xaml")
                    continue;

                if (current != source)
                    dictionaries[i] = new ResourceDictionary { Source = new Uri(source, UriKind.Relative) };
                return;
            }

            dictionaries.Add(new ResourceDictionary { Source = new Uri(source, UriKind.Relative) });
        }
    }
}
