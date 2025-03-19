/*
* WhackerLink - DVMConsole
*
* This program is free software: you can redistribute it and/or modify
* it under the terms of the GNU General Public License as published by
* the Free Software Foundation, either version 3 of the License, or
* (at your option) any later version.
*
* This program is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
* GNU General Public License for more details.
*
* You should have received a copy of the GNU General Public License
* along with this program.  If not, see <http://www.gnu.org/licenses/>.
* 
* Copyright (C) 2024 Caleb, K4PHP
* 
*/

using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DVMConsole.Controls
{
    public partial class AlertTone : UserControl
    {
        public event Action<AlertTone> OnAlertTone;

        public static readonly DependencyProperty AlertFileNameProperty =
            DependencyProperty.Register("AlertFileName", typeof(string), typeof(AlertTone), new PropertyMetadata(string.Empty));

        public string AlertFileName
        {
            get => (string)GetValue(AlertFileNameProperty);
            set => SetValue(AlertFileNameProperty, value);
        }

        public string AlertFilePath { get; set; }

        private Point _startPoint;
        private bool _isDragging;

        public bool IsEditMode { get; set; }

        public AlertTone(string alertFilePath)
        {
            InitializeComponent();
            AlertFilePath = alertFilePath;
            AlertFileName = System.IO.Path.GetFileNameWithoutExtension(alertFilePath);

            this.MouseLeftButtonDown += AlertTone_MouseLeftButtonDown;
            this.MouseMove += AlertTone_MouseMove;
            this.MouseRightButtonDown += AlertTone_MouseRightButtonDown;
        }

        private void PlayAlert_Click(object sender, RoutedEventArgs e)
        {
            OnAlertTone.Invoke(this);
        }

        private void AlertTone_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsEditMode) return;

            _startPoint = e.GetPosition(this);
            _isDragging = true;
        }

        private void AlertTone_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging && IsEditMode)
            {
                var parentCanvas = VisualTreeHelper.GetParent(this) as Canvas;
                if (parentCanvas != null)
                {
                    Point mousePos = e.GetPosition(parentCanvas);
                    double newLeft = mousePos.X - _startPoint.X;
                    double newTop = mousePos.Y - _startPoint.Y;

                    Canvas.SetLeft(this, Math.Max(0, newLeft));
                    Canvas.SetTop(this, Math.Max(0, newTop));
                }
            }
        }

        private void AlertTone_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsEditMode || !_isDragging) return;

            _isDragging = false;

            var parentCanvas = VisualTreeHelper.GetParent(this) as Canvas;
            if (parentCanvas != null)
            {
                double x = Canvas.GetLeft(this);
                double y = Canvas.GetTop(this);
            }

            ReleaseMouseCapture();
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }
    }
}
