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
    /// Interaction logic for QuickCallPage.xaml
    /// </summary>
    public partial class QuickCallPage : Window
    {
        public string ToneA;
        public string ToneB;

        /*
        ** Methods
        */

        /// <summary>
        /// Initializes a new instance of the <see cref="QuickCallPage"/> class.
        /// </summary>
        public QuickCallPage()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            ToneA = ToneAText.Text;
            ToneB = ToneBText.Text;

            DialogResult = true;
            Close();
        }
    } // public partial class QuickCallPage : Window
} // namespace dvmconsole
