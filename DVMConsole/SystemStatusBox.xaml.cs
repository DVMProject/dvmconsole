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

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Controls;

namespace DVMConsole.Controls
{
    public partial class SystemStatusBox : UserControl, INotifyPropertyChanged
    {
        private string _connectionState = "Disconnected";

        public string SystemName { get; set; }
        public string AddressPort { get; set; }

        public string ConnectionState
        {
            get => _connectionState;
            set
            {
                if (_connectionState != value)
                {
                    _connectionState = value;
                    NotifyPropertyChanged();
                }
            }
        }

        public SystemStatusBox()
        {
            InitializeComponent();
            DataContext = this;
        }

        public SystemStatusBox(string systemName, string address, int port) : this()
        {
            SystemName = systemName;
            AddressPort = $"Address: {address}:{port}";
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void NotifyPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
