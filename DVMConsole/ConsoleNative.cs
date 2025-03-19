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

using System.Runtime.InteropServices;

namespace DVMConsole
{
    public static class ConsoleNative
    {
        [DllImport("kernel32.dll")]
        private static extern bool AllocConsole();

        public static void ShowConsole()
        {
            AllocConsole();
            Console.WriteLine("Console attached.");
        }
    }
}
