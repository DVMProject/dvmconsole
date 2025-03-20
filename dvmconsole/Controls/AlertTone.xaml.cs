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
using System.Windows.Input;
using System.Windows.Media;

namespace dvmconsole.Controls
{
    /// <summary>
    /// 
    /// </summary>
    public partial class AlertTone : UserControl
    {
        private Point startPoint;
        private bool isDragging;

        public static readonly DependencyProperty AlertFileNameProperty =
            DependencyProperty.Register("AlertFileName", typeof(string), typeof(AlertTone), new PropertyMetadata(string.Empty));

        /*
        ** Properties
        */

        /// <summary>
        /// 
        /// </summary>
        public string AlertFileName
        {
            get => (string)GetValue(AlertFileNameProperty);
            set => SetValue(AlertFileNameProperty, value);
        }

        /// <summary>
        /// 
        /// </summary>
        public string AlertFilePath { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public bool IsEditMode { get; set; }

        /*
        ** Events
        */

        public event Action<AlertTone> OnAlertTone;

        /*
        ** Methods
        */

        /// <summary>
        /// Initializes a new instance of the <see cref="AlertTone"/> class.
        /// </summary>
        /// <param name="alertFilePath"></param>
        public AlertTone(string alertFilePath)
        {
            InitializeComponent();
            AlertFilePath = alertFilePath;
            AlertFileName = System.IO.Path.GetFileNameWithoutExtension(alertFilePath);

            this.MouseLeftButtonDown += AlertTone_MouseLeftButtonDown;
            this.MouseMove += AlertTone_MouseMove;
            this.MouseRightButtonDown += AlertTone_MouseRightButtonDown;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void PlayAlert_Click(object sender, RoutedEventArgs e)
        {
            OnAlertTone.Invoke(this);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AlertTone_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsEditMode) return;

            startPoint = e.GetPosition(this);
            isDragging = true;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AlertTone_MouseMove(object sender, MouseEventArgs e)
        {
            if (isDragging && IsEditMode)
            {
                var parentCanvas = VisualTreeHelper.GetParent(this) as Canvas;
                if (parentCanvas != null)
                {
                    Point mousePos = e.GetPosition(parentCanvas);
                    double newLeft = mousePos.X - startPoint.X;
                    double newTop = mousePos.Y - startPoint.Y;

                    Canvas.SetLeft(this, Math.Max(0, newLeft));
                    Canvas.SetTop(this, Math.Max(0, newTop));
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AlertTone_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsEditMode || !isDragging) return;

            isDragging = false;

            var parentCanvas = VisualTreeHelper.GetParent(this) as Canvas;
            if (parentCanvas != null)
            {
                double x = Canvas.GetLeft(this);
                double y = Canvas.GetTop(this);
            }

            ReleaseMouseCapture();
        }
    } // public partial class AlertTone : UserControl
} // namespace dvmconsole.Controls
