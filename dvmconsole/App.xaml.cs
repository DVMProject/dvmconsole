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
*   Copyright (C) 2025 Bryan Biedenkapp, N2PLL
*
*/

using System.IO;
using System.Reflection;
using System.Windows;
using MessageBox = System.Windows.Forms.MessageBox;
using MessageBoxButtons = System.Windows.Forms.MessageBoxButtons;
using MessageBoxIcon = System.Windows.Forms.MessageBoxIcon;

namespace dvmconsole
{
    /// <summary>
    /// 
    /// </summary>
    public partial class App : Application
    {
        /*
        ** Methods
        */

        /// <summary>
        /// Initializes a new instance of the <see cref="App"/> class.
        /// </summary>
        public App()
        {
            string codeBase = Assembly.GetExecutingAssembly().CodeBase;
            UriBuilder uri = new UriBuilder(codeBase);
            string path = Uri.UnescapeDataString(uri.Path);

            if (!File.Exists(Path.Combine(new string[] { Path.GetDirectoryName(path), "libvocoder.DLL" })))
            {
                MessageBox.Show("libvocoder is missing or not found! The library is required for operation of the console, please see: https://github.com/DVMProject/dvmvocoder.", "Digital Voice Modem - Desktop Dispatch Console",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Current.Shutdown();
                return;
            }
        }
    } // public partial class App : Application
} // namespace dvmconsole
