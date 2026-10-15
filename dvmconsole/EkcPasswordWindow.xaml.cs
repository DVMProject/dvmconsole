// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Desktop Dispatch Console
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Desktop Dispatch Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2026 DVMProject Authors
*   Copyright (C) 2026 C. Lovell, Dev_Ranger
*
*/

using System.IO;
using System.Windows;

namespace dvmconsole
{
    public partial class EkcPasswordWindow : Window
    {
        internal string Password => ContainerPassword.Password;

        internal EkcPasswordWindow(string path)
        {
            InitializeComponent();
            ContainerName.Text = Path.GetFileName(path);
            Loaded += (_, _) => ContainerPassword.Focus();
        }

        private void Unlock_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        internal void ClearPassword() => ContainerPassword.Clear();
    }
}

