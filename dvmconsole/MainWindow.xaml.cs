// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Desktop Dispatch Console
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Desktop Dispatch Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2024-2025 Caleb, K4PHP
*   Copyright (C) 2025 J. Dean
*   Copyright (C) 2025 Bryan Biedenkapp, N2PLL
*   Copyright (C) 2025 Steven Jennison, KD8RHO
*   Copyright (C) 2025 Lorenzo L Romero, K2LLR
*
*/

using System.Net.Sockets;
using System.IO;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using NAudio;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NWaves.Signals;

using MaterialDesignThemes.Wpf;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

using dvmconsole.Controls;

using Constants = fnecore.Constants;
using fnecore;
using fnecore.DMR;
using fnecore.P25;
using fnecore.P25.KMM;
using fnecore.P25.LC.TSBK;

using Application = System.Windows.Application;
using Cursors = System.Windows.Input.Cursors;
using MessageBox = System.Windows.MessageBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace dvmconsole
{
    /// <summary>
    /// Data structure representing the position of a <see cref="ChannelBox"/> widget.
    /// </summary>
    public class ChannelPosition
    {
        /*
         ** Properties
         */

        /// <summary>
        /// X
        /// </summary>
        public double X { get; set; }
        /// <summary>
        /// Y
        /// </summary>
        public double Y { get; set; }
    } // public class ChannelPosition

    /// <summary>
    /// Interaction logic for MainWindow.xaml.
    /// </summary>
    public partial class MainWindow : Window
    {
        public const double MIN_WIDTH = 875;
        public const double MIN_HEIGHT = 700;

        public const int PCM_SAMPLES_LENGTH = 320; // MBE_SAMPLES_LENGTH * 2

        public const int MAX_SYSTEM_NAME_LEN = 10;
        public const int MAX_CHANNEL_NAME_LEN = 21;

        private const string INVALID_SYSTEM = "INVALID SYSTEM";
        private const string INVALID_CODEPLUG_CHANNEL = "INVALID CODEPLUG CHANNEL";
        private const string ERR_INVALID_FNE_REF = "invalid FNE peer reference, this should not happen";
        private const string ERR_INVALID_CODEPLUG = "Codeplug has/may contain errors";
        private const string ERR_SKIPPING_AUDIO = "Skipping channel for audio";

        private const string PLEASE_CHECK_CODEPLUG = "Please check your codeplug for errors.";
        private const string PLEASE_RESTART_CONSOLE = "Please restart the console.";

        private const string URI_RESOURCE_PATH = "pack://application:,,,/dvmconsole;component";

        private bool isShuttingDown = false;
        private bool globalPttState = false;

        private const int GridSize = 5;

        private UIElement draggedElement;
        private Point startPoint;
        private double offsetX;
        private double offsetY;
        private bool isDragging;

        private bool windowLoaded = false;
        
        // Tab management
        private Dictionary<TabItem, ScrollViewer> tabScrollViewers = new Dictionary<TabItem, ScrollViewer>();
        private Dictionary<TabItem, Canvas> tabCanvases = new Dictionary<TabItem, Canvas>();
        private Dictionary<UIElement, TabItem> elementToTabMap = new Dictionary<UIElement, TabItem>();
        private Dictionary<TabItem, StackPanel> tabHeaders = new Dictionary<TabItem, StackPanel>();
        private bool noSaveSettingsOnClose = false;
        private SettingsManager settingsManager = new SettingsManager();
        private SelectedChannelsManager selectedChannelsManager;
        private FlashingBackgroundManager flashingManager;

        private Brush btnGlobalPttDefaultBg;

        private ChannelBox playbackChannelBox;

        private CallHistoryWindow callHistoryWindow;

        public static string PLAYBACKTG = "LOCPLAYBACK";
        public static string PLAYBACKSYS = "Local Playback";
        public static string PLAYBACKCHNAME = "PLAYBACK";

        private readonly WaveInEvent waveIn;
        private readonly AudioManager audioManager;

        private static System.Timers.Timer channelHoldTimer;

        private Dictionary<string, SlotStatus> systemStatuses = new Dictionary<string, SlotStatus>();
        private FneSystemManager fneSystemManager = new FneSystemManager();

        private bool selectAll = false;
        private KeyboardManager keyboardManager;

        private CancellationTokenSource maintainenceCancelToken = new CancellationTokenSource();
        private Task maintainenceTask = null;

        /*
        ** Properties
        */

        /// <summary>
        /// Codeplug
        /// </summary>
        public Codeplug Codeplug { get; set; }

        /*
        ** Methods
        */

        /// <summary>
        /// Initializes a new instance of the <see cref="MainWindow"/> class.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            this.keyboardManager = new KeyboardManager();
            MinWidth = Width = MIN_WIDTH;
            MinHeight = Height = MIN_HEIGHT;

            DisableControls();

            settingsManager.LoadSettings();
            InitializeKeyboardShortcuts();
            callHistoryWindow = new CallHistoryWindow(settingsManager, CallHistoryWindow.MAX_CALL_HISTORY);

            selectedChannelsManager = new SelectedChannelsManager();
            flashingManager = new FlashingBackgroundManager(null, channelsCanvas, null, this);

            channelHoldTimer = new System.Timers.Timer(10000);
            channelHoldTimer.Elapsed += OnHoldTimerElapsed;
            channelHoldTimer.AutoReset = true;
            channelHoldTimer.Enabled = true;

            waveIn = new WaveInEvent { WaveFormat = new WaveFormat(8000, 16, 1) };
            waveIn.DataAvailable += WaveIn_DataAvailable;
            waveIn.RecordingStopped += WaveIn_RecordingStopped;

            try
            {
                waveIn.StartRecording();
            }
            catch (MmException ex)
            {
                MessageBox.Show($"Error initializing audio input device, {ex.Message}. This *will* cause console inconsistency, and inability to transmit audio.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Log.StackTrace(ex, false);
            }

            audioManager = new AudioManager(settingsManager);

            btnGlobalPtt.PreviewMouseLeftButtonDown += btnGlobalPtt_MouseLeftButtonDown;
            btnGlobalPtt.PreviewMouseLeftButtonUp += btnGlobalPtt_MouseLeftButtonUp;
            btnGlobalPtt.MouseRightButtonDown += btnGlobalPtt_MouseRightButtonDown;
            
            // Handle tab selection changes to update background
            resourceTabs.SelectionChanged += ResourceTabs_SelectionChanged;

            selectedChannelsManager.SelectedChannelsChanged += SelectedChannelsChanged;
            selectedChannelsManager.PrimaryChannelChanged += PrimaryChannelChanged;

            LocationChanged += MainWindow_LocationChanged;
            SizeChanged += MainWindow_SizeChanged;
            Loaded += MainWindow_Loaded;

            // Initialize first tab
            InitializeFirstTab();
        }
        
        /// <summary>
        /// Initializes the first tab with the default canvas
        /// </summary>
        private void InitializeFirstTab()
        {
            TabItem firstTab = new TabItem();
            
            // Create a custom header with text and optional audio icon
            StackPanel headerPanel = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new Thickness(0, 0, 4, 0)
            };
            
            TextBlock headerText = new TextBlock
            {
                Text = "Tab 1",
                VerticalAlignment = VerticalAlignment.Center
            };
            headerPanel.Children.Add(headerText);
            
            // Audio icon (initially hidden)
            Image audioIcon = new Image
            {
                Source = new BitmapImage(new Uri($"{URI_RESOURCE_PATH}/Assets/audio_receive.png")),
                Width = 16,
                Height = 16,
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
                Name = "AudioIcon"
            };
            headerPanel.Children.Add(audioIcon);
            
            firstTab.Header = headerPanel;
            tabHeaders[firstTab] = headerPanel;
            
            // Remove canvasScrollViewer from its parent (Grid) if it exists
            DependencyObject parent = canvasScrollViewer.Parent;
            if (parent is System.Windows.Controls.Panel panel)
            {
                panel.Children.Remove(canvasScrollViewer);
            }
            else if (parent is ContentControl contentControl)
            {
                contentControl.Content = null;
            }
            
            // Now we can safely assign it to the tab
            firstTab.Content = canvasScrollViewer;

            tabScrollViewers[firstTab] = canvasScrollViewer;
            tabCanvases[firstTab] = channelsCanvas;
            resourceTabs.Items.Add(firstTab);
            resourceTabs.SelectedItem = firstTab;
        }
        
        /// <summary>
        /// Creates tabs from the codeplug zones (each zone becomes a tab)
        /// </summary>
        private void CreateTabsFromCodeplug()
        {
            // Clear existing tabs
            resourceTabs.Items.Clear();
            tabCanvases.Clear();
            tabHeaders.Clear();
            elementToTabMap.Clear();
            
            // Create tabs from zones
            if (Codeplug.Zones != null && Codeplug.Zones.Count > 0)
            {
                foreach (var zone in Codeplug.Zones)
                    CreateNewTab(zone.Name, zone.TabColor);
                
                // Apply current background to all newly created tabs
                ApplyCurrentBackgroundToAllTabs();
                
                // Select the first tab
                if (resourceTabs.Items.Count > 0)
                    resourceTabs.SelectedItem = resourceTabs.Items[0];
            }
            else
            {
                // No zones defined, create a default tab
                TabItem firstTab = new TabItem();
                
                // Apply the tab style from resources
                if (resourceTabs.Resources["TabItemStyle"] is Style tabStyle)
                    firstTab.Style = tabStyle;
                
            // Create a custom header with text and optional audio icon
            StackPanel headerPanel = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                    Margin = new Thickness(0, 0, 4, 0)
                };
                
                TextBlock headerText = new TextBlock
                {
                    Text = "Tab 1",
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = settingsManager.DarkMode ? Brushes.White : Brushes.Black
                };
                headerPanel.Children.Add(headerText);
                
                // Audio icon (initially hidden)
                Image audioIcon = new Image
                {
                    Source = new BitmapImage(new Uri($"{URI_RESOURCE_PATH}/Assets/audio_receive.png")),
                    Width = 16,
                    Height = 16,
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Visibility = Visibility.Collapsed,
                    Name = "AudioIcon"
                };
                headerPanel.Children.Add(audioIcon);
                
                firstTab.Header = headerPanel;
                tabHeaders[firstTab] = headerPanel;
                
                // Remove canvasScrollViewer from its parent if it exists
                DependencyObject parent = canvasScrollViewer.Parent;
                if (parent is System.Windows.Controls.Panel panel)
                {
                    panel.Children.Remove(canvasScrollViewer);
                }
                else if (parent is ContentControl contentControl)
                {
                    contentControl.Content = null;
                }
                
                firstTab.Content = canvasScrollViewer;
                tabScrollViewers[firstTab] = canvasScrollViewer;
                tabCanvases[firstTab] = channelsCanvas;
                resourceTabs.Items.Add(firstTab);
                resourceTabs.SelectedItem = firstTab;
                
                // Apply current background to the newly created tab
                ApplyCurrentBackgroundToAllTabs();
            }
        }
        
        /// <summary>
        /// Creates a new tab with a ScrollViewer and Canvas
        /// </summary>
        /// <param name="tabName"></param>
        /// <param name="tabColor"></param>
        private TabItem CreateNewTab(string tabName, string tabColor = null)
        {
            TabItem tab = new TabItem();
            
            // Apply the tab style from resources
            if (resourceTabs.Resources["TabItemStyle"] is Style tabStyle)
                tab.Style = tabStyle;

            if (tabColor != null)
            {
                SolidColorBrush bgBrush = (SolidColorBrush)new BrushConverter().ConvertFrom(tabColor);
                ColorZoneAssist.SetMode(tab, MaterialDesignThemes.Wpf.ColorZoneMode.Custom);
                ColorZoneAssist.SetBackground(tab, bgBrush);
                //ColorZoneAssist.SetForeground(tab, Brushes.White);
            }

            // Create a custom header with text and optional audio icon
            StackPanel headerPanel = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new Thickness(0, 0, 4, 0)
            };
            
            TextBlock headerText = new TextBlock
            {
                Text = tabName,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = settingsManager.DarkMode ? Brushes.White : Brushes.Black
            };
            headerPanel.Children.Add(headerText);
            
            // Audio icon (initially hidden)
            Image audioIcon = new Image
            {
                Source = new BitmapImage(new Uri($"{URI_RESOURCE_PATH}/Assets/audio_receive.png")),
                Width = 16,
                Height = 16,
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
                Name = "AudioIcon"
            };
            headerPanel.Children.Add(audioIcon);
            
            tab.Header = headerPanel;
            tabHeaders[tab] = headerPanel;
            
            ScrollViewer scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalAlignment = System.Windows.VerticalAlignment.Stretch,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch
            };
            
            Canvas canvas = new Canvas
            {
                VerticalAlignment = VerticalAlignment.Top,
            };
            
            // Set background from original canvas or channelsCanvasBg
            // Use the current background from channelsCanvasBg if available, otherwise use default
            if (channelsCanvasBg != null && channelsCanvasBg.ImageSource != null)
            {
                canvas.Background = new ImageBrush(channelsCanvasBg.ImageSource) { Stretch = Stretch.UniformToFill };
            }
            else if (channelsCanvas.Background is ImageBrush originalBg)
            {
                canvas.Background = new ImageBrush(originalBg.ImageSource) { Stretch = Stretch.UniformToFill };
            }
            else
            {
                // Use default background based on dark mode
                BitmapImage defaultBg = new BitmapImage();
                defaultBg.BeginInit();
                if (settingsManager.DarkMode)
                    defaultBg.UriSource = new Uri($"{URI_RESOURCE_PATH}/Assets/bg_main_hd_dark.png");
                else
                    defaultBg.UriSource = new Uri($"{URI_RESOURCE_PATH}/Assets/bg_main_hd_light.png");
                defaultBg.EndInit();
                canvas.Background = new ImageBrush(defaultBg) { Stretch = Stretch.UniformToFill };
            }
            
            scrollViewer.Content = canvas;
            tab.Content = scrollViewer;

            tabScrollViewers[tab] = scrollViewer;
            tabCanvases[tab] = canvas;
            
            // Hook up property changed to track audio state
            tab.DataContext = tab;

            resourceTabs.Items.Add(tab);
            return tab;
        }
        
        /// <summary>
        /// Updates the tab appearance based on whether it contains channels playing audio
        /// </summary>
        private void UpdateTabAudioIndicator(TabItem tab)
        {
            if (!tabHeaders.ContainsKey(tab))
                return;
                
            Canvas tabCanvas = tabCanvases.ContainsKey(tab) ? tabCanvases[tab] : null;
            if (tabCanvas == null)
                return;
            
            // Check if any channel in this tab is receiving audio
            bool hasAudio = false;
            foreach (UIElement element in tabCanvas.Children)
            {
                if (element is ChannelBox channelBox)
                {
                    if (channelBox.IsReceiving || channelBox.IsReceivingEncrypted)
                    {
                        hasAudio = true;
                        break;
                    }
                }
            }
            
            StackPanel headerPanel = tabHeaders[tab];
            
            // Find the audio icon
            Image audioIcon = null;
            foreach (UIElement child in headerPanel.Children)
            {
                if (child is Image img && img.Name == "AudioIcon")
                {
                    audioIcon = img;
                    break;
                }
            }
            
            if (hasAudio)
            {
                // Set tab background to green (using the same green as receiving channels)
                tab.Background = ChannelBox.GREEN_GRADIENT;
                
                // Show audio icon
                if (audioIcon != null)
                {
                    audioIcon.Visibility = Visibility.Visible;
                }
            }
            else
            {
                // Reset tab background to default
                tab.Background = null;
                
                // Hide audio icon
                if (audioIcon != null)
                {
                    audioIcon.Visibility = Visibility.Collapsed;
                }
            }
        }
        
        /// <summary>
        /// Updates all tab audio indicators
        /// </summary>
        private void UpdateAllTabAudioIndicators()
        {
            foreach (TabItem tab in resourceTabs.Items)
            {
                UpdateTabAudioIndicator(tab);
            }
        }
        
        /// <summary>
        /// Updates the tab audio indicator for a specific channel
        /// </summary>
        private void UpdateTabAudioIndicatorForChannel(ChannelBox channel)
        {
            TabItem tab = GetTabForElement(channel);
            if (tab != null)
            {
                UpdateTabAudioIndicator(tab);
            }
        }
        
        /// <summary>
        /// Gets the currently active canvas
        /// </summary>
        private Canvas GetActiveCanvas()
        {
            if (resourceTabs.SelectedItem is TabItem selectedTab && tabCanvases.ContainsKey(selectedTab))
            {
                return tabCanvases[selectedTab];
            }
            // If no tab selected or tab not found, return the original canvas
            return channelsCanvas;
        }
        
        /// <summary>
        /// Gets the tab that contains the given element
        /// </summary>
        private TabItem GetTabForElement(UIElement element)
        {
            if (elementToTabMap.TryGetValue(element, out TabItem tab))
            {
                return tab;
            }
            // If not in map, it's in the first tab (or original canvas)
            if (resourceTabs.Items.Count > 0)
            {
                return resourceTabs.Items[0] as TabItem;
            }
            return null;
        }
        
        /// <summary>
        /// Gets all canvases (from all tabs plus the original)
        /// </summary>
        internal IEnumerable<Canvas> GetAllCanvases()
        {
            foreach (var canvas in tabCanvases.Values)
            {
                yield return canvas;
            }
            yield return channelsCanvas;
        }

        /// <summary>
        /// 
        /// </summary>
        private void PrimaryChannelChanged()
        {
            var primaryChannel = selectedChannelsManager.PrimaryChannel;
            // Check all canvases in all tabs
            foreach (var canvas in tabCanvases.Values)
            {
                foreach (UIElement element in canvas.Children)
                {
                    if (element is ChannelBox box)
                    {
                        box.IsPrimary = box == primaryChannel;
                    }
                }
            }
            // Also check the original canvas
            foreach (UIElement element in channelsCanvas.Children)
            {
                if (element is ChannelBox box)
                {
                    box.IsPrimary = box == primaryChannel;
                }
            }
        }

        /// <summary>
        /// Helper to enable menu controls for Commands submenu.
        /// </summary>
        private void EnableCommandControls()
        {
            menuPageSubscriber.IsEnabled = true;
            menuRadioCheckSubscriber.IsEnabled = true;
            menuInhibitSubscriber.IsEnabled = true;
            menuUninhibitSubscriber.IsEnabled = true;
            menuQuickCall2.IsEnabled = true;
        }

        /// <summary>
        /// Helper to enable form controls when settings and codeplug are loaded.
        /// </summary>
        private void EnableControls()
        {
            btnGlobalPtt.IsEnabled = true;
            btnAlert1.IsEnabled = true;
            btnAlert2.IsEnabled = true;
            btnAlert3.IsEnabled = true;
            btnPageSub.IsEnabled = true;
            btnSelectAll.IsEnabled = true;
            btnKeyStatus.IsEnabled = true;
            btnCallHistory.IsEnabled = true;
        }

        /// <summary>
        /// Helper to disable menu controls for Commands submenu.
        /// </summary>
        private void DisableCommandControls()
        {
            menuPageSubscriber.IsEnabled = false;
            menuRadioCheckSubscriber.IsEnabled = false;
            menuInhibitSubscriber.IsEnabled = false;
            menuUninhibitSubscriber.IsEnabled = false;
            menuQuickCall2.IsEnabled = false;
        }

        /// <summary>
        /// Helper to disable form controls when settings load fails.
        /// </summary>
        private void DisableControls()
        {
            DisableCommandControls();

            btnGlobalPtt.IsEnabled = false;
            btnAlert1.IsEnabled = false;
            btnAlert2.IsEnabled = false;
            btnAlert3.IsEnabled = false;
            btnPageSub.IsEnabled = false;
            btnSelectAll.IsEnabled = false;
            btnKeyStatus.IsEnabled = false;
            btnCallHistory.IsEnabled = false;
        }

        /// <summary>
        /// Helper to load the codeplug.
        /// </summary>
        /// <param name="filePath"></param>
        private void LoadCodeplug(string filePath)
        {
            DisableControls();

            // Clear all canvases
            foreach (var canvas in tabCanvases.Values)
            {
                canvas.Children.Clear();
            }
            channelsCanvas.Children.Clear();
            elementToTabMap.Clear();
            systemStatuses.Clear();

            fneSystemManager.ClearAll();

            try
            {
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .IgnoreUnmatchedProperties()
                    .Build();

                string yaml = File.ReadAllText(filePath);
                Codeplug = deserializer.Deserialize<Codeplug>(yaml);

                // perform codeplug validation
                List<string> errors = new List<string>();

                // ensure string lengths are acceptable
                // systems
                Dictionary<string, string> replacedSystemNames = new Dictionary<string, string>();
                foreach (Codeplug.System system in Codeplug.Systems)
                {
                    // ensure system name is less then or equals to the max
                    if (system.Name.Length > MAX_SYSTEM_NAME_LEN)
                    {
                        string original = system.Name;
                        system.Name = system.Name.Substring(0, MAX_SYSTEM_NAME_LEN);
                        replacedSystemNames.Add(original, system.Name);
                        Log.WriteLine($"{original} SYSTEM NAME was greater then {MAX_SYSTEM_NAME_LEN} characters, truncated {system.Name}");
                    }
                }

                // zones
                foreach (Codeplug.Zone zone in Codeplug.Zones)
                {
                    // channels
                    foreach (Codeplug.Channel channel in zone.Channels)
                    {
                        if (Codeplug.Systems.Find((x) => x.Name == channel.System) == null)
                            errors.Add($"{channel.Name} refers to an {INVALID_SYSTEM} {channel.System}.");

                        // because we possibly truncated system names above lets see if we
                        // have to replaced the related system name
                        if (replacedSystemNames.ContainsKey(channel.System))
                            channel.System = replacedSystemNames[channel.System];

                        // ensure channel name is less then or equals to the max
                        if (channel.Name.Length > MAX_CHANNEL_NAME_LEN)
                        {
                            string original = channel.Name;
                            channel.Name = channel.Name.Substring(0, MAX_CHANNEL_NAME_LEN);
                            Log.WriteLine($"{original} CHANNEL NAME was greater then {MAX_CHANNEL_NAME_LEN} characters, truncated {channel.Name}");
                        }

                        // clamp slot value
                        if (channel.Slot <= 0)
                            channel.Slot = 1;
                        if (channel.Slot > 2)
                            channel.Slot = 1;
                    }
                }

                // compile list of errors and throw up a messagebox of doom
                if (errors.Count > 0)
                {
                    string newLine = Environment.NewLine + Environment.NewLine;
                    string messageBoxString = $"Loaded codeplug {filePath} contains errors. {PLEASE_CHECK_CODEPLUG}" + newLine;
                    foreach (string error in errors)
                        messageBoxString += error + newLine;
                    messageBoxString = messageBoxString.TrimEnd(new char[] { '\r', '\n' });

                    MessageBox.Show(messageBoxString, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }

                // generate widgets and enable controls
                GenerateChannelWidgets();
                EnableControls();
                MainWindow_SizeChanged(this, null);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading codeplug: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Log.StackTrace(ex, false);
                DisableControls();
            }
        }

        /// <summary>
        /// Helper to initialize and generate channel widgets on the canvas.
        /// </summary>
        private void GenerateChannelWidgets()
        {
            // Clear all canvases
            foreach (var canvas in tabCanvases.Values)
            {
                canvas.Children.Clear();
            }
            channelsCanvas.Children.Clear();
            elementToTabMap.Clear();
            systemStatuses.Clear();
            
            // Note: tabHeaders is not cleared here as tabs are recreated in CreateTabsFromCodeplug

            fneSystemManager.ClearAll();

            Cursor = Cursors.Wait;

            // Create tabs from codeplug configuration (if codeplug exists)
            if (Codeplug != null)
                CreateTabsFromCodeplug();
            
            // Create a dictionary to map tab names to TabItems
            Dictionary<string, TabItem> tabNameToTabItem = new Dictionary<string, TabItem>();
            foreach (TabItem tab in resourceTabs.Items)
            {
                string tabName = null;
                
                // Check if header is a string (legacy) or a StackPanel (new custom header)
                if (tab.Header is string name)
                {
                    tabName = name;
                }
                else if (tab.Header is StackPanel headerPanel)
                {
                    // Extract text from the first TextBlock in the StackPanel
                    foreach (UIElement child in headerPanel.Children)
                    {
                        if (child is TextBlock textBlock)
                        {
                            tabName = textBlock.Text;
                            break;
                        }
                    }
                }
                
                if (!string.IsNullOrEmpty(tabName))
                {
                    tabNameToTabItem[tabName] = tab;
                }
            }
            
            // Get default tab (first tab or create one if none exist)
            TabItem defaultTab = resourceTabs.Items.Count > 0 ? resourceTabs.Items[0] as TabItem : null;
            Canvas defaultCanvas = defaultTab != null && tabCanvases.ContainsKey(defaultTab) 
                ? tabCanvases[defaultTab] 
                : channelsCanvas;

            if (Codeplug != null)
            {
                // Track offset for system status boxes (add to default tab)
                double systemOffsetX = 20;
                double systemOffsetY = 20;
                
                // load and initialize systems
                foreach (var system in Codeplug.Systems)
                {
                    SystemStatusBox systemStatusBox = new SystemStatusBox(system.Name, system.Address, system.Port);
                    if (settingsManager.SystemStatusPositions.TryGetValue(system.Name, out var position))
                    {
                        Canvas.SetLeft(systemStatusBox, position.X);
                        Canvas.SetTop(systemStatusBox, position.Y);
                    }
                    else
                    {
                        Canvas.SetLeft(systemStatusBox, systemOffsetX);
                        Canvas.SetTop(systemStatusBox, systemOffsetY);
                    }

                    // widget placement
                    systemStatusBox.MouseRightButtonDown += SystemStatusBox_MouseRightButtonDown;
                    systemStatusBox.MouseRightButtonUp += SystemStatusBox_MouseRightButtonUp;
                    systemStatusBox.MouseMove += SystemStatusBox_MouseMove;

                    defaultCanvas.Children.Add(systemStatusBox);
                    if (defaultTab != null)
                        elementToTabMap[systemStatusBox] = defaultTab;

                    systemOffsetX += 225;
                    if (systemOffsetX + 220 > defaultCanvas.ActualWidth)
                    {
                        systemOffsetX = 20;
                        systemOffsetY += 106;
                    }

                    // do we have aliases for this system?
                    if (File.Exists(system.AliasPath))
                        system.RidAlias = AliasTools.LoadAliases(system.AliasPath);

                    fneSystemManager.AddFneSystem(system.Name, system, this);
                    PeerSystem peer = fneSystemManager.GetFneSystem(system.Name);

                    // hook FNE events
                    peer.peer.PeerConnected += (sender, response) =>
                    {
                        Log.WriteLine("FNE Peer connected");
                        Dispatcher.Invoke(() =>
                        {
                            EnableCommandControls();
                            systemStatusBox.Background = ChannelBox.GREEN_GRADIENT;
                            systemStatusBox.ConnectionState = "Connected";
                        });
                    };

                    peer.peer.PeerDisconnected += (response) =>
                    {
                        Log.WriteLine("FNE Peer disconnected");
                        Dispatcher.Invoke(() =>
                        {
                            DisableCommandControls();
                            systemStatusBox.Background = ChannelBox.RED_GRADIENT;
                            systemStatusBox.ConnectionState = "Disconnected";

                            foreach (ChannelBox channel in selectedChannelsManager.GetSelectedChannels())
                            {
                                if (channel.SystemName == PLAYBACKSYS || channel.ChannelName == PLAYBACKCHNAME || channel.DstId == PLAYBACKTG)
                                    continue;

                                if (channel.IsReceiving || channel.IsReceivingEncrypted)
                                {
                                    channel.IsReceiving = false;
                                    channel.PeerId = 0;
                                    channel.RxStreamId = 0;

                                    channel.IsReceivingEncrypted = false;
                                    channel.Background = ChannelBox.BLUE_GRADIENT;
                                    channel.VolumeMeterLevel = 0;
                                    
                                    // Update tab audio indicator
                                    UpdateTabAudioIndicatorForChannel(channel);
                                }
                            }
                        });
                    };

                    // start peer
                    Task.Run(() =>
                    {
                        try
                        {
                            peer.Start();
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"Fatal error while connecting to server. {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            Log.StackTrace(ex, false);
                        }
                    });

                    if (!settingsManager.ShowSystemStatus)
                        systemStatusBox.Visibility = Visibility.Collapsed;
                }
            }

            // are we showing channels?
            if (settingsManager.ShowChannels && Codeplug != null)
            {
                // Track offset per tab
                Dictionary<TabItem, Point> tabOffsets = new Dictionary<TabItem, Point>();
                
                // iterate through the codeplug zones and begin building channel widgets
                foreach (var zone in Codeplug.Zones)
                {
                    // Get the tab for this zone (zone name = tab name)
                    TabItem targetTab = defaultTab;
                    if (tabNameToTabItem.TryGetValue(zone.Name, out TabItem zoneTab))
                    {
                        targetTab = zoneTab;
                    }
                    
                    // iterate through zone channels
                    foreach (var channel in zone.Channels)
                    {
                        // Get or create offset for this tab
                        if (!tabOffsets.ContainsKey(targetTab))
                        {
                            tabOffsets[targetTab] = new Point(20, 20);
                        }
                        Point tabOffset = tabOffsets[targetTab];
                        
                        // Get the canvas for this tab
                        Canvas targetCanvas = targetTab != null && tabCanvases.ContainsKey(targetTab) 
                            ? tabCanvases[targetTab] 
                            : channelsCanvas;
                        
                        ChannelBox channelBox = new ChannelBox(selectedChannelsManager, audioManager, channel.Name, channel.System, channel.Tgid, settingsManager.TogglePTTMode);
                        channelBox.ChannelMode = channel.Mode.ToUpperInvariant();
                        if (channel.GetAlgoId() != P25Defines.P25_ALGO_UNENCRYPT && channel.GetKeyId() > 0)
                            channelBox.IsTxEncrypted = true;

                        systemStatuses.Add(channel.Name, new SlotStatus());

                        if (settingsManager.ChannelPositions.TryGetValue(channel.Name, out var position))
                        {
                            Canvas.SetLeft(channelBox, position.X);
                            Canvas.SetTop(channelBox, position.Y);
                        }
                        else
                        {
                            Canvas.SetLeft(channelBox, tabOffset.X);
                            Canvas.SetTop(channelBox, tabOffset.Y);
                        }

                        channelBox.PTTButtonClicked += ChannelBox_PTTButtonClicked;
                        channelBox.PTTButtonPressed += ChannelBox_PTTButtonPressed;
                        channelBox.PTTButtonReleased += ChannelBox_PTTButtonReleased;
                        channelBox.PageButtonClicked += ChannelBox_PageButtonClicked;
                        channelBox.HoldChannelButtonClicked += ChannelBox_HoldChannelButtonClicked;

                        // widget placement
                        channelBox.MouseRightButtonDown += ChannelBox_MouseRightButtonDown;
                        channelBox.MouseRightButtonUp += ChannelBox_MouseRightButtonUp;
                        channelBox.MouseMove += ChannelBox_MouseMove;

                        targetCanvas.Children.Add(channelBox);
                        if (targetTab != null)
                            elementToTabMap[channelBox] = targetTab;

                        // Update offset for next channel in this tab
                        tabOffset.X += 269;
                        if (tabOffset.X + 264 > targetCanvas.ActualWidth)
                        {
                            tabOffset.X = 20;
                            tabOffset.Y += 116;
                        }
                        tabOffsets[targetTab] = tabOffset;
                    }
                }
            }

            // are we showing user configured alert tones?
            if (settingsManager.ShowAlertTones && Codeplug != null)
            {
                // Add alert tones to the default/first tab
                Canvas alertCanvas = defaultTab != null && tabCanvases.ContainsKey(defaultTab) 
                    ? tabCanvases[defaultTab] 
                    : channelsCanvas;
                    
                // iterate through the alert tones and begin building alert tone widges
                foreach (var alertPath in settingsManager.AlertToneFilePaths)
                {
                    AlertTone alertTone = new AlertTone(alertPath);

                    alertTone.OnAlertTone += SendAlertTone;

                    // widget placement
                    alertTone.MouseRightButtonDown += AlertTone_MouseRightButtonDown;
                    alertTone.MouseRightButtonUp += AlertTone_MouseRightButtonUp;
                    alertTone.MouseMove += AlertTone_MouseMove;

                    if (settingsManager.AlertTonePositions.TryGetValue(alertPath, out var position))
                    {
                        Canvas.SetLeft(alertTone, position.X);
                        Canvas.SetTop(alertTone, position.Y);
                    }
                    else
                    {
                        Canvas.SetLeft(alertTone, 20);
                        Canvas.SetTop(alertTone, 20);
                    }

                    alertCanvas.Children.Add(alertTone);
                    if (defaultTab != null)
                        elementToTabMap[alertTone] = defaultTab;
                }
            }

            // initialize the playback channel - add to default tab
            Canvas playbackCanvas = defaultTab != null && tabCanvases.ContainsKey(defaultTab) 
                ? tabCanvases[defaultTab] 
                : channelsCanvas;
                
            playbackChannelBox = new ChannelBox(selectedChannelsManager, audioManager, PLAYBACKCHNAME, PLAYBACKSYS, PLAYBACKTG);
            playbackChannelBox.ChannelMode = "Local";
            playbackChannelBox.HidePTTButton(); // playback box shouldn't have PTT

            if (settingsManager.ChannelPositions.TryGetValue(PLAYBACKCHNAME, out var pos))
            {
                Canvas.SetLeft(playbackChannelBox, pos.X);
                Canvas.SetTop(playbackChannelBox, pos.Y);
            }
            else
            {
                Canvas.SetLeft(playbackChannelBox, 20);
                Canvas.SetTop(playbackChannelBox, 20);
            }

            playbackChannelBox.PageButtonClicked += ChannelBox_PageButtonClicked;
            playbackChannelBox.HoldChannelButtonClicked += ChannelBox_HoldChannelButtonClicked;

            // widget placement
            playbackChannelBox.MouseRightButtonDown += ChannelBox_MouseRightButtonDown;
            playbackChannelBox.MouseRightButtonUp += ChannelBox_MouseRightButtonUp;
            playbackChannelBox.MouseMove += ChannelBox_MouseMove;

            playbackCanvas.Children.Add(playbackChannelBox);
            if (defaultTab != null)
                elementToTabMap[playbackChannelBox] = defaultTab;

            Cursor = Cursors.Arrow;
        }

        /// <summary>
        /// 
        /// </summary>
        private void SelectedChannelsChanged()
        {
            foreach (ChannelBox channel in selectedChannelsManager.GetSelectedChannels())
            {
                if (channel.SystemName == PLAYBACKSYS || channel.ChannelName == PLAYBACKCHNAME || channel.DstId == PLAYBACKTG)
                    continue;

                Codeplug.System system = Codeplug.GetSystemForChannel(channel.ChannelName);
                if (system == null)
                {
                    MessageBox.Show($"{channel.ChannelName} refers to an {INVALID_SYSTEM} {channel.SystemName}. {PLEASE_CHECK_CODEPLUG}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    channel.IsSelected = false;
                    selectedChannelsManager.RemoveSelectedChannel(channel);
                    continue;
                }

                Codeplug.Channel cpgChannel = Codeplug.GetChannelByName(channel.ChannelName);
                if (cpgChannel == null)
                {
                    // bryanb: this should actually never happen...
                    MessageBox.Show($"{channel.ChannelName} refers to an {INVALID_CODEPLUG_CHANNEL}. {PLEASE_CHECK_CODEPLUG}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    channel.IsSelected = false;
                    selectedChannelsManager.RemoveSelectedChannel(channel);
                    continue;
                }

                PeerSystem fne = fneSystemManager.GetFneSystem(system.Name);
                if (fne == null)
                {
                    MessageBox.Show($"{channel.ChannelName} has a {ERR_INVALID_FNE_REF}. {PLEASE_RESTART_CONSOLE}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    channel.IsSelected = false;
                    selectedChannelsManager.RemoveSelectedChannel(channel);
                    continue;
                }

                // is the channel selected?
                if (channel.IsSelected)
                {
                    // if the channel is configured for encryption request the key from the FNE
                    uint newTgid = uint.Parse(cpgChannel.Tgid);
                    if (cpgChannel.GetAlgoId() != 0 && cpgChannel.GetKeyId() != 0)
                    {
                        fne.peer.SendMasterKeyRequest(cpgChannel.GetAlgoId(), cpgChannel.GetKeyId());
                        if (Codeplug.KeyFile != null)
                        {
                            if (!File.Exists(Codeplug.KeyFile))
                            {
                                MessageBox.Show($"Key file {Codeplug.KeyFile} not found. {PLEASE_CHECK_CODEPLUG}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                            else
                            {
                                var deserializer = new DeserializerBuilder()
                                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                                    .IgnoreUnmatchedProperties()
                                    .Build();
                                var keys = deserializer.Deserialize<KeyContainer>(File.ReadAllText(Codeplug.KeyFile));
                                var KeysetItems = new Dictionary<int, KeysetItem>();

                                foreach (var keyEntry in keys.Keys)
                                {
                                    var keyItem = new KeyItem();
                                    keyItem.KeyId = keyEntry.KeyId;
                                    var keyBytes = keyEntry.KeyBytes;
                                    keyItem.SetKey(keyBytes,(uint)keyBytes.Length);
                                    if (!KeysetItems.ContainsKey(keyEntry.AlgId))
                                    {
                                        var asByte = (byte)keyEntry.AlgId;
                                        KeysetItems.Add(keyEntry.AlgId, new KeysetItem() { AlgId = asByte });
                                    }


                                    KeysetItems[keyEntry.AlgId].AddKey(keyItem);
                                }

                                foreach (var eventData in KeysetItems.Select(keyValuePair => keyValuePair.Value).Select(keysetItem => new KeyResponseEvent(0, new KmmModifyKey
                                         {
                                             AlgId = 0,
                                             KeyId = 0,
                                             MessageId = 0,
                                             MessageLength = 0,
                                             KeysetItem = keysetItem
                                         }, [])))
                                {
                                    KeyResponseReceived(eventData);
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Helper to reset channel states.
        /// </summary>
        /// <param name="e"></param>
        private void ResetChannel(ChannelBox e)
        {
            // reset values
            e.p25SeqNo = 0;
            e.p25N = 0;

            e.dmrSeqNo = 0;
            e.dmrN = 0;

            e.pktSeq = 0;

            e.TxStreamId = 0;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="e"></param>
        private void SendAlertTone(AlertTone e)
        {
            Task.Run(() => SendAlertTone(e.AlertFilePath));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="forHold"></param>
        private void SendAlertTone(string filePath, bool forHold = false)
        {
            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                try
                {
                    ChannelBox primaryChannel = selectedChannelsManager.PrimaryChannel;
                    List<ChannelBox> channelsToProcess = primaryChannel != null
                        ? new List<ChannelBox> { primaryChannel }
                        : selectedChannelsManager.GetSelectedChannels().ToList();

                    foreach (ChannelBox channel in channelsToProcess)
                    {

                        if (channel.SystemName == PLAYBACKSYS || channel.ChannelName == PLAYBACKCHNAME || channel.DstId == PLAYBACKTG)
                            return;

                        Codeplug.System system = Codeplug.GetSystemForChannel(channel.ChannelName);
                        if (system == null)
                        {
                            Log.WriteLine($"{channel.ChannelName} refers to an {INVALID_SYSTEM} {channel.SystemName}. {ERR_INVALID_CODEPLUG}. {ERR_SKIPPING_AUDIO}.");
                            channel.IsSelected = false;
                            selectedChannelsManager.RemoveSelectedChannel(channel);
                            return;
                        }

                        Codeplug.Channel cpgChannel = Codeplug.GetChannelByName(channel.ChannelName);
                        if (cpgChannel == null)
                        {
                            Log.WriteLine($"{channel.ChannelName} refers to an {INVALID_CODEPLUG_CHANNEL}. {ERR_INVALID_CODEPLUG}. {ERR_SKIPPING_AUDIO}.");
                            channel.IsSelected = false;
                            selectedChannelsManager.RemoveSelectedChannel(channel);
                            return;
                        }

                        PeerSystem fne = fneSystemManager.GetFneSystem(system.Name);
                        if (fne == null)
                        {
                            Log.WriteLine($"{channel.ChannelName} has a {ERR_INVALID_FNE_REF}. {ERR_INVALID_CODEPLUG}. {ERR_SKIPPING_AUDIO}.");
                            channel.IsSelected = false;
                            selectedChannelsManager.RemoveSelectedChannel(channel);
                            return;
                        }

                        if (channel.PageState || (forHold && channel.HoldState) || primaryChannel != null)
                        {
                            byte[] pcmData;

                            Task.Run(async () =>
                            {
                                using (var waveReader = new WaveFileReader(filePath))
                                {
                                    if (waveReader.WaveFormat.Encoding != WaveFormatEncoding.Pcm ||
                                        waveReader.WaveFormat.SampleRate != 8000 ||
                                        waveReader.WaveFormat.BitsPerSample != 16 ||
                                        waveReader.WaveFormat.Channels != 1)
                                    {
                                        MessageBox.Show("The alert tone must be PCM 16-bit, Mono, 8000Hz format.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                                        return;
                                    }

                                    using (MemoryStream ms = new MemoryStream())
                                    {
                                        waveReader.CopyTo(ms);
                                        pcmData = ms.ToArray();
                                    }
                                }

                                int chunkSize = 1600;
                                int totalChunks = (pcmData.Length + chunkSize - 1) / chunkSize;

                                if (pcmData.Length % chunkSize != 0)
                                {
                                    byte[] paddedData = new byte[totalChunks * chunkSize];
                                    Buffer.BlockCopy(pcmData, 0, paddedData, 0, pcmData.Length);
                                    pcmData = paddedData;
                                }

                                Task.Run(() =>
                                {
                                    audioManager.AddTalkgroupStream(cpgChannel.Tgid, pcmData);
                                });

                                DateTime startTime = DateTime.UtcNow;

                                if (channel.TxStreamId != 0)
                                    Log.WriteWarning($"{channel.ChannelName} CHANNEL still had a TxStreamId? This shouldn't happen.");

                                channel.TxStreamId = fne.NewStreamId();
                                Log.WriteLine($"({system.Name}) {channel.ChannelMode.ToUpperInvariant()} Traffic *ALRT TONE      * TGID {channel.DstId} [STREAM ID {channel.TxStreamId}]");
                                channel.VolumeMeterLevel = 0;

                                for (int i = 0; i < totalChunks; i++)
                                {
                                    int offset = i * chunkSize;
                                    byte[] chunk = new byte[chunkSize];
                                    Buffer.BlockCopy(pcmData, offset, chunk, 0, chunkSize);

                                    channel.chunkedPCM = AudioConverter.SplitToChunks(chunk);

                                    foreach (byte[] audioChunk in channel.chunkedPCM)
                                    {
                                        if (audioChunk.Length == PCM_SAMPLES_LENGTH)
                                        {
                                            if (cpgChannel.GetChannelMode() == Codeplug.ChannelMode.P25)
                                                P25EncodeAudioFrame(audioChunk, fne, channel, cpgChannel, system);
                                            else if (cpgChannel.GetChannelMode() == Codeplug.ChannelMode.DMR)
                                                DMREncodeAudioFrame(audioChunk, fne, channel, cpgChannel, system);
                                        }
                                    }

                                    DateTime nextPacketTime = startTime.AddMilliseconds((i + 1) * 100);
                                    TimeSpan waitTime = nextPacketTime - DateTime.UtcNow;

                                    if (waitTime.TotalMilliseconds > 0)
                                        await Task.Delay(waitTime);
                                }

                                double totalDurationMs = ((double)pcmData.Length / 16000) + 250;
                                await Task.Delay((int)totalDurationMs + 3000);

                                fne.SendP25TDU(uint.Parse(system.Rid), uint.Parse(cpgChannel.Tgid), false, true);

                                ResetChannel(channel);

                                Dispatcher.Invoke(() =>
                                {
                                    if (forHold)
                                        channel.PttButton.Background = ChannelBox.GRAY_GRADIENT;
                                    else
                                        channel.PageState = false;
                                });
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to process alert tone: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    Log.StackTrace(ex, false);
                }
            }
            else
                MessageBox.Show("Alert file not set or file not found.", "Alert", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        /// <summary>
        /// Updates the text color of all tab headers based on dark mode setting
        /// </summary>
        private void UpdateTabTextColors()
        {
            Brush textColor = settingsManager.DarkMode ? Brushes.White : Brushes.Black;
            
            foreach (var kvp in tabHeaders)
            {
                StackPanel headerPanel = kvp.Value;
                foreach (UIElement child in headerPanel.Children)
                {
                    if (child is TextBlock textBlock)
                    {
                        textBlock.Foreground = textColor;
                    }
                }
            }
        }

        /// <summary>
        /// Handles tab selection changes to update background colors
        /// </summary>
        private void ResourceTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateTabSelectedBackground();
        }

        /// <summary>
        /// Updates the selected tab background color based on dark mode setting
        /// </summary>
        private void UpdateTabSelectedBackground()
        {
            // Ensure all tabs have the style applied
            foreach (TabItem tab in resourceTabs.Items)
            {
                if (tab.Style == null && resourceTabs.Resources["TabItemStyle"] is Style tabStyle)
                    tab.Style = tabStyle;

                // Force update of selected tab background
                if (tab.IsSelected)
                {
                    tab.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#888888"));
                }
                else
                {
                    // Clear background for unselected tabs to let style handle it
                    tab.ClearValue(TabItem.BackgroundProperty);
                }
            }
        }

        /// <summary>
        /// Applies the current background (user-defined or default) to all tab canvases
        /// </summary>
        private void ApplyCurrentBackgroundToAllTabs()
        {
            BitmapImage bg = new BitmapImage();
            ImageBrush backgroundBrush;

            // Check if we have a user defined background
            if (settingsManager.UserBackgroundImage != null && File.Exists(settingsManager.UserBackgroundImage))
            {
                bg.BeginInit();
                bg.UriSource = new Uri(settingsManager.UserBackgroundImage);
                bg.EndInit();
                backgroundBrush = new ImageBrush(bg) { Stretch = Stretch.UniformToFill };
            }
            else
            {
                // Use default background based on dark mode
                bg.BeginInit();
                if (settingsManager.DarkMode)
                    bg.UriSource = new Uri($"{URI_RESOURCE_PATH}/Assets/bg_main_hd_dark.png");
                else
                    bg.UriSource = new Uri($"{URI_RESOURCE_PATH}/Assets/bg_main_hd_light.png");
                bg.EndInit();
                backgroundBrush = new ImageBrush(bg) { Stretch = Stretch.UniformToFill };
            }

            // Update all tab canvases with the background
            foreach (var canvas in tabCanvases.Values)
            {
                canvas.Background = backgroundBrush;
            }

            // Also update the original channelsCanvas if it exists and isn't already in tabCanvases
            if (channelsCanvas != null && !tabCanvases.ContainsValue(channelsCanvas))
            {
                channelsCanvas.Background = backgroundBrush;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        private void UpdateBackground()
        {
            // set the UI theme
            PaletteHelper paletteHelper = new PaletteHelper();
            Theme theme = paletteHelper.GetTheme();

            if (settingsManager.DarkMode)
                theme.SetBaseTheme(BaseTheme.Dark);
            else
                theme.SetBaseTheme(BaseTheme.Light);

            paletteHelper.SetTheme(theme);

            // Update tab text colors and selected background based on dark mode
            UpdateTabTextColors();
            UpdateTabSelectedBackground();

            BitmapImage bg = new BitmapImage();

            // do we have a user defined background?
            if (settingsManager.UserBackgroundImage != null)
            {
                // does the file exist?
                if (File.Exists(settingsManager.UserBackgroundImage))
                {
                    bg.BeginInit();
                    bg.UriSource = new Uri(settingsManager.UserBackgroundImage);
                    bg.EndInit();

                    // Update the original canvas background
                    channelsCanvasBg.ImageSource = bg;
                    
                    // Update all tab canvases with the same background
                    ImageBrush backgroundBrush = new ImageBrush(bg) { Stretch = Stretch.UniformToFill };
                    foreach (var canvas in tabCanvases.Values)
                    {
                        canvas.Background = backgroundBrush;
                    }
                    
                    // Also update the original channelsCanvas if it exists and isn't already in tabCanvases
                    if (channelsCanvas != null && !tabCanvases.ContainsValue(channelsCanvas))
                    {
                        channelsCanvas.Background = backgroundBrush;
                    }
                    
                    return;
                }
            }

            bg.BeginInit();
            if (settingsManager.DarkMode)
                bg.UriSource = new Uri($"{URI_RESOURCE_PATH}/Assets/bg_main_hd_dark.png");
            else
                bg.UriSource = new Uri($"{URI_RESOURCE_PATH}/Assets/bg_main_hd_light.png");
            bg.EndInit();

            // Update the original canvas background
            channelsCanvasBg.ImageSource = bg;
            
            // Update all tab canvases with the same background
            ImageBrush defaultBrush = new ImageBrush(bg) { Stretch = Stretch.UniformToFill };
            foreach (var canvas in tabCanvases.Values)
            {
                canvas.Background = defaultBrush;
            }
            
            // Also update the original channelsCanvas if it exists and isn't already in tabCanvases
            if (channelsCanvas != null && !tabCanvases.ContainsValue(channelsCanvas))
            {
                channelsCanvas.Background = defaultBrush;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void OnHoldTimerElapsed(object sender, ElapsedEventArgs e)
        {
            foreach (ChannelBox channel in selectedChannelsManager.GetSelectedChannels())
            {
                if (channel.SystemName == PLAYBACKSYS || channel.ChannelName == PLAYBACKCHNAME || channel.DstId == PLAYBACKTG)
                    continue;

                Codeplug.System system = Codeplug.GetSystemForChannel(channel.ChannelName);
                Codeplug.Channel cpgChannel = Codeplug.GetChannelByName(channel.ChannelName);
                PeerSystem handler = fneSystemManager.GetFneSystem(system.Name);

                if (channel.HoldState && !channel.IsReceiving && !channel.PttState && !channel.PageState)
                {
                    handler.SendP25TDU(uint.Parse(system.Rid), uint.Parse(cpgChannel.Tgid), true);
                    await Task.Delay(1000);

                    SendAlertTone(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Audio/hold.wav"), true);
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="e"></param>
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            isShuttingDown = true;

            // stop maintainence task
            if (maintainenceTask != null)
            {
                maintainenceCancelToken.Cancel();

                try
                {
                    maintainenceTask.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException) { /* stub */ }
                finally
                {
                    maintainenceCancelToken.Dispose();
                }
            }

            waveIn.StopRecording();

            fneSystemManager.ClearAll();

            if (!noSaveSettingsOnClose)
            {
                if (WindowState == WindowState.Maximized)
                {
                    settingsManager.Maximized = true;
                    if (settingsManager.SnapCallHistoryToWindow)
                        menuSnapCallHistory.IsChecked = false;
                }

                settingsManager.SaveSettings();
            }

            base.OnClosing(e);
            Application.Current.Shutdown();
        }

        /// <summary>
        /// Internal maintainence routine.
        /// </summary>
        private async void Maintainence()
        {
            CancellationToken ct = maintainenceCancelToken.Token;
            while (!isShuttingDown)
            {
                foreach (ChannelBox channel in selectedChannelsManager.GetSelectedChannels())
                {
                    if (channel.SystemName == PLAYBACKSYS || channel.ChannelName == PLAYBACKCHNAME || channel.DstId == PLAYBACKTG)
                        continue;

                    Codeplug.System system = Codeplug.GetSystemForChannel(channel.ChannelName);
                    if (system == null)
                        continue;

                    Codeplug.Channel cpgChannel = Codeplug.GetChannelByName(channel.ChannelName);
                    if (cpgChannel == null)
                        continue;

                    PeerSystem fne = fneSystemManager.GetFneSystem(system.Name);
                    if (fne == null)
                        continue;

                    // check if the channel is stuck reporting Rx
                    if (channel.IsReceiving)
                    {
                        DateTime now = DateTime.Now;
                        TimeSpan dt = now - channel.LastPktTime;
                        if (dt.TotalMilliseconds > 2000) // 2 seconds is more then enough time -- the interpacket time for P25 is ~180ms and DMR is ~60ms
                        {
                            Log.WriteLine($"({system.Name}) P25D: Traffic *CALL TIMEOUT   * TGID {channel.DstId} ALGID {channel.algId} KID {channel.kId}");
                            Dispatcher.Invoke(() =>
                            {
                                channel.IsReceiving = false;
                                channel.PeerId = 0;
                                channel.RxStreamId = 0;

                                channel.Background = ChannelBox.BLUE_GRADIENT;
                                channel.VolumeMeterLevel = 0;
                                
                                // Update tab audio indicator
                                UpdateTabAudioIndicatorForChannel(channel);
                            });
                        }
                    }
                }

                try
                {
                    await Task.Delay(1000, ct);
                }
                catch (TaskCanceledException) { /* stub */ }
            }
        }

        /** NAudio Events */

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void WaveIn_RecordingStopped(object sender, EventArgs e)
        {
            /* stub */
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void WaveIn_DataAvailable(object sender, WaveInEventArgs e)
        {
            bool isAnyTgOn = false;
            if (isShuttingDown)
                return;

            foreach (ChannelBox channel in selectedChannelsManager.GetSelectedChannels())
            {
                if (channel.SystemName == PLAYBACKSYS || channel.ChannelName == PLAYBACKCHNAME || channel.DstId == PLAYBACKTG)
                    continue;

                Codeplug.System system = Codeplug.GetSystemForChannel(channel.ChannelName);
                if (system == null)
                {
                    Log.WriteLine($"{channel.ChannelName} refers to an {INVALID_SYSTEM} {channel.SystemName}. {ERR_INVALID_CODEPLUG}. {ERR_SKIPPING_AUDIO}.");
                    channel.IsSelected = false;
                    selectedChannelsManager.RemoveSelectedChannel(channel);
                    continue;
                }

                Codeplug.Channel cpgChannel = Codeplug.GetChannelByName(channel.ChannelName);
                if (cpgChannel == null)
                {
                    Log.WriteLine($"{channel.ChannelName} refers to an {INVALID_CODEPLUG_CHANNEL}. {ERR_INVALID_CODEPLUG}. {ERR_SKIPPING_AUDIO}.");
                    channel.IsSelected = false;
                    selectedChannelsManager.RemoveSelectedChannel(channel);
                    continue;
                }

                PeerSystem fne = fneSystemManager.GetFneSystem(system.Name);
                if (fne == null)
                {
                    Log.WriteLine($"{channel.ChannelName} has a {ERR_INVALID_FNE_REF}. {ERR_INVALID_CODEPLUG}. {ERR_SKIPPING_AUDIO}.");
                    channel.IsSelected = false;
                    selectedChannelsManager.RemoveSelectedChannel(channel);
                    continue;
                }

                // is the channel selected and in a PTT state?
                if (channel.IsSelected && channel.PttState)
                {
                    isAnyTgOn = true;
                    Task.Run(() =>
                    {
                        channel.chunkedPCM = AudioConverter.SplitToChunks(e.Buffer);
                        foreach (byte[] chunk in channel.chunkedPCM)
                        {
                            if (chunk.Length == PCM_SAMPLES_LENGTH)
                            {
                                if (cpgChannel.GetChannelMode() == Codeplug.ChannelMode.P25)
                                    P25EncodeAudioFrame(chunk, fne, channel, cpgChannel, system);
                                else if (cpgChannel.GetChannelMode() == Codeplug.ChannelMode.DMR)
                                    DMREncodeAudioFrame(chunk, fne, channel, cpgChannel, system);
                            }
                            else
                                Log.WriteLine("bad sample length: " + chunk.Length);
                        }
                    });
                }
            }

            if (playbackChannelBox != null && isAnyTgOn && playbackChannelBox.IsSelected)
                audioManager.AddTalkgroupStream(PLAYBACKTG, e.Buffer);
        }

        /** WPF Window Events */

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// <exception cref="NotImplementedException"></exception>
        private void MainWindow_LocationChanged(object sender, EventArgs e)
        {
            if (settingsManager.SnapCallHistoryToWindow && callHistoryWindow.Visibility == Visibility.Visible && 
                WindowState != WindowState.Maximized)
            {
                callHistoryWindow.Left = Left + ActualWidth + 5;
                callHistoryWindow.Top = Top;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// <exception cref="NotImplementedException"></exception>
        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            const double widthOffset = 16;
            const double heightOffset = 115;

            if (!windowLoaded)
                return;

            if (ActualWidth > channelsCanvas.ActualWidth)
            {
                channelsCanvas.Width = ActualWidth;
                canvasScrollViewer.Width = ActualWidth;
            }
            else
                canvasScrollViewer.Width = Width - widthOffset;

            if (ActualHeight > channelsCanvas.ActualHeight)
            {
                channelsCanvas.Height = ActualHeight;
                canvasScrollViewer.Height = ActualHeight;
            }
            else
                canvasScrollViewer.Height = Height - heightOffset;

            foreach (KeyValuePair<TabItem, ScrollViewer> kvp in tabScrollViewers)
            {
                if (ActualWidth > channelsCanvas.ActualWidth)
                    kvp.Value.Width = ActualWidth;
                else
                    kvp.Value.Width = Width - widthOffset;

                if (ActualHeight > channelsCanvas.ActualHeight)
                    kvp.Value.Height = ActualHeight;
                else
                    kvp.Value.Height = Height - heightOffset;
            }

            foreach (KeyValuePair<TabItem, Canvas> kvp in tabCanvases)
            {
                if (ActualWidth > channelsCanvas.ActualWidth)
                    kvp.Value.Width = ActualWidth;

                if (ActualHeight > channelsCanvas.ActualHeight)
                    kvp.Value.Height = ActualHeight;
            }

            if (WindowState == WindowState.Maximized)
                ResizeCanvasToWindow_Click(sender, e);
            else
                settingsManager.Maximized = false;

            if (settingsManager.SnapCallHistoryToWindow && callHistoryWindow.Visibility == Visibility.Visible && 
                WindowState != WindowState.Maximized)
            {
                callHistoryWindow.Height = ActualHeight;
                callHistoryWindow.Left = Left + ActualWidth + 5;
                callHistoryWindow.Top = Top;
            }

            settingsManager.CanvasWidth = channelsCanvas.ActualWidth;
            settingsManager.CanvasHeight = channelsCanvas.ActualHeight;

            settingsManager.WindowWidth = ActualWidth;
            settingsManager.WindowHeight = ActualHeight;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            const double widthOffset = 16;
            const double heightOffset = 115;

            // set PTT toggle mode (this must be done before channel widgets are defined)
            menuToggleLockWidgets.IsChecked = settingsManager.LockWidgets;
            menuSnapCallHistory.IsChecked = settingsManager.SnapCallHistoryToWindow;
            menuTogglePTTMode.IsChecked = settingsManager.TogglePTTMode;
            menuToggleGlobalPTTMode.IsChecked = settingsManager.GlobalPTTKeysAllChannels;
            menuKeepWindowOnTop.IsChecked = settingsManager.KeepWindowOnTop;

            if (!string.IsNullOrEmpty(settingsManager.LastCodeplugPath) && File.Exists(settingsManager.LastCodeplugPath))
                LoadCodeplug(settingsManager.LastCodeplugPath);
            else
                GenerateChannelWidgets();

            // set background configuration
            menuDarkMode.IsChecked = settingsManager.DarkMode;
            UpdateBackground();

            btnGlobalPttDefaultBg = btnGlobalPtt.Background;

            maintainenceTask = Task.Factory.StartNew(Maintainence, maintainenceCancelToken.Token);

            // set window configuration
            if (settingsManager.Maximized)
            {
                windowLoaded = true;
                WindowState = WindowState.Maximized;
                ResizeCanvasToWindow_Click(sender, e);
            }
            else
            {
                Width = settingsManager.WindowWidth;
                channelsCanvas.Width = settingsManager.CanvasWidth;
                if (settingsManager.CanvasWidth > settingsManager.WindowWidth)
                    canvasScrollViewer.Width = Width - widthOffset;
                else
                    canvasScrollViewer.Width = Width;

                Height = settingsManager.WindowHeight;
                channelsCanvas.Height = settingsManager.CanvasHeight;
                if (settingsManager.CanvasHeight > settingsManager.WindowHeight)
                    canvasScrollViewer.Height = Height - heightOffset;
                else
                    canvasScrollViewer.Height = Height;

                foreach (KeyValuePair<TabItem, ScrollViewer> kvp in tabScrollViewers)
                {
                    if (settingsManager.CanvasWidth > settingsManager.WindowWidth)
                        kvp.Value.Width = Width - widthOffset;
                    else
                        kvp.Value.Width = Width;

                    if (settingsManager.CanvasHeight > settingsManager.WindowHeight)
                        kvp.Value.Height = Height - heightOffset;
                    else
                        kvp.Value.Height = Height;
                }

                foreach (KeyValuePair<TabItem, Canvas> kvp in tabCanvases)
                {
                    kvp.Value.Width = settingsManager.CanvasWidth;
                    kvp.Value.Height = settingsManager.CanvasHeight;
                }

                windowLoaded = true;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
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

                settingsManager.LastCodeplugPath = openFileDialog.FileName;
                noSaveSettingsOnClose = false;
                settingsManager.SaveSettings();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void PageRID_Click(object sender, RoutedEventArgs e)
        {
            DigitalPageWindow pageWindow = new DigitalPageWindow(Codeplug.Systems);
            pageWindow.Owner = this;
            pageWindow.Title = "Page Subscriber";

            if (pageWindow.ShowDialog() == true)
            {
                // throw an error if the user does the dumb...
                if (pageWindow.DstId == string.Empty)
                {
                    MessageBox.Show($"Must supply a destination ID.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                PeerSystem fne = fneSystemManager.GetFneSystem(pageWindow.RadioSystem.Name);
                IOSP_CALL_ALRT callAlert = new IOSP_CALL_ALRT(uint.Parse(pageWindow.DstId), uint.Parse(pageWindow.RadioSystem.Rid));

                RemoteCallData callData = new RemoteCallData
                {
                    SrcId = uint.Parse(pageWindow.RadioSystem.Rid),
                    DstId = uint.Parse(pageWindow.DstId),
                    LCO = P25Defines.TSBK_IOSP_CALL_ALRT
                };

                byte[] tsbk = new byte[P25Defines.P25_TSBK_LENGTH_BYTES];

                callAlert.Encode(ref tsbk);

                fne.SendP25TSBK(callData, tsbk);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RadioCheckRID_Click(object sender, RoutedEventArgs e)
        {
            DigitalPageWindow pageWindow = new DigitalPageWindow(Codeplug.Systems);
            pageWindow.Owner = this;
            pageWindow.Title = "Radio Check Subscriber";

            if (pageWindow.ShowDialog() == true)
            {
                // throw an error if the user does the dumb...
                if (pageWindow.DstId == string.Empty)
                {
                    MessageBox.Show($"Must supply a destination ID.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                PeerSystem fne = fneSystemManager.GetFneSystem(pageWindow.RadioSystem.Name);
                IOSP_EXT_FNCT extFunc = new IOSP_EXT_FNCT((ushort)ExtendedFunction.CHECK, uint.Parse(pageWindow.RadioSystem.Rid), uint.Parse(pageWindow.DstId));

                RemoteCallData callData = new RemoteCallData
                {
                    SrcId = uint.Parse(pageWindow.RadioSystem.Rid),
                    DstId = uint.Parse(pageWindow.DstId),
                    LCO = P25Defines.TSBK_IOSP_EXT_FNCT
                };

                byte[] tsbk = new byte[P25Defines.P25_TSBK_LENGTH_BYTES];

                extFunc.Encode(ref tsbk);

                fne.SendP25TSBK(callData, tsbk);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void InhibitRID_Click(object sender, RoutedEventArgs e)
        {
            DigitalPageWindow pageWindow = new DigitalPageWindow(Codeplug.Systems);
            pageWindow.Owner = this;
            pageWindow.Title = "Inhibit Subscriber";

            if (pageWindow.ShowDialog() == true)
            {
                // throw an error if the user does the dumb...
                if (pageWindow.DstId == string.Empty)
                {
                    MessageBox.Show($"Must supply a destination ID.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                PeerSystem fne = fneSystemManager.GetFneSystem(pageWindow.RadioSystem.Name);
                IOSP_EXT_FNCT extFunc = new IOSP_EXT_FNCT((ushort)ExtendedFunction.INHIBIT, P25Defines.WUID_FNE, uint.Parse(pageWindow.DstId));

                RemoteCallData callData = new RemoteCallData
                {
                    SrcId = uint.Parse(pageWindow.RadioSystem.Rid),
                    DstId = uint.Parse(pageWindow.DstId),
                    LCO = P25Defines.TSBK_IOSP_EXT_FNCT
                };

                byte[] tsbk = new byte[P25Defines.P25_TSBK_LENGTH_BYTES];

                extFunc.Encode(ref tsbk);

                fne.SendP25TSBK(callData, tsbk);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UninhibitRID_Click(object sender, RoutedEventArgs e)
        {
            DigitalPageWindow pageWindow = new DigitalPageWindow(Codeplug.Systems);
            pageWindow.Owner = this;
            pageWindow.Title = "Uninhibit Subscriber";

            if (pageWindow.ShowDialog() == true)
            {
                // throw an error if the user does the dumb...
                if (pageWindow.DstId == string.Empty)
                {
                    MessageBox.Show($"Must supply a destination ID.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                PeerSystem fne = fneSystemManager.GetFneSystem(pageWindow.RadioSystem.Name);
                IOSP_EXT_FNCT extFunc = new IOSP_EXT_FNCT((ushort)ExtendedFunction.UNINHIBIT, P25Defines.WUID_FNE, uint.Parse(pageWindow.DstId));

                RemoteCallData callData = new RemoteCallData
                {
                    SrcId = uint.Parse(pageWindow.RadioSystem.Rid),
                    DstId = uint.Parse(pageWindow.DstId),
                    LCO = P25Defines.TSBK_IOSP_EXT_FNCT
                };

                byte[] tsbk = new byte[P25Defines.P25_TSBK_LENGTH_BYTES];

                extFunc.Encode(ref tsbk);

                fne.SendP25TSBK(callData, tsbk);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void ManualPage_Click(object sender, RoutedEventArgs e)
        {
            QuickCallPage pageWindow = new QuickCallPage();
            pageWindow.Owner = this;

            if (pageWindow.ShowDialog() == true)
            {
                foreach (ChannelBox channel in selectedChannelsManager.GetSelectedChannels())
                {
                    Codeplug.System system = Codeplug.GetSystemForChannel(channel.ChannelName);
                    if (system == null)
                    {
                        Log.WriteLine($"{channel.ChannelName} refers to an {INVALID_SYSTEM} {channel.SystemName}. {ERR_INVALID_CODEPLUG}.");
                        channel.IsSelected = false;
                        selectedChannelsManager.RemoveSelectedChannel(channel);
                        continue;
                    }

                    Codeplug.Channel cpgChannel = Codeplug.GetChannelByName(channel.ChannelName);
                    if (cpgChannel == null)
                    {
                        Log.WriteLine($"{channel.ChannelName} refers to an {INVALID_CODEPLUG_CHANNEL}. {ERR_INVALID_CODEPLUG}.");
                        channel.IsSelected = false;
                        selectedChannelsManager.RemoveSelectedChannel(channel);
                        continue;
                    }

                    PeerSystem fne = fneSystemManager.GetFneSystem(system.Name);
                    if (fne == null)
                    {
                        MessageBox.Show($"{channel.ChannelName} has a {ERR_INVALID_FNE_REF}. {PLEASE_RESTART_CONSOLE}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        channel.IsSelected = false;
                        selectedChannelsManager.RemoveSelectedChannel(channel);
                        continue;
                    }

                    // 
                    if (channel.PageState)
                    {
                        ToneGenerator generator = new ToneGenerator();

                        double toneADuration = 1.0;
                        double toneBDuration = 3.0;

                        byte[] toneA = generator.GenerateTone(Double.Parse(pageWindow.ToneA), toneADuration);
                        byte[] toneB = generator.GenerateTone(Double.Parse(pageWindow.ToneB), toneBDuration);

                        byte[] combinedAudio = new byte[toneA.Length + toneB.Length];
                        Buffer.BlockCopy(toneA, 0, combinedAudio, 0, toneA.Length);
                        Buffer.BlockCopy(toneB, 0, combinedAudio, toneA.Length, toneB.Length);

                        int chunkSize = PCM_SAMPLES_LENGTH;
                        int totalChunks = (combinedAudio.Length + chunkSize - 1) / chunkSize;

                        Task.Run(() =>
                        {
                            //_waveProvider.ClearBuffer();
                            audioManager.AddTalkgroupStream(cpgChannel.Tgid, combinedAudio);
                        });

                        await Task.Run(() =>
                        {
                            for (int i = 0; i < totalChunks; i++)
                            {
                                int offset = i * chunkSize;
                                int size = Math.Min(chunkSize, combinedAudio.Length - offset);

                                byte[] chunk = new byte[chunkSize];
                                Buffer.BlockCopy(combinedAudio, offset, chunk, 0, size);

                                if (chunk.Length == 320)
                                {
                                    if (cpgChannel.GetChannelMode() == Codeplug.ChannelMode.P25)
                                        P25EncodeAudioFrame(chunk, fne, channel, cpgChannel, system);
                                    else if (cpgChannel.GetChannelMode() == Codeplug.ChannelMode.DMR)
                                        DMREncodeAudioFrame(chunk, fne, channel, cpgChannel, system);
                                }
                            }
                        });

                        double totalDurationMs = (toneADuration + toneBDuration) * 1000 + 750;
                        await Task.Delay((int)totalDurationMs  + 4000);

                        fne.SendP25TDU(uint.Parse(system.Rid), uint.Parse(cpgChannel.Tgid), false, true);

                        Dispatcher.Invoke(() =>
                        {
                            //channel.PageState = false; // TODO: Investigate
                            channel.PageSelectButton.Background = ChannelBox.GRAY_GRADIENT;
                        });
                    }
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void TogglePTTMode_Click(object sender, RoutedEventArgs e)
        {
            if (!windowLoaded)
                return;

            settingsManager.TogglePTTMode = menuTogglePTTMode.IsChecked;
            settingsManager.SaveSettings();

            // update elements in all tab canvases
            foreach (var canvas in tabCanvases.Values)
            {
                foreach (UIElement child in canvas.Children)
                {
                    if (child is ChannelBox channelBox)
                        channelBox.PTTToggleMode = settingsManager.TogglePTTMode;
                }
            }
            
            // Also update the original channelsCanvas if it exists and isn't already in tabCanvases
            if (channelsCanvas != null && !tabCanvases.ContainsValue(channelsCanvas))
            {
                foreach (UIElement child in channelsCanvas.Children)
                {
                    if (child is ChannelBox channelBox)
                        channelBox.PTTToggleMode = settingsManager.TogglePTTMode;
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AudioSettings_Click(object sender, RoutedEventArgs e)
        {
            List<Codeplug.Channel> channels = Codeplug?.Zones.SelectMany(z => z.Channels).ToList() ?? new List<Codeplug.Channel>();

            AudioSettingsWindow audioSettingsWindow = new AudioSettingsWindow(settingsManager, audioManager, channels);
            audioSettingsWindow.ShowDialog();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ResetSettings_Click(object sender, RoutedEventArgs e)
        {
            var confirmResult = MessageBox.Show("Are you sure to wish to reset console settings?", "Reset Settings", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirmResult == MessageBoxResult.Yes)
            {
                MessageBox.Show("Settings will be reset after console restart.", "Reset Settings", MessageBoxButton.OK, MessageBoxImage.Information);
                noSaveSettingsOnClose = true;
                settingsManager.Reset();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SelectWidgets_Click(object sender, RoutedEventArgs e)
        {
            WidgetSelectionWindow widgetSelectionWindow = new WidgetSelectionWindow();
            widgetSelectionWindow.Owner = this;
            if (widgetSelectionWindow.ShowDialog() == true)
            {
                settingsManager.ShowSystemStatus = widgetSelectionWindow.ShowSystemStatus;
                settingsManager.ShowChannels = widgetSelectionWindow.ShowChannels;
                settingsManager.ShowAlertTones = widgetSelectionWindow.ShowAlertTones;

                GenerateChannelWidgets();
                if (!noSaveSettingsOnClose)
                    settingsManager.SaveSettings();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AddAlertTone_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "WAV Files (*.wav)|*.wav|All Files (*.*)|*.*",
                Title = "Select Alert Tone"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                string alertFilePath = openFileDialog.FileName;
                AlertTone alertTone = new AlertTone(alertFilePath);

                alertTone.OnAlertTone += SendAlertTone;

                // widget placement
                alertTone.MouseRightButtonDown += AlertTone_MouseRightButtonDown;
                alertTone.MouseRightButtonUp += AlertTone_MouseRightButtonUp;
                alertTone.MouseMove += AlertTone_MouseMove;

                // Get the current active tab's canvas
                TabItem currentTab = resourceTabs.SelectedItem as TabItem;
                Canvas targetCanvas = currentTab != null && tabCanvases.ContainsKey(currentTab) 
                    ? tabCanvases[currentTab] 
                    : channelsCanvas;

                if (settingsManager.AlertTonePositions.TryGetValue(alertFilePath, out var position))
                {
                    Canvas.SetLeft(alertTone, position.X);
                    Canvas.SetTop(alertTone, position.Y);
                }
                else
                {
                    Canvas.SetLeft(alertTone, 20);
                    Canvas.SetTop(alertTone, 20);
                }

                targetCanvas.Children.Add(alertTone);
                if (currentTab != null)
                    elementToTabMap[alertTone] = currentTab;
                    
                settingsManager.UpdateAlertTonePaths(alertFilePath);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OpenUserBackground_Click(object sender, RoutedEventArgs e)
        {
            if (!windowLoaded)
                return;

            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "JPEG Files (*.jpg)|*.jpg|PNG Files (*.png)|*.png|All Files (*.*)|*.*",
                Title = "Open User Background"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                settingsManager.UserBackgroundImage = openFileDialog.FileName;
                settingsManager.SaveSettings();
                UpdateBackground();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ToggleDarkMode_Click(object sender, RoutedEventArgs e)
        {
            if (!windowLoaded)
                return;

            settingsManager.DarkMode = menuDarkMode.IsChecked;
            UpdateBackground();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ToggleLockWidgets_Click(object sender, RoutedEventArgs e)
        {
            if (!windowLoaded)
                return;

            settingsManager.LockWidgets = !settingsManager.LockWidgets;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ToggleSnapCallHistory_Click(object sender, RoutedEventArgs e)
        {
            if (!windowLoaded)
                return;

            settingsManager.SnapCallHistoryToWindow = !settingsManager.SnapCallHistoryToWindow;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ToggleKeepWindowOnTop_Click(object sender, RoutedEventArgs e)
        {
            this.Topmost = !this.Topmost;

            if (!windowLoaded)
                return;

            settingsManager.KeepWindowOnTop = !settingsManager.KeepWindowOnTop;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ResizeCanvasToWindow_Click(object sender, RoutedEventArgs e)
        {
            const double widthOffset = 16;
            const double heightOffset = 115;
            
            Canvas activeCanvas = GetActiveCanvas();

            foreach (UIElement child in activeCanvas.Children)
            {
                double childLeft = Canvas.GetLeft(child) + child.RenderSize.Width;
                if (childLeft > ActualWidth)
                    Canvas.SetLeft(child, ActualWidth - (child.RenderSize.Width + widthOffset));
                double childBottom = Canvas.GetTop(child) + child.RenderSize.Height;
                if (childBottom > ActualHeight)
                    Canvas.SetTop(child, ActualHeight - (child.RenderSize.Height + heightOffset));
            }

            channelsCanvas.Width = ActualWidth;
            canvasScrollViewer.Width = ActualWidth;
            channelsCanvas.Height = ActualHeight;
            canvasScrollViewer.Height = ActualHeight;

            foreach (KeyValuePair<TabItem, ScrollViewer> kvp in tabScrollViewers)
            {
                kvp.Value.Width = ActualWidth;
                kvp.Value.Height = ActualHeight;
            }

            foreach (KeyValuePair<TabItem, Canvas> kvp in tabCanvases)
            {
                kvp.Value.Width = ActualWidth;
                kvp.Value.Height = ActualHeight;
            }

            settingsManager.CanvasWidth = ActualWidth;
            settingsManager.CanvasHeight = ActualHeight;

            settingsManager.WindowWidth = ActualWidth;
            settingsManager.WindowHeight = ActualHeight;
        }

        /** Widget Controls */

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ChannelBox_HoldChannelButtonClicked(object sender, ChannelBox e)
        {
            if (e.SystemName == PLAYBACKSYS || e.ChannelName == PLAYBACKCHNAME || e.DstId == PLAYBACKTG)
                return;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ChannelBox_PageButtonClicked(object sender, ChannelBox e)
        {
            if (e.SystemName == PLAYBACKSYS || e.ChannelName == PLAYBACKCHNAME || e.DstId == PLAYBACKTG)
                return;

            Codeplug.System system = Codeplug.GetSystemForChannel(e.ChannelName);
            if (system == null)
            {
                MessageBox.Show($"{e.ChannelName} refers to an {INVALID_SYSTEM} {e.SystemName}. {PLEASE_CHECK_CODEPLUG}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                e.IsSelected = false;
                selectedChannelsManager.RemoveSelectedChannel(e);
                return;
            }

            Codeplug.Channel cpgChannel = Codeplug.GetChannelByName(e.ChannelName);
            if (cpgChannel == null)
            {
                // bryanb: this should actually never happen...
                MessageBox.Show($"{e.ChannelName} refers to an {INVALID_CODEPLUG_CHANNEL}. {PLEASE_CHECK_CODEPLUG}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                e.IsSelected = false;
                selectedChannelsManager.RemoveSelectedChannel(e);
                return;
            }

            PeerSystem fne = fneSystemManager.GetFneSystem(system.Name);
            if (fne == null)
            {
                MessageBox.Show($"{e.ChannelName} has a {ERR_INVALID_FNE_REF}. {PLEASE_RESTART_CONSOLE}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                e.IsSelected = false;
                selectedChannelsManager.RemoveSelectedChannel(e);
                return;
            }

            if (e.PageState)
                fne.SendP25TDU(uint.Parse(system.Rid), uint.Parse(cpgChannel.Tgid), true);
            else
                fne.SendP25TDU(uint.Parse(system.Rid), uint.Parse(cpgChannel.Tgid), false, true);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ChannelBox_PTTButtonClicked(object sender, ChannelBox e)
        {
            if (e.SystemName == PLAYBACKSYS || e.ChannelName == PLAYBACKCHNAME || e.DstId == PLAYBACKTG)
                return;

            Codeplug.System system = Codeplug.GetSystemForChannel(e.ChannelName);
            if (system == null)
            {
                MessageBox.Show($"{e.ChannelName} refers to an {INVALID_SYSTEM} {e.SystemName}. {PLEASE_CHECK_CODEPLUG}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                e.IsSelected = false;
                selectedChannelsManager.RemoveSelectedChannel(e);
                return;
            }

            Codeplug.Channel cpgChannel = Codeplug.GetChannelByName(e.ChannelName);
            if (cpgChannel == null)
            {
                // bryanb: this should actually never happen...
                MessageBox.Show($"{e.ChannelName} refers to an {INVALID_CODEPLUG_CHANNEL}. {PLEASE_CHECK_CODEPLUG}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                e.IsSelected = false;
                selectedChannelsManager.RemoveSelectedChannel(e);
                return;
            }

            PeerSystem fne = fneSystemManager.GetFneSystem(system.Name);
            if (fne == null)
            {
                MessageBox.Show($"{e.ChannelName} has a {ERR_INVALID_FNE_REF}. {PLEASE_RESTART_CONSOLE}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                e.IsSelected = false;
                selectedChannelsManager.RemoveSelectedChannel(e);
                return;
            }

            if (!e.IsSelected)
                return;

            FneUtils.Memset(e.mi, 0x00, P25Defines.P25_MI_LENGTH);

            uint srcId = uint.Parse(system.Rid);
            uint dstId = uint.Parse(cpgChannel.Tgid);

            if (e.PttState)
            {
                if (e.TxStreamId != 0)
                    Log.WriteWarning($"{e.ChannelName} CHANNEL still had a TxStreamId? This shouldn't happen.");

                e.TxStreamId = fne.NewStreamId();
                Log.WriteLine($"({system.Name}) {e.ChannelMode.ToUpperInvariant()} Traffic *CALL START     * SRC_ID {srcId} TGID {dstId} [STREAM ID {e.TxStreamId}]");
                e.VolumeMeterLevel = 0;
                if (cpgChannel.GetChannelMode() == Codeplug.ChannelMode.P25)
                    fne.SendP25TDU(srcId, dstId, true);
            }
            else
            {
                e.VolumeMeterLevel = 0;
                Log.WriteLine($"({system.Name}) {e.ChannelMode.ToUpperInvariant()} Traffic *CALL END       * SRC_ID {srcId} TGID {dstId} [STREAM ID {e.TxStreamId}]");
                if (cpgChannel.GetChannelMode() == Codeplug.ChannelMode.P25)
                    fne.SendP25TDU(srcId, dstId, false, true);
                else if (cpgChannel.GetChannelMode() == Codeplug.ChannelMode.DMR)
                    fne.SendDMRTerminator(srcId, dstId, 1, e.dmrSeqNo, e.dmrN, e.embeddedData);

                // reset values
                ResetChannel(e);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// <exception cref="NotImplementedException"></exception>
        private void ChannelBox_PTTButtonPressed(object sender, ChannelBox e)
        {
            if (e.SystemName == PLAYBACKSYS || e.ChannelName == PLAYBACKCHNAME || e.DstId == PLAYBACKTG)
                return;

            if (!e.PttState)
            {
                Codeplug.System system = Codeplug.GetSystemForChannel(e.ChannelName);
                if (system == null)
                {
                    MessageBox.Show($"{e.ChannelName} refers to an {INVALID_SYSTEM} {e.SystemName}. {PLEASE_CHECK_CODEPLUG}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    e.IsSelected = false;
                    selectedChannelsManager.RemoveSelectedChannel(e);
                    return;
                }

                Codeplug.Channel cpgChannel = Codeplug.GetChannelByName(e.ChannelName);
                if (cpgChannel == null)
                {
                    // bryanb: this should actually never happen...
                    MessageBox.Show($"{e.ChannelName} refers to an {INVALID_CODEPLUG_CHANNEL}. {PLEASE_CHECK_CODEPLUG}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    e.IsSelected = false;
                    selectedChannelsManager.RemoveSelectedChannel(e);
                    return;
                }

                PeerSystem fne = fneSystemManager.GetFneSystem(system.Name);
                if (fne == null)
                {
                    MessageBox.Show($"{e.ChannelName} has a {ERR_INVALID_FNE_REF}. {PLEASE_RESTART_CONSOLE}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    e.IsSelected = false;
                    selectedChannelsManager.RemoveSelectedChannel(e);
                    return;
                }

                if (!e.IsSelected)
                    return;

                FneUtils.Memset(e.mi, 0x00, P25Defines.P25_MI_LENGTH);

                uint srcId = uint.Parse(system.Rid);
                uint dstId = uint.Parse(cpgChannel.Tgid);

                if (e.TxStreamId != 0)
                    Log.WriteWarning($"{e.ChannelName} CHANNEL still had a TxStreamId? This shouldn't happen.");

                e.TxStreamId = fne.NewStreamId();
                Log.WriteLine($"({system.Name}) {e.ChannelMode.ToUpperInvariant()} Traffic *CALL START     * SRC_ID {srcId} TGID {dstId} [STREAM ID {e.TxStreamId}]");
                e.VolumeMeterLevel = 0;
                if (cpgChannel.GetChannelMode() == Codeplug.ChannelMode.P25)
                    fne.SendP25TDU(srcId, dstId, true);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// <exception cref="NotImplementedException"></exception>
        private void ChannelBox_PTTButtonReleased(object sender, ChannelBox e)
        {
            if (e.SystemName == PLAYBACKSYS || e.ChannelName == PLAYBACKCHNAME || e.DstId == PLAYBACKTG)
                return;

            if (e.PttState)
            {
                Codeplug.System system = Codeplug.GetSystemForChannel(e.ChannelName);
                if (system == null)
                {
                    MessageBox.Show($"{e.ChannelName} refers to an {INVALID_SYSTEM} {e.SystemName}. {PLEASE_CHECK_CODEPLUG}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    e.IsSelected = false;
                    selectedChannelsManager.RemoveSelectedChannel(e);
                    return;
                }

                Codeplug.Channel cpgChannel = Codeplug.GetChannelByName(e.ChannelName);
                if (cpgChannel == null)
                {
                    // bryanb: this should actually never happen...
                    MessageBox.Show($"{e.ChannelName} refers to an {INVALID_CODEPLUG_CHANNEL}. {PLEASE_CHECK_CODEPLUG}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    e.IsSelected = false;
                    selectedChannelsManager.RemoveSelectedChannel(e);
                    return;
                }

                PeerSystem fne = fneSystemManager.GetFneSystem(system.Name);
                if (fne == null)
                {
                    MessageBox.Show($"{e.ChannelName} has a {ERR_INVALID_FNE_REF}. {PLEASE_RESTART_CONSOLE}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    e.IsSelected = false;
                    selectedChannelsManager.RemoveSelectedChannel(e);
                    return;
                }

                if (!e.IsSelected)
                    return;

                uint srcId = uint.Parse(system.Rid);
                uint dstId = uint.Parse(cpgChannel.Tgid);

                Log.WriteLine($"({system.Name}) {e.ChannelMode.ToUpperInvariant()} Traffic *CALL END       * SRC_ID {srcId} TGID {dstId} [STREAM ID {e.TxStreamId}]");
                e.VolumeMeterLevel = 0;
                if (cpgChannel.GetChannelMode() == Codeplug.ChannelMode.P25)
                    fne.SendP25TDU(srcId, dstId, false, true);
                else if (cpgChannel.GetChannelMode() == Codeplug.ChannelMode.DMR)
                    fne.SendDMRTerminator(srcId, dstId, 1, e.dmrSeqNo, e.dmrN, e.embeddedData);

                ResetChannel(e);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ChannelBox_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (settingsManager.LockWidgets || !(sender is UIElement element))
                return;

            draggedElement = element;
            Canvas targetCanvas = GetCanvasForElement(element);
            startPoint = e.GetPosition(targetCanvas);
            offsetX = startPoint.X - Canvas.GetLeft(draggedElement);
            offsetY = startPoint.Y - Canvas.GetTop(draggedElement);
            isDragging = true;

            Cursor = Cursors.ScrollAll;

            element.CaptureMouse();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ChannelBox_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (settingsManager.LockWidgets || !isDragging || draggedElement == null)
                return;

            Cursor = Cursors.Arrow;

            isDragging = false;
            if (draggedElement != null)
            {
                draggedElement.ReleaseMouseCapture();
                draggedElement = null;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ChannelBox_MouseMove(object sender, MouseEventArgs e)
        {
            if (settingsManager.LockWidgets || !isDragging || draggedElement == null) 
                return;

            // Get the canvas that contains the dragged element
            Canvas targetCanvas = GetCanvasForElement(draggedElement);
            if (targetCanvas == null)
                return;

            Point currentPosition = e.GetPosition(targetCanvas);

            // Calculate the new position with snapping to the grid
            double newLeft = Math.Round((currentPosition.X - offsetX) / GridSize) * GridSize;
            double newTop = Math.Round((currentPosition.Y - offsetY) / GridSize) * GridSize;

            // Get the ScrollViewer parent to get proper viewport dimensions
            ScrollViewer scrollViewer = targetCanvas.Parent as ScrollViewer;
            double maxWidth = scrollViewer != null ? Math.Max(targetCanvas.ActualWidth, scrollViewer.ViewportWidth) : targetCanvas.ActualWidth;
            double maxHeight = scrollViewer != null ? Math.Max(targetCanvas.ActualHeight, scrollViewer.ViewportHeight) : targetCanvas.ActualHeight;
            
            // If canvas height is 0 or very small, use a large default to allow free vertical movement
            if (maxHeight < 100)
            {
                maxHeight = 10000; // Allow free vertical movement
            }
            if (maxWidth < 100)
            {
                maxWidth = 10000; // Allow free horizontal movement
            }

            // Ensure the box stays within canvas bounds (but allow free movement if canvas is small)
            newLeft = Math.Max(0, Math.Min(newLeft, maxWidth - draggedElement.RenderSize.Width));
            newTop = Math.Max(0, Math.Min(newTop, maxHeight - draggedElement.RenderSize.Height));

            // Apply snapped position
            Canvas.SetLeft(draggedElement, newLeft);
            Canvas.SetTop(draggedElement, newTop);

            // Save the new position if it's a ChannelBox
            if (draggedElement is ChannelBox channelBox)
                settingsManager.UpdateChannelPosition(channelBox.ChannelName, newLeft, newTop);
        }
        
        /// <summary>
        /// Gets the canvas that contains the given element
        /// </summary>
        private Canvas GetCanvasForElement(UIElement element)
        {
            // Check if element is mapped to a tab
            if (elementToTabMap.TryGetValue(element, out TabItem tab) && tabCanvases.ContainsKey(tab))
            {
                return tabCanvases[tab];
            }
            
            // Check all tab canvases
            foreach (var kvp in tabCanvases)
            {
                if (kvp.Value.Children.Contains(element))
                {
                    elementToTabMap[element] = kvp.Key;
                    return kvp.Value;
                }
            }
            
            // Fallback to original canvas
            if (channelsCanvas.Children.Contains(element))
            {
                // Map to first tab if it exists
                if (resourceTabs.Items.Count > 0 && resourceTabs.Items[0] is TabItem firstTab)
                {
                    elementToTabMap[element] = firstTab;
                }
                return channelsCanvas;
            }
            
            return GetActiveCanvas();
        }

        /// <summary>
        /// Activates Global PTT after a click or keyboard shortcut
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SystemStatusBox_MouseRightButtonDown(object sender, MouseButtonEventArgs e) => ChannelBox_MouseRightButtonDown(sender, e);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SystemStatusBox_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (settingsManager.LockWidgets)
                return;

            if (sender is SystemStatusBox systemStatusBox)
            {
                double x = Canvas.GetLeft(systemStatusBox);
                double y = Canvas.GetTop(systemStatusBox);
                settingsManager.SystemStatusPositions[systemStatusBox.SystemName] = new ChannelPosition { X = x, Y = y };

                ChannelBox_MouseRightButtonUp(sender, e);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SystemStatusBox_MouseMove(object sender, MouseEventArgs e) => ChannelBox_MouseMove(sender, e);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AlertTone_MouseRightButtonDown(object sender, MouseButtonEventArgs e) => ChannelBox_MouseRightButtonDown(sender, e);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AlertTone_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (settingsManager.LockWidgets)
                return;

            if (sender is AlertTone alertTone)
            {
                double x = Canvas.GetLeft(alertTone);
                double y = Canvas.GetTop(alertTone);
                settingsManager.UpdateAlertTonePosition(alertTone.AlertFilePath, x, y);

                ChannelBox_MouseRightButtonUp(sender, e);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AlertTone_MouseMove(object sender, MouseEventArgs e) => ChannelBox_MouseMove(sender, e);

        /** WPF Ribbon Controls */

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void GlobalPTTActivate(object sender, RoutedEventArgs e)
        {
            if (globalPttState)
                await Task.Delay(500);

            ChannelBox primaryChannel = selectedChannelsManager.PrimaryChannel;

            if (primaryChannel != null)
            {
                Dispatcher.Invoke(() =>
                {
                    if (globalPttState)
                        btnGlobalPtt.Background = ChannelBox.RED_GRADIENT;
                    else
                        btnGlobalPtt.Background = btnGlobalPttDefaultBg;
                });
                
                primaryChannel.TriggerPTTState(globalPttState);

                return;
            }

            
            // Check for global PTT keys all preference, if not enabled, return early
            if (!settingsManager.GlobalPTTKeysAllChannels)
            {
                return;
            }

            foreach (ChannelBox channel in selectedChannelsManager.GetSelectedChannels())
            {
                if (channel.SystemName == PLAYBACKSYS || channel.ChannelName == PLAYBACKCHNAME || channel.DstId == PLAYBACKTG)
                    continue;

                Codeplug.System system = Codeplug.GetSystemForChannel(channel.ChannelName);
                if (system == null)
                {
                    Log.WriteLine($"{channel.ChannelName} refers to an {INVALID_SYSTEM} {channel.SystemName}. {ERR_INVALID_CODEPLUG}.");
                    channel.IsSelected = false;
                    selectedChannelsManager.RemoveSelectedChannel(channel);
                    continue;
                }

                Codeplug.Channel cpgChannel = Codeplug.GetChannelByName(channel.ChannelName);
                if (cpgChannel == null)
                {
                    Log.WriteLine($"{channel.ChannelName} refers to an {INVALID_CODEPLUG_CHANNEL}. {ERR_INVALID_CODEPLUG}.");
                    channel.IsSelected = false;
                    selectedChannelsManager.RemoveSelectedChannel(channel);
                    continue;
                }

                PeerSystem fne = fneSystemManager.GetFneSystem(system.Name);
                if (fne == null)
                {
                    Log.WriteLine($"{channel.ChannelName} has a {ERR_INVALID_FNE_REF}. {ERR_INVALID_CODEPLUG}.");
                    channel.IsSelected = false;
                    selectedChannelsManager.RemoveSelectedChannel(channel);
                    continue;
                }

                channel.TxStreamId = fne.NewStreamId();
                if (globalPttState)
                {
                    Dispatcher.Invoke(() =>
                    {
                        btnGlobalPtt.Background = ChannelBox.RED_GRADIENT;
                        channel.PttState = true;
                    });

                    fne.SendP25TDU(uint.Parse(system.Rid), uint.Parse(cpgChannel.Tgid), true);
                }
                else
                {
                    Dispatcher.Invoke(() =>
                    {
                        btnGlobalPtt.Background = btnGlobalPttDefaultBg;
                        channel.PttState = false;
                    });

                    fne.SendP25TDU(uint.Parse(system.Rid), uint.Parse(cpgChannel.Tgid), false, true);
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// <exception cref="NotImplementedException"></exception>
        private void btnGlobalPtt_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (settingsManager.TogglePTTMode)
                return;

            globalPttState = !globalPttState;

            GlobalPTTActivate(sender, e);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// <exception cref="NotImplementedException"></exception>
        private void btnGlobalPtt_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (settingsManager.TogglePTTMode)
            {
                globalPttState = !globalPttState;
                GlobalPTTActivate(sender, e);
            }
            else
            {
                globalPttState = true;
                GlobalPTTActivate(sender, e);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// <exception cref="NotImplementedException"></exception>
        private void btnGlobalPtt_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (settingsManager.TogglePTTMode)
                return;

            globalPttState = false;
            GlobalPTTActivate(sender, e);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btnAlert1_Click(object sender, RoutedEventArgs e)
        {
            Dispatcher.Invoke(() => {
                SendAlertTone(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Audio/alert1.wav"));
            });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btnAlert2_Click(object sender, RoutedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                SendAlertTone(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Audio/alert2.wav"));
            });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btnAlert3_Click(object sender, RoutedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                SendAlertTone(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Audio/alert3.wav"));
            });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            selectAll = !selectAll;
            
            // Iterate through all canvases (all tabs) to select/deselect all channels
            foreach (var canvas in GetAllCanvases())
            {
                foreach (ChannelBox channel in canvas.Children.OfType<ChannelBox>())
                {
                    if (channel.SystemName == PLAYBACKSYS || channel.ChannelName == PLAYBACKCHNAME || channel.DstId == PLAYBACKTG)
                        continue;

                    Codeplug.System system = Codeplug.GetSystemForChannel(channel.ChannelName);
                    if (system == null)
                    {
                        Log.WriteLine($"{channel.ChannelName} refers to an {INVALID_SYSTEM} {channel.SystemName}. {ERR_INVALID_CODEPLUG}.");
                        channel.IsSelected = false;
                        selectedChannelsManager.RemoveSelectedChannel(channel);
                        continue;
                    }

                    Codeplug.Channel cpgChannel = Codeplug.GetChannelByName(channel.ChannelName);
                    if (cpgChannel == null)
                    {
                        Log.WriteLine($"{channel.ChannelName} refers to an {INVALID_CODEPLUG_CHANNEL}. {ERR_INVALID_CODEPLUG}.");
                        channel.IsSelected = false;
                        selectedChannelsManager.RemoveSelectedChannel(channel);
                        continue;
                    }

                    channel.IsSelected = selectAll;
                    channel.Background = channel.IsSelected ? ChannelBox.BLUE_GRADIENT : ChannelBox.DARK_GRAY_GRADIENT;

                    if (channel.IsSelected)
                        selectedChannelsManager.AddSelectedChannel(channel);
                    else
                        selectedChannelsManager.RemoveSelectedChannel(channel);
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void KeyStatus_Click(object sender, RoutedEventArgs e)
        {
            KeyStatusWindow keyStatus = new KeyStatusWindow(Codeplug, this);
            keyStatus.Owner = this;
            keyStatus.Show();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CallHist_Click(object sender, RoutedEventArgs e)
        {
            callHistoryWindow.Owner = this;
            if (callHistoryWindow.Visibility == Visibility.Visible)
                callHistoryWindow.Hide();
            else
            {
                callHistoryWindow.Show();

                if (settingsManager.SnapCallHistoryToWindow && WindowState != WindowState.Maximized)
                {
                    if (ActualHeight > callHistoryWindow.Height)
                        callHistoryWindow.Height = ActualHeight;

                    callHistoryWindow.Left = Left + ActualWidth + 5;
                    callHistoryWindow.Top = Top;
                }
            }
        }

        /** fnecore Hooks / Helpers */

        /// <summary>
        /// Handler for FNE key responses.
        /// </summary>
        /// <param name="e"></param>
        public void KeyResponseReceived(KeyResponseEvent e)
        {
            //Log.WriteLine($"Message ID: {e.KmmKey.MessageId}");
            //Log.WriteLine($"Decrypt Info Format: {e.KmmKey.DecryptInfoFmt}");
            //Log.WriteLine($"Algorithm ID: {e.KmmKey.AlgId}");
            //Log.WriteLine($"Key ID: {e.KmmKey.KeyId}");
            //Log.WriteLine($"Keyset ID: {e.KmmKey.KeysetItem.KeysetId}");
            //Log.WriteLine($"Keyset Alg ID: {e.KmmKey.KeysetItem.AlgId}");
            //Log.WriteLine($"Keyset Key Length: {e.KmmKey.KeysetItem.KeyLength}");
            //Log.WriteLine($"Number of Keys: {e.KmmKey.KeysetItem.Keys.Count}");

            foreach (var key in e.KmmKey.KeysetItem.Keys)
            {
                //Log.WriteLine($"  Key Format: {key.KeyFormat}");
                //Log.WriteLine($"  SLN: {key.Sln}");
                //Log.WriteLine($"  Key ID: {key.KeyId}");
                //Log.WriteLine($"  Key Data: {BitConverter.ToString(key.GetKey())}");

                Dispatcher.Invoke(() =>
                {
                    foreach (ChannelBox channel in selectedChannelsManager.GetSelectedChannels())
                    {
                        if (channel.SystemName == PLAYBACKSYS || channel.ChannelName == PLAYBACKCHNAME || channel.DstId == PLAYBACKTG)
                            continue;

                        Codeplug.System system = Codeplug.GetSystemForChannel(channel.ChannelName);
                        if (system == null)
                        {
                            Log.WriteLine($"{channel.ChannelName} refers to an {INVALID_SYSTEM} {channel.SystemName}. {ERR_INVALID_CODEPLUG}.");
                            channel.IsSelected = false;
                            selectedChannelsManager.RemoveSelectedChannel(channel);
                            continue;
                        }

                        Codeplug.Channel cpgChannel = Codeplug.GetChannelByName(channel.ChannelName);
                        if (cpgChannel == null)
                        {
                            Log.WriteLine($"{channel.ChannelName} refers to an {INVALID_CODEPLUG_CHANNEL}. {ERR_INVALID_CODEPLUG}.");
                            channel.IsSelected = false;
                            selectedChannelsManager.RemoveSelectedChannel(channel);
                            continue;
                        }

                        ushort keyId = cpgChannel.GetKeyId();
                        byte algoId = cpgChannel.GetAlgoId();
                        KeysetItem receivedKey = e.KmmKey.KeysetItem;

                        if (keyId != 0 && algoId != 0 && keyId == key.KeyId && algoId == receivedKey.AlgId)
                            channel.Crypter.SetKey(key.KeyId, receivedKey.AlgId, key.GetKey());
                    }
                });
            }
        }

        /** Keyboard Shortcuts */

        /// <summary>
        /// Sets the global PTT keybind
        /// Hooks a listener to listen for a keypress, then saves that as the global PTT keybind
        /// Global PTT keybind is effectively the same as pressing the Global PTT button
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// <exception cref="NotImplementedException"></exception>
        private async void SetGlobalPTTKeybind(object sender, RoutedEventArgs e)
        {
            
            // Create and show a MessageBox with no buttons or standard close behavior
            Window messageBox = new Window
            {
                Width = 500,
                Height = 150,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ResizeMode = ResizeMode.NoResize,
                Topmost = true,
                Background = System.Windows.Media.Brushes.White,
                Content = new System.Windows.Controls.TextBlock
                {
                    Text = "Press any key to set the Global PTT shortcut...",
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    FontSize = 16,
                    FontWeight = System.Windows.FontWeights.Bold,
                }
            };

            // Center messageBox on the main window
            messageBox.Owner = this; // Set the current window as owner
            messageBox.WindowStartupLocation = WindowStartupLocation.CenterOwner;

            // Open and close the MessageBox after 500ms
            messageBox.Show();
            Keys keyPress = await keyboardManager.GetNextKeyPress();
            messageBox.Close();
            settingsManager.GlobalPTTShortcut = keyPress;
            InitializeKeyboardShortcuts();
            settingsManager.SaveSettings();
            MessageBox.Show("Global PTT shortcut set to " + keyPress.ToString(), "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// Initializes global keyboard shortcut listener
        /// </summary>
        private void InitializeKeyboardShortcuts()
        {
            var listeningKeys = new List<Keys> { settingsManager.GlobalPTTShortcut };
            keyboardManager.SetListenKeys(listeningKeys);
            // Clear event listener
            keyboardManager.OnKeyEvent -= KeyboardManagerOnKeyEvent;
            // Re-add listener
            keyboardManager.OnKeyEvent += KeyboardManagerOnKeyEvent;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="pressedKey"></param>
        /// <param name="state"></param>
        private void KeyboardManagerOnKeyEvent(Keys pressedKey,GlobalKeyboardHook.KeyboardState state)
        {
            if (pressedKey == settingsManager.GlobalPTTShortcut)
            {
                if(state is GlobalKeyboardHook.KeyboardState.KeyDown or GlobalKeyboardHook.KeyboardState.SysKeyDown)
                {
                    globalPttState = true;
                    GlobalPTTActivate(null, null);
                }
                else
                {
                    globalPttState = false;
                    GlobalPTTActivate(null, null);
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ToggleGlobalPTTAllChannels_Click(object sender, RoutedEventArgs e)
        {
            settingsManager.GlobalPTTKeysAllChannels = !settingsManager.GlobalPTTKeysAllChannels;
        }
    } // public partial class MainWindow : Window
} // namespace dvmconsole
