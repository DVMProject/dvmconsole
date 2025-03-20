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

using NAudio.Wave;

namespace dvmconsole
{
    /// <summary>
    /// Interaction logic for AudioSettingsWindow.xaml.
    /// </summary>
    public partial class AudioSettingsWindow : Window
    {
        private readonly SettingsManager settingsManager;
        private readonly AudioManager audioManager;
        private readonly List<Codeplug.Channel> channels;
        private readonly Dictionary<string, int> selectedOutputDevices = new Dictionary<string, int>();

        /*
        ** Methods
        */

        /// <summary>
        /// Initializes a new instance of the <see cref="AudioSettingsWindow"/> class.
        /// </summary>
        /// <param name="settingsManager"></param>
        /// <param name="audioManager"></param>
        /// <param name="channels"></param>
        public AudioSettingsWindow(SettingsManager settingsManager, AudioManager audioManager, List<Codeplug.Channel> channels)
        {
            InitializeComponent();
            this.settingsManager = settingsManager;
            this.audioManager = audioManager;
            this.channels = channels;

            LoadAudioDevices();
            LoadChannelOutputSettings();
        }

        /// <summary>
        /// 
        /// </summary>
        private void LoadAudioDevices()
        {
            List<string> inputDevices = GetAudioInputDevices();
            List<string> outputDevices = GetAudioOutputDevices();

            InputDeviceComboBox.ItemsSource = inputDevices;
            InputDeviceComboBox.SelectedIndex = settingsManager.ChannelOutputDevices.ContainsKey("GLOBAL_INPUT")
                ? settingsManager.ChannelOutputDevices["GLOBAL_INPUT"] : 0;
        }

        /// <summary>
        /// 
        /// </summary>
        private void LoadChannelOutputSettings()
        {
            List<string> outputDevices = GetAudioOutputDevices();

            foreach (var channel in channels)
            {
                TextBlock channelLabel = new TextBlock
                {
                    Text = channel.Name,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 5, 0, 0)
                };

                ComboBox outputDeviceComboBox = new ComboBox
                {
                    Width = 350,
                    ItemsSource = outputDevices,
                    SelectedIndex = settingsManager.ChannelOutputDevices.ContainsKey(channel.Tgid)
                        ? settingsManager.ChannelOutputDevices[channel.Tgid]
                        : 0
                };

                outputDeviceComboBox.SelectionChanged += (s, e) =>
                {
                    int selectedIndex = outputDeviceComboBox.SelectedIndex;
                    selectedOutputDevices[channel.Tgid] = selectedIndex;
                };

                ChannelOutputStackPanel.Children.Add(channelLabel);
                ChannelOutputStackPanel.Children.Add(outputDeviceComboBox);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private List<string> GetAudioInputDevices()
        {
            List<string> inputDevices = new List<string>();

            for (int i = 0; i < WaveIn.DeviceCount; i++)
            {
                var deviceInfo = WaveIn.GetCapabilities(i);
                inputDevices.Add(deviceInfo.ProductName);
            }

            return inputDevices;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private List<string> GetAudioOutputDevices()
        {
            List<string> outputDevices = new List<string>();

            for (int i = 0; i < WaveOut.DeviceCount; i++)
            {
                var deviceInfo = WaveOut.GetCapabilities(i);
                outputDevices.Add(deviceInfo.ProductName);
            }

            return outputDevices;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            int selectedInputIndex = InputDeviceComboBox.SelectedIndex;
            settingsManager.UpdateChannelOutputDevice("GLOBAL_INPUT", selectedInputIndex);

            foreach (var entry in selectedOutputDevices)
            {
                settingsManager.UpdateChannelOutputDevice(entry.Key, entry.Value);
                audioManager.SetTalkgroupOutputDevice(entry.Key, entry.Value);
            }

            DialogResult = true;
            Close();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    } // public partial class AudioSettingsWindow : Window
} // namespace dvmconsole
