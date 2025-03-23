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

using fnecore.Utility;
using NAudio.Wave;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;

namespace dvmconsole
{
    /// <summary>
    /// Encapsulates a Windows Presentation Foundation application.
    /// </summary>
    public partial class App : Application
    {
        public static string USER_PROFILE_PATH_OVERRIDE = string.Empty;

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
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
                return;
            }
        }

        /// <summary>
        /// Internal helper to prints the program usage.
        /// </summary>
        private static void Usage(OptionSet p)
        {
            string messageBoxText = "[-h | --help][--userprofile <path for UserProfile.yaml>]\r\nOptions:\r\n";

            using (MemoryStream ms = new MemoryStream())
            {
                using (TextWriter writer = new StreamWriter(ms))
                    p.WriteOptionDescriptions(writer);

                messageBoxText += Encoding.UTF8.GetString(ms.ToArray());
            }

            MessageBox.Show(messageBoxText, "Digital Voice Modem - Desktop Dispatch Console",
                MessageBoxButton.OK, MessageBoxImage.Information);

            Application.Current.Shutdown();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            bool showHelp = false;
            string configFile = string.Empty;

            // command line parameters
            OptionSet options = new OptionSet()
            {
                { "h|help", "show this message and exit", v => showHelp = v != null },
                { "userprofile=", "sets the path to the UserProfile.yaml", v => USER_PROFILE_PATH_OVERRIDE = v },
            };

            // attempt to parse the commandline
            try
            {
                options.Parse(e.Args);
            }
            catch (OptionException)
            {
                /* ignore */
            }

            // show help?
            if (showHelp)
                Usage(options);
        }
    } // public partial class App : Application
} // namespace dvmconsole
