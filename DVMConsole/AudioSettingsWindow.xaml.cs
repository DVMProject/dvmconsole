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

using System.Windows;
using System.Collections.Generic;
using System.Linq;
using NAudio.Wave;
using System.Windows.Controls;

namespace DVMConsole
{
    public partial class AudioSettingsWindow : Window
    {
        private readonly SettingsManager _settingsManager;
        private readonly AudioManager _audioManager;
        private readonly List<Codeplug.Channel> _channels;
        private readonly Dictionary<string, int> _selectedOutputDevices = new Dictionary<string, int>();

        public AudioSettingsWindow(SettingsManager settingsManager, AudioManager audioManager, List<Codeplug.Channel> channels)
        {
            InitializeComponent();
            _settingsManager = settingsManager;
            _audioManager = audioManager;
            _channels = channels;

            LoadAudioDevices();
            LoadChannelOutputSettings();
        }

        private void LoadAudioDevices()
        {
            List<string> inputDevices = GetAudioInputDevices();
            List<string> outputDevices = GetAudioOutputDevices();

            InputDeviceComboBox.ItemsSource = inputDevices;
            InputDeviceComboBox.SelectedIndex = _settingsManager.ChannelOutputDevices.ContainsKey("GLOBAL_INPUT")
                ? _settingsManager.ChannelOutputDevices["GLOBAL_INPUT"]
                : 0;
        }

        private void LoadChannelOutputSettings()
        {
            List<string> outputDevices = GetAudioOutputDevices();

            foreach (var channel in _channels)
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
                    SelectedIndex = _settingsManager.ChannelOutputDevices.ContainsKey(channel.Tgid)
                        ? _settingsManager.ChannelOutputDevices[channel.Tgid]
                        : 0
                };

                outputDeviceComboBox.SelectionChanged += (s, e) =>
                {
                    int selectedIndex = outputDeviceComboBox.SelectedIndex;
                    _selectedOutputDevices[channel.Tgid] = selectedIndex;
                };

                ChannelOutputStackPanel.Children.Add(channelLabel);
                ChannelOutputStackPanel.Children.Add(outputDeviceComboBox);
            }
        }

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

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            int selectedInputIndex = InputDeviceComboBox.SelectedIndex;
            _settingsManager.UpdateChannelOutputDevice("GLOBAL_INPUT", selectedInputIndex);

            foreach (var entry in _selectedOutputDevices)
            {
                _settingsManager.UpdateChannelOutputDevice(entry.Key, entry.Value);
                _audioManager.SetTalkgroupOutputDevice(entry.Key, entry.Value);
            }

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
