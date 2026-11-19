// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Desktop Dispatch Console
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Desktop Dispatch Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2026 C. Lovell, Dev_Ranger
*
*/

using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

using NAudio.Wave;

namespace dvmconsole.Controls
{
    /// <summary>
    /// Compact web URL stream player chip.
    /// </summary>
    public partial class WebStreamChip : UserControl, INotifyPropertyChanged, IDisposable
    {
        private const int PlaybackBufferLength = 2560;
        private const int MaxConnectionAttempts = 3;
        private const double DefaultVolume = 1.0;
        private const double VolumeStep = 0.1;
        private const double VolumeMarkerTrackPadding = 4.0;
        private const double AudioActivityRmsThreshold = 0.0035;
        private const short AudioActivityPeakThreshold = 650;
        private static readonly TimeSpan AudioActivityHold = TimeSpan.FromMilliseconds(1400);
        private static readonly TimeSpan ConnectionRetryDelay = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan LiveMonitorBufferDuration = TimeSpan.FromSeconds(2);

        private readonly object sync = new object();
        private readonly DispatcherTimer rxIdleTimer;
        private CancellationTokenSource playbackCts;
        private readonly HashSet<Task> playbackTasks = new HashSet<Task>();
        private long playbackGeneration;
        private bool disposed;
        private bool isActive;
        private bool isConnecting;
        private bool isReceiving;
        private bool isFailed;
        private bool updatingVolumeSlider;
        private string displayName = string.Empty;
        private string streamUrl = string.Empty;
        private string statusText = "Off";
        private double volume = DefaultVolume;
        private DateTime lastAudioActivityUtc = DateTime.MinValue;

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler<double> VolumeChanged;

        public WebStreamChip()
        {
            InitializeComponent();
            ControlBorder.Background = ChannelBox.DARK_GRAY_GRADIENT;
            rxIdleTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            rxIdleTimer.Tick += (s, e) =>
            {
                if (IsReceiving && DateTime.UtcNow - lastAudioActivityUtc > AudioActivityHold + TimeSpan.FromMilliseconds(200))
                    IsReceiving = false;
            };
        }

        public string DisplayName
        {
            get => displayName;
            set
            {
                displayName = value ?? string.Empty;
                OnPropertyChanged(nameof(DisplayName));
            }
        }

        public string StreamUrl
        {
            get => streamUrl;
            set
            {
                streamUrl = value ?? string.Empty;
                OnPropertyChanged(nameof(StreamUrl));
            }
        }
        public string AuthUsername { get; set; } = string.Empty;
        public string AuthPassword { get; set; } = string.Empty;
        public string AudioOutputKey { get; set; } = string.Empty;
        public AudioManager AudioManager { get; set; }
        public Brush IdleBackground { get; set; } = ChannelBox.BLUE_GRADIENT;
        public bool SuppressSelectionToggle { get; set; }

        public bool IsActive
        {
            get => isActive;
            private set
            {
                if (isActive == value)
                    return;
                isActive = value;
                if (isActive)
                {
                    IsFailed = false;
                    IsConnecting = true;
                }
                else
                {
                    IsConnecting = false;
                }

                StatusText = isActive ? "Connecting" : "Off";
                UpdateBackground();
            }
        }

        private bool IsConnecting
        {
            get => isConnecting;
            set
            {
                if (isConnecting == value)
                    return;

                isConnecting = value;
                UpdateBackground();
            }
        }

        public bool IsFailed
        {
            get => isFailed;
            private set
            {
                if (isFailed == value)
                    return;

                isFailed = value;
                if (isFailed)
                {
                    IsConnecting = false;
                    StatusText = "Down";
                }
                else if (!IsActive)
                    StatusText = "Off";
                else if (!IsReceiving)
                    StatusText = "Idle";

                UpdateBackground();
            }
        }

        public bool IsReceiving
        {
            get => isReceiving;
            private set
            {
                if (isReceiving == value)
                    return;
                isReceiving = value;
                if (isReceiving)
                    IsConnecting = false;

                StatusText = isReceiving ? "RX" : (IsActive ? "Idle" : "Off");
                UpdateBackground();
            }
        }

        public string StatusText
        {
            get => statusText;
            private set
            {
                statusText = value;
                OnPropertyChanged(nameof(StatusText));
            }
        }

