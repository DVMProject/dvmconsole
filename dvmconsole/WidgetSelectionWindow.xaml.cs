// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Desktop Dispatch Console
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Desktop Dispatch Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2024 Caleb, K4PHP
*
*/

using System.Windows;

namespace dvmconsole
{
    /// <summary>
    /// Interaction logic for WidgetSelectionWindow.xaml
    /// </summary>
    public partial class WidgetSelectionWindow : Window
    {
        /*
        ** Properties
        */
        
        /// <summary>
        /// Flag indicating whether or not the system status widgets appear.
        /// </summary>
        public bool ShowSystemStatus { get; private set; } = true;
        /// <summary>
        /// Flag indicating whether or not the channel widgets appear.
        /// </summary>
        public bool ShowChannels { get; private set; } = true;
        /// <summary>
        /// Flag indicating whether or not alert tone widgets appear.
        /// </summary>
        public bool ShowAlertTones { get; private set; } = true;

        /*
        ** Methods
        */

        /// <summary>
        /// Initializes a new instance of the <see cref="WidgetSelectionWindow"/> class.
        /// </summary>
        public WidgetSelectionWindow()
        {
            InitializeComponent();
        }

        /** WPF Events */

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            ShowSystemStatus = SystemStatusCheckBox.IsChecked ?? false;
            ShowChannels = ChannelCheckBox.IsChecked ?? false;
            ShowAlertTones = AlertToneCheckBox.IsChecked ?? false;
            DialogResult = true;
            Close();
        }
    } // public partial class WidgetSelectionWindow : Window
} // namespace dvmconsole
