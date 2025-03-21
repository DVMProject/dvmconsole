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

using System.Windows;

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
        private void SendPageButton_Click(object sender, RoutedEventArgs e)
        {
            RadioSystem = SystemCombo.SelectedItem as Codeplug.System;
            DstId = DstIdText.Text;
            DialogResult = true;
            Close();
        }
    } // public partial class DigitalPageWindow : Window
} // namespace dvmconsole
