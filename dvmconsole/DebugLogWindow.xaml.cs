// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Desktop Dispatch Console
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Desktop Dispatch Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*/
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace dvmconsole
{
    /// <summary>
    /// Interaction logic for DebugLogWindow.xaml
    /// </summary>
    public partial class DebugLogWindow : Window
    {
        private const int MAX_VISIBLE_LINES = 500;
        private static readonly TimeSpan REFRESH_INTERVAL = TimeSpan.FromMilliseconds(100);

        private readonly Queue<string> visibleLines = new Queue<string>();
        private readonly object visibleLinesSync = new object();
        private readonly DispatcherTimer refreshTimer;
        private ScrollViewer logScrollViewer;
        private long bufferVersion;
        private long renderedVersion = -1;
        private bool isAutoScrollPaused;

        public DebugLogWindow()
        {
            InitializeComponent();

            refreshTimer = new DispatcherTimer
            {
                Interval = REFRESH_INTERVAL
            };
            refreshTimer.Tick += RefreshTimer_Tick;

            foreach (string line in Log.GetRecentLines())
                AddLineToBuffer(line);

            RenderLatestSnapshot(shouldAutoScroll: false);

            Loaded += DebugLogWindow_Loaded;
            Closed += DebugLogWindow_Closed;
            Log.LogLineWritten += Log_LogLineWritten;
            refreshTimer.Start();
        }

        private void DebugLogWindow_Loaded(object sender, RoutedEventArgs e)
        {
            logScrollViewer = FindScrollViewer(LogTextBox);
            LogTextBox.ScrollToEnd();
        }

        private void DebugLogWindow_Closed(object sender, EventArgs e)
        {
            refreshTimer.Stop();
            refreshTimer.Tick -= RefreshTimer_Tick;
            Log.LogLineWritten -= Log_LogLineWritten;
            Loaded -= DebugLogWindow_Loaded;
            Closed -= DebugLogWindow_Closed;
        }

        private void Log_LogLineWritten(string line)
        {
            AddLineToBuffer(line);
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            lock (visibleLinesSync)
            {
                visibleLines.Clear();
                bufferVersion++;
            }

            RenderLatestSnapshot(shouldAutoScroll: false);
        }

        private void RefreshTimer_Tick(object sender, EventArgs e)
        {
            if (isAutoScrollPaused)
                return;

            if (renderedVersion == bufferVersion)
                return;

            bool shouldAutoScroll = IsNearBottom();
            RenderLatestSnapshot(shouldAutoScroll);
        }

        private void PauseAutoScrollCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            isAutoScrollPaused = true;
        }

        private void PauseAutoScrollCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            isAutoScrollPaused = false;
            RenderLatestSnapshot(shouldAutoScroll: true);
        }

        private void AddLineToBuffer(string line)
        {
            lock (visibleLinesSync)
            {
                while (visibleLines.Count >= MAX_VISIBLE_LINES)
                    visibleLines.Dequeue();

                visibleLines.Enqueue(line);
                bufferVersion++;
            }
        }

        private void RenderLatestSnapshot(bool shouldAutoScroll)
        {
            string[] snapshot;
            long snapshotVersion;

            lock (visibleLinesSync)
            {
                snapshot = visibleLines.ToArray();
                snapshotVersion = bufferVersion;
            }

            LogTextBox.Text = string.Join(Environment.NewLine, snapshot);
            renderedVersion = snapshotVersion;

            if (shouldAutoScroll)
                LogTextBox.ScrollToEnd();
        }

        private bool IsNearBottom()
        {
            if (logScrollViewer == null)
                return true;

            return logScrollViewer.VerticalOffset >= logScrollViewer.ScrollableHeight - 24.0;
        }

        private static ScrollViewer FindScrollViewer(DependencyObject root)
        {
            if (root == null)
                return null;

            if (root is ScrollViewer viewer)
                return viewer;

            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childCount; i++)
            {
                ScrollViewer childViewer = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
                if (childViewer != null)
                    return childViewer;
            }

            return null;
        }
    }
}
