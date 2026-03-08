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
using System.Reflection;
using System.Windows;
using System.Diagnostics;
using System.Windows.Navigation;

namespace dvmconsole
{
    /// <summary>
    /// Interaction logic for AboutWindow.xaml
    /// </summary>
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            LoadVersionInfo();
        }

        private void LoadVersionInfo()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();

            Version asmVersion = assembly.GetName().Version;
            string informationalVersion = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion ?? string.Empty;

            string releaseVersion = "Unknown";
            if (asmVersion != null)
            {
                releaseVersion = $"R{asmVersion.Major:D2}A{asmVersion.Minor:D2}";
            }

            string shortHash = "unknown";

            int openParen = informationalVersion.IndexOf('(');
            int closeParen = informationalVersion.IndexOf(')');

            if (openParen >= 0 && closeParen > openParen)
            {
                string fullHash = informationalVersion.Substring(openParen + 1, closeParen - openParen - 1).Trim();
                if (!string.IsNullOrWhiteSpace(fullHash))
                {
                    shortHash = fullHash.Length > 8 ? fullHash.Substring(0, 8) : fullHash;
                }
            }
            else
            {
                string[] parts = informationalVersion.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 1)
                {
                    string possibleHash = parts[^1].Trim();
                    if (!string.IsNullOrWhiteSpace(possibleHash) && possibleHash != releaseVersion)
                    {
                        shortHash = possibleHash.Length > 8 ? possibleHash.Substring(0, 8) : possibleHash;
                    }
                }
            }

            string buildTime = "Unknown";
            try
            {
                buildTime = System.IO.File.GetLastWriteTime(assembly.Location).ToString("MM/dd/yyyy HH:mm:ss");
            }
            catch
            {
                // leave as Unknown
            }

            txtVersion.Text = $"{releaseVersion} ({shortHash})\nBuilt: {buildTime}";
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
        private void Repository_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/DVMProject/dvmconsole",
                UseShellExecute = true
            });
        }
        private void LicenseLink_Click(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
        }
    }
}