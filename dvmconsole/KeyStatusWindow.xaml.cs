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

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;

using dvmconsole.Controls;

namespace dvmconsole
{
    /// <summary>
    /// 
    /// </summary>
    public class KeyStatusItem
    {
        /*
        ** Properties
        */

        /// <summary>
        /// 
        /// </summary>
        public string ChannelName { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string AlgId { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string KeyId { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string KeyStatus { get; set; }
    } // public class KeyStatusItem

    /// <summary>
    /// 
    /// </summary>
    public partial class KeyStatusWindow : Window
    {
        private Codeplug Codeplug;
        private MainWindow mainWindow;

        /*
        ** Properties
        */

        /// <summary>
        /// 
        /// </summary>
        public ObservableCollection<KeyStatusItem> KeyStatusItems { get; private set; } = new ObservableCollection<KeyStatusItem>();

        /*
        ** Methods
        */

        /// <summary>
        /// Initializes a new instance of the <see cref="KeyStatusWindow"/> class.
        /// </summary>
        /// <param name="codeplug"></param>
        /// <param name="mainWindow"></param>
        public KeyStatusWindow(Codeplug codeplug, MainWindow mainWindow)
        {
            InitializeComponent();
            this.Codeplug = codeplug;
            this.mainWindow = mainWindow;
            DataContext = this;

            LoadKeyStatus();
        }

        /// <summary>
        /// 
        /// </summary>
        private void LoadKeyStatus()
        {
            Dispatcher.Invoke(() =>
            {
                KeyStatusItems.Clear();

                foreach (var child in mainWindow.ChannelsCanvas.Children)
                {
                    if (child == null)
                    {
                        Trace.WriteLine("A child in ChannelsCanvas.Children is null.");
                        continue;
                    }

                    if (!(child is ChannelBox channelBox))
                    {
                        continue;
                    }

                    Codeplug.System system = Codeplug.GetSystemForChannel(channelBox.ChannelName);
                    if (system == null)
                    {
                        Trace.WriteLine($"System not found for {channelBox.ChannelName}");
                        continue;
                    }

                    Codeplug.Channel cpgChannel = Codeplug.GetChannelByName(channelBox.ChannelName);
                    if (cpgChannel == null)
                    {
                        Trace.WriteLine($"Channel not found for {channelBox.ChannelName}");
                        continue;
                    }

                    if (cpgChannel.GetKeyId() == 0 || cpgChannel.GetAlgoId() == 0)
                        continue;

                    if (channelBox.Crypter == null)
                    {
                        Trace.WriteLine($"Crypter is null for channel {channelBox.ChannelName}");
                        continue;
                    }

                    bool hasKey = channelBox.Crypter.HasKey(cpgChannel.GetKeyId());

                    KeyStatusItems.Add(new KeyStatusItem
                    {
                        ChannelName = channelBox.ChannelName,
                        AlgId = $"0x{cpgChannel.GetAlgoId():X2}",
                        KeyId = $"0x{cpgChannel.GetKeyId():X4}",
                        KeyStatus = hasKey ? "Key Available" : "No Key"
                    });
                }
            });
        }
    } // public partial class KeyStatusWindow : Window
} // namespace dvmconsole
