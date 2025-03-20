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
using System.Windows.Media;
using System.Windows.Threading;

namespace dvmconsole
{
    /// <summary>
    /// 
    /// </summary>
    public class FlashingBackgroundManager
    {
        private readonly Control control;
        private readonly Canvas canvas;
        private readonly UserControl userControl;
        private readonly Window mainWindow;
        private readonly DispatcherTimer timer;
        
        private Brush originalControlBackground;
        private Brush originalCanvasBackground;
        private Brush originalUserControlBackground;
        private Brush originalMainWindowBackground;
        
        private bool isFlashing;

        /*
        ** Methods
        */

        /// <summary>
        /// Initializes a new instance of the <see cref="FlashingBackgroundManager"/> class.
        /// </summary>
        /// <param name="control"></param>
        /// <param name="canvas"></param>
        /// <param name="userControl"></param>
        /// <param name="mainWindow"></param>
        /// <param name="intervalMilliseconds"></param>
        /// <exception cref="ArgumentException"></exception>
        public FlashingBackgroundManager(Control control = null, Canvas canvas = null, UserControl userControl = null, Window mainWindow = null, int intervalMilliseconds = 450)
        {
            this.control = control;
            this.canvas = canvas;
            this.userControl = userControl;
            this.mainWindow = mainWindow;

            if (this.control == null && this.canvas == null && this.userControl == null && this.mainWindow == null)
                throw new ArgumentException("At least one of control, canvas, userControl, or mainWindow must be provided.");

            timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(intervalMilliseconds)
            };
            timer.Tick += OnTimerTick;
        }

        /// <summary>
        /// 
        /// </summary>
        public void Start()
        {
            if (isFlashing)
                return;

            if (control != null)
                originalControlBackground = control.Background;

            if (canvas != null)
                originalCanvasBackground = canvas.Background;

            if (userControl != null)
                originalUserControlBackground = userControl.Background;

            if (mainWindow != null)
                originalMainWindowBackground = mainWindow.Background;

            isFlashing = true;
            timer.Start();
        }
        
        /// <summary>
        /// 
        /// </summary>
        public void Stop()
        {
            if (!isFlashing)
                return;

            timer.Stop();

            if (control != null)
                control.Background = originalControlBackground;

            if (canvas != null)
                canvas.Background = originalCanvasBackground;

            if (userControl != null)
                userControl.Background = originalUserControlBackground;

            if (mainWindow != null && originalMainWindowBackground != null)
                mainWindow.Background = originalMainWindowBackground;

            isFlashing = false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnTimerTick(object sender, EventArgs e)
        {
            Brush flashingColor = Brushes.Red;

            if (control != null)
                control.Background = control.Background == Brushes.DarkRed ? originalControlBackground : Brushes.DarkRed;

            if (canvas != null)
                canvas.Background = canvas.Background == flashingColor ? originalCanvasBackground : flashingColor;

            if (userControl != null)
                userControl.Background = userControl.Background == Brushes.DarkRed ? originalUserControlBackground : Brushes.DarkRed;

            if (mainWindow != null)
                mainWindow.Background = mainWindow.Background == flashingColor ? originalMainWindowBackground : flashingColor;
        }
    } // public class FlashingBackgroundManager
} // namespace dvmconsole