        public double Volume
        {
            get => volume;
            set
            {
                double normalized = NormalizeVolume(value);
                if (Math.Abs(volume - normalized) < 0.001)
                    return;

                volume = normalized;
                AudioManager?.SetTalkgroupVolume(ResolveAudioOutputKey(), (float)volume);
                VolumeChanged?.Invoke(this, volume);
                OnPropertyChanged(nameof(Volume));
            }
        }

        public uint StreamId { get; private set; }

        public void SetInitialVolume(double value)
        {
            volume = NormalizeVolume(value);
            OnPropertyChanged(nameof(Volume));
        }

        public void ApplyCurrentVolume()
        {
            AudioManager?.SetTalkgroupVolume(ResolveAudioOutputKey(), (float)volume);
        }

        public void StartPlayback()
        {
            if (IsActive || string.IsNullOrWhiteSpace(StreamUrl))
                return;

            Start();
        }

        public void Stop()
        {
            try
            {
                lock (sync)
                {
                    CancellationTokenSource cts = playbackCts;
                    playbackCts = null;
                    playbackGeneration++;
                    // Cancel and retire output under the same fence used to publish PCM.
                    try { cts?.Cancel(); }
                    finally { AudioManager?.StopTalkgroupStream(ResolveAudioOutputKey()); }
                }
            }
            finally
            {
                rxIdleTimer.Stop();
                lastAudioActivityUtc = DateTime.MinValue;
                IsConnecting = false;
                IsReceiving = false;
                IsFailed = false;
                IsActive = false;
                StreamId = 0;
            }
        }

        /// <summary>Stops playback and observes retired workers without blocking the dispatcher.</summary>
        public async Task StopAsync()
        {
            try { Stop(); }
            catch (Exception ex) { Log.WriteWarning($"Web stream '{DisplayName}' stop failed: {ex.Message}"); }
            Task[] workers;
            lock (sync)
                workers = playbackTasks.ToArray();
            try { await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (TimeoutException)
            {
                // Media Foundation URL readers cannot always interrupt a native open/read.
                // Their tasks remain observed and fenced from this and replacement outputs.
                Log.WriteWarning($"Web stream '{DisplayName}' is still closing its reader; retired output is disabled.");
            }
            catch (Exception ex)
            {
                Log.WriteWarning($"Web stream '{DisplayName}' worker cleanup failed: {ex.Message}");
            }
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (disposed)
                    return;
                disposed = true;
            }
            Stop();
        }

        private void ToggleActive()
        {
            if (IsFailed)
            {
                IsFailed = false;
                IsActive = false;
                return;
            }

            if (IsActive)
            {
                Stop();
                return;
            }

            if (string.IsNullOrWhiteSpace(StreamUrl))
            {
                IsFailed = true;
                return;
            }

            Start();
        }

        private void Start()
        {
            lock (sync)
            {
                if (disposed || playbackCts != null)
                    return;

                CancellationTokenSource sessionCts = new CancellationTokenSource();
                playbackCts = sessionCts;
                long generation = ++playbackGeneration;
                StreamId = GenerateStreamId();
                IsFailed = false;
                IsActive = true;
                rxIdleTimer.Start();
                Task worker = Task.Run(() => PlaybackLoop(sessionCts, generation));
                playbackTasks.Add(worker);
                _ = worker.ContinueWith(completed =>
                {
                    lock (sync)
                        playbackTasks.Remove(completed);
                    if (completed.IsFaulted)
                        Log.WriteWarning($"Web stream '{DisplayName}' worker failed: {completed.Exception.GetBaseException().Message}");
                }, TaskScheduler.Default);
            }
        }

