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
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace dvmconsole.Controls
{
    /// <summary>
    /// 
    /// </summary>
    public partial class AlertTone : UserControl
    {
        public static readonly DependencyProperty AlertFileNameProperty =
            DependencyProperty.Register("AlertFileName", typeof(string), typeof(AlertTone), new PropertyMetadata(string.Empty));

        /*
        ** Properties
        */

        /// <summary>
        /// 
        /// </summary>
        public string AlertFileName
        {
            get => (string)GetValue(AlertFileNameProperty);
            set => SetValue(AlertFileNameProperty, value);
        }

        /// <summary>
        /// 
        /// </summary>
        public string AlertFilePath { get; set; }

        /*
        ** Events
        */

        public event Action<AlertTone> OnAlertTone;

        /*
        ** Methods
        */

        /// <summary>
        /// Initializes a new instance of the <see cref="AlertTone"/> class.
        /// </summary>
        /// <param name="alertFilePath"></param>
        public AlertTone(string alertFilePath)
        {
            InitializeComponent();
            AlertFilePath = alertFilePath;
            AlertFileName = System.IO.Path.GetFileNameWithoutExtension(alertFilePath);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void PlayAlert_Click(object sender, RoutedEventArgs e)
        {
            OnAlertTone.Invoke(this);
        }
    } // public partial class AlertTone : UserControl
} // namespace dvmconsole.Controls
