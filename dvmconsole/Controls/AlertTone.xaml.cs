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
*   Copyright (C) 2026 C. Lovell, Dev_Ranger
*   
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
        public string AlertToneId { get; set; }

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
        /// <param name="alertToneId"></param>
        /// <param name="alertFilePath"></param>
        /// <param name="displayName"></param>
        public AlertTone(string alertToneId, string alertFilePath, string displayName = null)
        {
            InitializeComponent();
            AlertToneId = alertToneId;
            AlertFilePath = alertFilePath;
            AlertFileName = string.IsNullOrWhiteSpace(displayName)
                ? System.IO.Path.GetFileNameWithoutExtension(alertFilePath)
                : displayName;
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