        private void PlaybackLoop(CancellationTokenSource sessionCts, long generation)
        {
            CancellationToken token = sessionCts.Token;
            bool failed = false;
            try
            {
                for (int attempt = 1; attempt <= MaxConnectionAttempts && !token.IsCancellationRequested; attempt++)
                {
                    SetConnectingStatus(sessionCts, attempt == 1 ? "Connecting" : $"Retry {attempt}/{MaxConnectionAttempts}");

                    try
                    {
                        RunPlaybackAttempt(sessionCts);
                        failed = false;
                        break;
                    }
                    catch (Exception ex) when (!token.IsCancellationRequested)
                    {
                        failed = true;
                        Log.WriteWarning($"Web stream '{DisplayName}' playback attempt {attempt}/{MaxConnectionAttempts} failed: {ex.Message}");
                        if (ex is HttpRequestException http &&
                            (http.StatusCode == HttpStatusCode.Unauthorized || http.StatusCode == HttpStatusCode.Forbidden))
                            break;
                    }

                    if (attempt < MaxConnectionAttempts && !token.IsCancellationRequested)
                        token.WaitHandle.WaitOne(ConnectionRetryDelay);
                }
            }
            catch (Exception) when (token.IsCancellationRequested)
            {
                failed = false;
            }
            catch (Exception ex)
            {
                failed = !token.IsCancellationRequested;
                Log.WriteWarning($"Web stream '{DisplayName}' playback failed: {ex.Message}");
            }
            finally
            {
                bool ownsSession;
                bool finalFailed = failed && !token.IsCancellationRequested;
                lock (sync)
                {
                    ownsSession = ReferenceEquals(playbackCts, sessionCts);
                    if (ownsSession)
                        playbackCts = null;
                    sessionCts.Dispose();
                }

                if (ownsSession)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (disposed || generation != playbackGeneration)
                            return;
                        AudioManager?.StopTalkgroupStream(ResolveAudioOutputKey());
                        rxIdleTimer.Stop();
                        IsConnecting = false;
                        IsReceiving = false;
                        IsActive = false;
                        IsFailed = finalFailed;
                        StreamId = 0;
                    }));
                }
            }
        }

        private void RunPlaybackAttempt(CancellationTokenSource sessionCts)
        {
            CancellationToken token = sessionCts.Token;
            token.ThrowIfCancellationRequested();
            using IDisposable authenticatedResources = CreateStreamReader(token, out WaveStream reader);
            using (reader)
            using (MediaFoundationResampler resampler = new MediaFoundationResampler(reader, new WaveFormat(8000, 16, 1)))
            {
                InitializePlayback(sessionCts);

                byte[] buffer = new byte[PlaybackBufferLength];
                while (!token.IsCancellationRequested)
                {
                    int bytesRead = resampler.Read(buffer.AsSpan());
                    token.ThrowIfCancellationRequested();
                    if (bytesRead <= 0)
                        throw new EndOfStreamException("stream ended");

                    byte[] playbackChunk = new byte[bytesRead];
                    Buffer.BlockCopy(buffer, 0, playbackChunk, 0, bytesRead);
                    lock (sync)
                    {
                        if (!ReferenceEquals(playbackCts, sessionCts) || token.IsCancellationRequested)
                            return;
                        AudioManager?.AddLiveMonitorStream(ResolveAudioOutputKey(), playbackChunk, LiveMonitorBufferDuration);
                    }
                    UpdateActivityState(sessionCts, playbackChunk, playbackChunk.Length);
                }
            }
        }

        private void SetConnectingStatus(CancellationTokenSource sessionCts, string text)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!ReferenceEquals(playbackCts, sessionCts) || !IsActive)
                    return;

                IsConnecting = true;
                StatusText = text;
            }), DispatcherPriority.Background);
        }

        private IDisposable CreateStreamReader(CancellationToken token, out WaveStream reader)
        {
            if (string.IsNullOrWhiteSpace(AuthUsername))
            {
                reader = new MediaFoundationReader(StreamUrl);
                return null;
            }

            AuthenticatedAudioRelay relay = AuthenticatedAudioRelay.Open(StreamUrl, AuthUsername, AuthPassword, token);
            try
            {
                token.ThrowIfCancellationRequested();
                reader = new MediaFoundationReader(relay.Url, new MediaFoundationReader.MediaFoundationReaderSettings { SingleReaderObject = true });
                return relay;
            }
            catch
            {
                relay.Dispose();
                throw;
            }
        }

        private void InitializePlayback(CancellationTokenSource sessionCts)
        {
            Dispatcher.Invoke(() =>
            {
                if (!ReferenceEquals(playbackCts, sessionCts))
                    return;
                AudioManager?.SetTalkgroupVolume(ResolveAudioOutputKey(), (float)Volume);
                IsConnecting = false;
                StatusText = "Idle";
            });
        }

        private void UpdateActivityState(CancellationTokenSource sessionCts, byte[] buffer, int bytesRead)
        {
            bool frameActive = IsAudioActive(buffer, bytesRead);
            DateTime now = DateTime.UtcNow;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!ReferenceEquals(playbackCts, sessionCts))
                    return;
                if (frameActive)
                    lastAudioActivityUtc = now;
                IsReceiving = now - lastAudioActivityUtc <= AudioActivityHold;
            }), DispatcherPriority.Background);
        }

        private static bool IsAudioActive(byte[] buffer, int bytesRead)
        {
            if (buffer == null || bytesRead < 2)
                return false;

            int peak = 0;
            double sumSquares = 0.0;
            int sampleCount = 0;
            for (int i = 0; i + 1 < bytesRead; i += 2)
            {
                short sample = (short)((buffer[i + 1] << 8) | buffer[i]);
                int abs = Math.Abs((int)sample);
                if (abs > peak)
                    peak = abs;

                double normalized = sample / 32768.0;
                sumSquares += normalized * normalized;
                sampleCount++;
            }

            if (sampleCount == 0)
                return false;

            double rms = Math.Sqrt(sumSquares / sampleCount);
            return rms >= AudioActivityRmsThreshold || peak >= AudioActivityPeakThreshold;
        }

        private void UpdateBackground()
        {
            if (ControlBorder == null)
                return;

            if (IsFailed)
                ControlBorder.Background = ChannelBox.RED_GRADIENT;
            else if (!IsActive)
                ControlBorder.Background = ChannelBox.DARK_GRAY_GRADIENT;
            else if (IsConnecting)
                ControlBorder.Background = ChannelBox.ORANGE_GRADIENT;
            else if (IsReceiving)
                ControlBorder.Background = ChannelBox.GREEN_GRADIENT;
            else
                ControlBorder.Background = IdleBackground ?? ChannelBox.BLUE_GRADIENT;
        }

        private static uint GenerateStreamId()
        {
            byte[] bytes = Guid.NewGuid().ToByteArray();
            uint streamId = BitConverter.ToUInt32(bytes, 0);
            return streamId == 0 ? 1 : streamId;
        }

        public void ProcessSelectionClick()
        {
            ToggleActive();
        }

        private string ResolveAudioOutputKey()
        {
            if (!string.IsNullOrWhiteSpace(AudioOutputKey))
                return AudioOutputKey;

            return DisplayName;
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (updatingVolumeSlider)
                return;

            double steppedValue = NormalizeVolume(e.NewValue);
            if (Math.Abs(steppedValue - e.NewValue) > 0.0001)
            {
                updatingVolumeSlider = true;
                VolumeSlider.Value = steppedValue;
                updatingVolumeSlider = false;
            }

            Volume = steppedValue;
        }

        private void VolumeSlider_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            AdjustVolumeFromMouseWheel(e);
        }

        private void VolumeBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            AdjustVolumeFromMouseWheel(e);
        }

        private void AdjustVolumeFromMouseWheel(MouseWheelEventArgs e)
        {
            if (!VolumeSlider.IsEnabled)
                return;

            double direction = e.Delta > 0 ? VolumeStep : -VolumeStep;
            VolumeSlider.Value = NormalizeVolume(VolumeSlider.Value + direction);
            e.Handled = true;
        }

        private void VolumeSlider_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateDefaultVolumeMarker();
        }

        private void VolumeSlider_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateDefaultVolumeMarker();
        }

        private static double NormalizeVolume(double value)
        {
            double steppedValue = Math.Round(value / VolumeStep) * VolumeStep;
            return Math.Max(0.0, Math.Min(4.0, steppedValue));
        }

        private void UpdateDefaultVolumeMarker()
        {
            VolumeSlider.ApplyTemplate();

            if (VolumeSlider.Template.FindName("PART_DefaultVolumeMarker", VolumeSlider) is not FrameworkElement marker)
                return;

            double sliderWidth = VolumeSlider.ActualWidth;
            double range = VolumeSlider.Maximum - VolumeSlider.Minimum;
            if (sliderWidth <= 0 || range <= 0)
                return;

            double percent = (DefaultVolume - VolumeSlider.Minimum) / range;
            double usableWidth = Math.Max(0, sliderWidth - (VolumeMarkerTrackPadding * 2));
            double markerWidth = marker.Width > 0 ? marker.Width : 2.0;
            double x = VolumeMarkerTrackPadding + (usableWidth * percent) - (markerWidth / 2.0);
            marker.Margin = new Thickness(Math.Max(0, x), 0, 0, 0);
        }

        private void UserControl_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (SuppressSelectionToggle)
                return;
            if (FindParent<Slider>(e.OriginalSource as DependencyObject) != null)
                return;

            ToggleActive();
        }

        private static T FindParent<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T match)
                    return match;
                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            UserControl_MouseLeftButtonUp(this, e);
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

    }
}
