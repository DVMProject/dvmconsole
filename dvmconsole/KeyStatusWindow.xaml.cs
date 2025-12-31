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
*   Copyright (C) 2025 Lorenzo L Romero, K2LLR
*
*/

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;

using dvmconsole.Controls;

namespace dvmconsole
{
    /// <summary>
    /// Data structure representing a key status item.
    /// </summary>
    public class KeyStatusItem
    {
        /*
        ** Properties
        */

        /// <summary>
        /// Textual name of channel key is for.
        /// </summary>
        public string ChannelName { get; set; }
        /// <summary>
        /// Algorithm ID.
        /// </summary>
        public string AlgId { get; set; }
        /// <summary>
        /// Key ID.
        /// </summary>
        public string KeyId { get; set; }
        /// <summary>
        /// Key status.
        /// </summary>
        public string KeyStatus { get; set; }
    } // public class KeyStatusItem

    /// <summary>
    /// Interaction logic for KeyStatusWindow.xaml.
    /// </summary>
    public partial class KeyStatusWindow : Window
    {
        private Codeplug Codeplug;
        private MainWindow mainWindow;

        /*
        ** Properties
        */

        /// <summary>
        ///  Collection of key status entries.
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

                // Iterate through all canvases (all tabs plus the original) so we see
                // keys for channels on every tab, not just the original canvas.
                foreach (var canvas in mainWindow.GetAllCanvases())
                {
                    foreach (var child in canvas.Children)
                    {
                        if (child == null)
                        {
                            Log.WriteLine("A child in canvas.Children is null.");
                            continue;
                        }

                        if (!(child is ChannelBox channelBox))
                        {
                            continue;
                        }

                        Codeplug.System system = Codeplug.GetSystemForChannel(channelBox.ChannelName);
                        if (system == null)
                        {
                            Log.WriteLine($"System not found for {channelBox.ChannelName}");
                            continue;
                        }

                        Codeplug.Channel cpgChannel = Codeplug.GetChannelByName(channelBox.ChannelName);
                        if (cpgChannel == null)
                        {
                            Log.WriteLine($"Channel not found for {channelBox.ChannelName}");
                            continue;
                        }

                        if (cpgChannel.GetKeyId() == 0 || cpgChannel.GetAlgoId() == 0)
                            continue;

                        if (channelBox.Crypter == null)
                        {
                            Log.WriteLine($"Crypter is null for channel {channelBox.ChannelName}");
                            continue;
                        }

                        bool hasKey = channelBox.Crypter.HasKey();

                        KeyStatusItems.Add(new KeyStatusItem
                        {
                            ChannelName = channelBox.ChannelName,
                            AlgId = $"0x{cpgChannel.GetAlgoId():X2}",
                            KeyId = $"0x{cpgChannel.GetKeyId():X4}",
                            KeyStatus = hasKey ? "Key Available" : "No Key"
                        });
                    }
                }
            });
        }
    } // public partial class KeyStatusWindow : Window
} // namespace dvmconsole
