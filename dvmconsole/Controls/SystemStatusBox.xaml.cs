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

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Controls;

namespace dvmconsole.Controls
{
    /// <summary>
    /// 
    /// </summary>
    public partial class SystemStatusBox : UserControl, INotifyPropertyChanged
    {
        private string connectionState = "Disconnected";

        /*
        ** Properties
        */

        /// <summary>
        /// 
        /// </summary>
        public string SystemName { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string AddressPort { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public string ConnectionState
        {
            get => connectionState;
            set
            {
                if (connectionState != value)
                {
                    connectionState = value;
                    NotifyPropertyChanged();
                }
            }
        }

        /*
        ** Events
        */

        /// <summary>
        /// 
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /*
        ** Methods
        */

        /// <summary>
        /// Initializes a new instance of the <see cref="SystemStatusBox"/> class.
        /// </summary>
        public SystemStatusBox()
        {
            InitializeComponent();
            DataContext = this;
        }
        /// <summary>
        /// Initializes a new instance of the <see cref="SystemStatusBox"/> class.
        /// </summary>
        /// <param name="systemName"></param>
        /// <param name="address"></param>
        /// <param name="port"></param>
        public SystemStatusBox(string systemName, string address, int port) : this()
        {
            SystemName = systemName;
            AddressPort = $"Address: {address}:{port}";
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="propertyName"></param>
        protected void NotifyPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    } // public partial class SystemStatusBox : UserControl, INotifyPropertyChanged
} // namespace dvmconsole.Controls
