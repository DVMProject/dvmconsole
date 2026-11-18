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

using System.Globalization;
using System.Windows;

using fnecore.P25;

namespace dvmconsole
{
    /// <summary>
    /// Interaction logic for DigitalPageWindow.xaml.
    /// </summary>
    public partial class DigitalPageWindow : Window
    {
        private List<Codeplug.System> systems = new List<Codeplug.System>();

        /// <summary>
        /// Destination ID.
        /// </summary>
        public string DstId = string.Empty;
        /// <summary>
        /// System.
        /// </summary>
        public Codeplug.System RadioSystem = null;

        /// <summary>
        /// Initializes a new instance of the <see cref="DigitalPageWindow"/> class.
        /// </summary>
        /// <param name="systems"></param>
        public DigitalPageWindow(List<Codeplug.System> systems)
        {
            InitializeComponent();
            this.systems = systems;

            SystemCombo.DisplayMemberPath = "Name";
            SystemCombo.ItemsSource = systems;
            SystemCombo.SelectedIndex = 0;
        }

        /** WPF Events */

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            RadioSystem = SystemCombo.SelectedItem as Codeplug.System;
            DstId = DstIdText.Text.Trim();
            if (!TryValidateRequest(RadioSystem, DstId, out _, out _, out string error))
            {
                MessageBox.Show(error, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
            Close();
        }

        /// <summary>
        /// Validates the selected system and decimal IDs before building a 24-bit P25 command.
        /// </summary>
        internal static bool TryValidateRequest(Codeplug.System system, string destination,
            out uint sourceId, out uint destinationId, out string error)
        {
            sourceId = destinationId = 0;
            error = null;
            if (system == null || string.IsNullOrWhiteSpace(system.Name))
                error = "Select a radio system first.";
            else if (!uint.TryParse(destination?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out destinationId) ||
                     destinationId == 0 || destinationId > P25Defines.WUID_ALL)
                error = "Enter a decimal destination radio ID from 1 to 16777215.";
            else if (!uint.TryParse(system.Rid?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out sourceId) ||
                     sourceId == 0 || sourceId > P25Defines.WUID_ALL)
                error = "The selected system needs a decimal source radio ID from 1 to 16777215.";
            return error == null;
        }
    } // public partial class DigitalPageWindow : Window
} // namespace dvmconsole
