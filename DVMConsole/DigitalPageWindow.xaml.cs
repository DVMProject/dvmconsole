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

using System.Windows;

namespace DVMConsole
{
    /// <summary>
    /// Interaction logic for DigitalPageWindow.xaml
    /// </summary>
    public partial class DigitalPageWindow : Window
    {
        public List<Codeplug.System> systems = new List<Codeplug.System>();

        public string DstId = string.Empty;
        public Codeplug.System RadioSystem = null;

        public DigitalPageWindow(List<Codeplug.System> systems)
        {
            InitializeComponent();
            this.systems = systems;

            SystemCombo.DisplayMemberPath = "Name";
            SystemCombo.ItemsSource = systems;
            SystemCombo.SelectedIndex = 0;
        }

        private void SendPageButton_Click(object sender, RoutedEventArgs e)
        {
            RadioSystem = SystemCombo.SelectedItem as Codeplug.System;
            DstId = DstIdText.Text;
            DialogResult = true;
            Close();
        }
    }
}
