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
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace DVMConsole
{
    public class FlashingBackgroundManager
    {
        private readonly Control _control;
        private readonly Canvas _canvas;
        private readonly UserControl _userControl;
        private readonly Window _mainWindow;
        private readonly DispatcherTimer _timer;
        private Brush _originalControlBackground;
        private Brush _originalCanvasBackground;
        private Brush _originalUserControlBackground;
        private Brush _originalMainWindowBackground;
        private bool _isFlashing;

        public FlashingBackgroundManager(Control control = null, Canvas canvas = null, UserControl userControl = null, Window mainWindow = null, int intervalMilliseconds = 450)
        {
            _control = control;
            _canvas = canvas;
            _userControl = userControl;
            _mainWindow = mainWindow;

            if (_control == null && _canvas == null && _userControl == null && _mainWindow == null)
                throw new ArgumentException("At least one of control, canvas, userControl, or mainWindow must be provided.");

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(intervalMilliseconds)
            };
            _timer.Tick += OnTimerTick;
        }

        public void Start()
        {
            if (_isFlashing)
                return;

            if (_control != null)
                _originalControlBackground = _control.Background;

            if (_canvas != null)
                _originalCanvasBackground = _canvas.Background;

            if (_userControl != null)
                _originalUserControlBackground = _userControl.Background;

            if (_mainWindow != null)
                _originalMainWindowBackground = _mainWindow.Background;

            _isFlashing = true;
            _timer.Start();
        }

        public void Stop()
        {
            if (!_isFlashing)
                return;

            _timer.Stop();

            if (_control != null)
                _control.Background = _originalControlBackground;

            if (_canvas != null)
                _canvas.Background = _originalCanvasBackground;

            if (_userControl != null)
                _userControl.Background = _originalUserControlBackground;

            if (_mainWindow != null && _originalMainWindowBackground != null)
                _mainWindow.Background = _originalMainWindowBackground;

            _isFlashing = false;
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            Brush flashingColor = Brushes.Red;

            if (_control != null)
                _control.Background = _control.Background == Brushes.DarkRed ? _originalControlBackground : Brushes.DarkRed;

            if (_canvas != null)
                _canvas.Background = _canvas.Background == flashingColor ? _originalCanvasBackground : flashingColor;

            if (_userControl != null)
                _userControl.Background = _userControl.Background == Brushes.DarkRed ? _originalUserControlBackground : Brushes.DarkRed;

            if (_mainWindow != null)
                _mainWindow.Background = _mainWindow.Background == flashingColor ? _originalMainWindowBackground : flashingColor;
        }
    }
}
