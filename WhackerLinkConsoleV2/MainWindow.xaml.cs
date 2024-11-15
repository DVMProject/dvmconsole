﻿using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WhackerLinkLib.Models.Radio;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using WhackerLinkConsoleV2.Controls;
using System.Collections.Generic;

namespace WhackerLinkConsoleV2
{
    public partial class MainWindow : Window
    {
        public Codeplug Codeplug { get; set; }
        private bool isEditMode = false;

        private UIElement _draggedElement;
        private Point _startPoint;
        private double _offsetX;
        private double _offsetY;
        private bool _isDragging;

        private SettingsManager _settingsManager = new SettingsManager();

        public MainWindow()
        {
            InitializeComponent();
            _settingsManager.LoadSettings();
            Loaded += MainWindow_Loaded;
        }

        private void OpenCodeplug_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Codeplug Files (*.yml)|*.yml|All Files (*.*)|*.*",
                Title = "Open Codeplug"
            };
            if (openFileDialog.ShowDialog() == true)
            {
                LoadCodeplug(openFileDialog.FileName);

                _settingsManager.LastCodeplugPath = openFileDialog.FileName;
                _settingsManager.SaveSettings();
            }
        }

        private void ResetSettings_Click(object sender, RoutedEventArgs e)
        {
            if (File.Exists("UserSettings.json"))
                File.Delete("UserSettings.json");
        }

        private void LoadCodeplug(string filePath)
        {
            try
            {
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .Build();

                var yaml = File.ReadAllText(filePath);
                Codeplug = deserializer.Deserialize<Codeplug>(yaml);

                GenerateChannelWidgets();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading codeplug: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void GenerateChannelWidgets()
        {
            ChannelsCanvas.Children.Clear();
            double offsetX = 20;
            double offsetY = 20;

            if (_settingsManager.ShowSystemStatus && Codeplug != null)
            {
                foreach (var system in Codeplug.Systems)
                {
                    var systemStatusBox = new SystemStatusBox(system.Name, system.Address, system.Port);

                    Canvas.SetLeft(systemStatusBox, offsetX);
                    Canvas.SetTop(systemStatusBox, offsetY);

                    systemStatusBox.MouseLeftButtonDown += SystemStatusBox_MouseLeftButtonDown;
                    systemStatusBox.MouseMove += SystemStatusBox_MouseMove;
                    systemStatusBox.MouseRightButtonDown += SystemStatusBox_MouseRightButtonDown;

                    ChannelsCanvas.Children.Add(systemStatusBox);

                    offsetX += 220;
                    if (offsetX + 200 > ChannelsCanvas.ActualWidth)
                    {
                        offsetX = 20;
                        offsetY += 140;
                    }
                }
            }

            if (_settingsManager.ShowChannels && Codeplug != null)
            {
                foreach (var zone in Codeplug.Zones)
                {
                    foreach (var channel in zone.Channels)
                    {
                        var channelBox = new ChannelBox(channel.Name, channel.System, channel.Tgid);

                        if (_settingsManager.ChannelPositions.TryGetValue(channel.Name, out var position))
                        {
                            Canvas.SetLeft(channelBox, position.X);
                            Canvas.SetTop(channelBox, position.Y);
                        }
                        else
                        {
                            Canvas.SetLeft(channelBox, offsetX);
                            Canvas.SetTop(channelBox, offsetY);
                        }

                        channelBox.MouseLeftButtonDown += ChannelBox_MouseLeftButtonDown;
                        channelBox.MouseMove += ChannelBox_MouseMove;
                        channelBox.MouseRightButtonDown += ChannelBox_MouseRightButtonDown;
                        ChannelsCanvas.Children.Add(channelBox);

                        offsetX += 220;
                        if (offsetX + 200 > ChannelsCanvas.ActualWidth)
                        {
                            offsetX = 20;
                            offsetY += 140;
                        }
                    }
                }
            }
        }

        private void SelectWidgets_Click(object sender, RoutedEventArgs e)
        {
            var widgetSelectionWindow = new WidgetSelectionWindow();
            widgetSelectionWindow.Owner = this;
            if (widgetSelectionWindow.ShowDialog() == true)
            {
                _settingsManager.ShowSystemStatus = widgetSelectionWindow.ShowSystemStatus;
                _settingsManager.ShowChannels = widgetSelectionWindow.ShowChannels;
                GenerateChannelWidgets();
                _settingsManager.SaveSettings();
            }
        }

        private void ChannelBox_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!isEditMode || !(sender is UIElement element)) return;

            _draggedElement = element;
            _startPoint = e.GetPosition(ChannelsCanvas);
            _offsetX = _startPoint.X - Canvas.GetLeft(_draggedElement);
            _offsetY = _startPoint.Y - Canvas.GetTop(_draggedElement);
            _isDragging = true;

            element.CaptureMouse();
        }

        private void ChannelBox_MouseMove(object sender, MouseEventArgs e)
        {
            if (!isEditMode || !_isDragging || _draggedElement == null) return;

            Point currentPosition = e.GetPosition(ChannelsCanvas);
            double newLeft = Math.Max(0, Math.Min(currentPosition.X - _offsetX, ChannelsCanvas.ActualWidth - _draggedElement.RenderSize.Width));
            double newTop = Math.Max(0, Math.Min(currentPosition.Y - _offsetY, ChannelsCanvas.ActualHeight - _draggedElement.RenderSize.Height));

            Canvas.SetLeft(_draggedElement, newLeft);
            Canvas.SetTop(_draggedElement, newTop);

            if (_draggedElement is ChannelBox channelBox)
            {
                _settingsManager.UpdateChannelPosition(channelBox.ChannelName, newLeft, newTop);
            }
        }

        private void ChannelBox_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!isEditMode || !_isDragging || _draggedElement == null) return;

            _isDragging = false;
            _draggedElement.ReleaseMouseCapture();
            _draggedElement = null;
        }

        private void SystemStatusBox_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => ChannelBox_MouseLeftButtonDown(sender, e);
        private void SystemStatusBox_MouseMove(object sender, MouseEventArgs e) => ChannelBox_MouseMove(sender, e);
        private void SystemStatusBox_MouseRightButtonDown(object sender, MouseButtonEventArgs e) => ChannelBox_MouseRightButtonDown(sender, e);

        private void ToggleEditMode_Click(object sender, RoutedEventArgs e)
        {
            isEditMode = !isEditMode;
            var menuItem = (MenuItem)sender;
            menuItem.Header = isEditMode ? "Disable Edit Mode" : "Enable Edit Mode";
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            _settingsManager.SaveSettings();
            base.OnClosing(e);
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_settingsManager.LastCodeplugPath) && File.Exists(_settingsManager.LastCodeplugPath))
            {
                LoadCodeplug(_settingsManager.LastCodeplugPath);
            }
            else
            {
                GenerateChannelWidgets();
            }
        }
    }
}
