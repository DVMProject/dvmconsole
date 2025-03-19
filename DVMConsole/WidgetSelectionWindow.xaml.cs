// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - DVMConsole
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / DVM Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2024 Caleb, K4PHP
*
*/

using System.Windows;

namespace DVMConsole
{
    public partial class WidgetSelectionWindow : Window
    {
        public bool ShowSystemStatus { get; private set; } = true;
        public bool ShowChannels { get; private set; } = true;
        public bool ShowAlertTones { get; private set; } = true;

        public WidgetSelectionWindow()
        {
            InitializeComponent();
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            ShowSystemStatus = SystemStatusCheckBox.IsChecked ?? false;
            ShowChannels = ChannelCheckBox.IsChecked ?? false;
            ShowAlertTones = AlertToneCheckBox.IsChecked ?? false;
            DialogResult = true;
            Close();
        }
    }
}
