// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Desktop Dispatch Console
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Desktop Dispatch Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2025 Bryan Biedenkapp, N2PLL
*
*/

using System.ComponentModel;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

namespace dvmconsole.Controls
{
    /// <summary>
    /// Convert a size to a double according a percent ratio (0 to 1).
    /// </summary>
    public class SizePercentConverter : IMultiValueConverter
    {
        private static SizePercentConverter instance;

        /*
        ** Properties
        */

        /// <summary>
        /// 
        /// </summary>
        public static SizePercentConverter Instance => instance ?? (instance = new SizePercentConverter());

        /*
        ** Methods
        */

        /// <summary>
        /// Convert a size to a double according a percent ratio (0 to 1).
        /// </summary>
        /// <param name="values"></param>
        /// <param name="targetType"></param>
        /// <param name="parameter"></param>
        /// <param name="culture"></param>
        /// <returns></returns>
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
            => Math.Max(0, System.Convert.ToDouble(values[0]) * System.Convert.ToDouble(values[1]));

        /// <summary>
        /// 
        /// </summary>
        /// <param name="value"></param>
        /// <param name="targetTypes"></param>
        /// <param name="parameter"></param>
        /// <param name="culture"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    } // public class SizePercentConverter : IMultiValueConverter

    /// <summary>
    /// View model for the VU meter.
    /// </summary>
    public class VuMeterViewModel : INotifyPropertyChanged
    {
        public double LevelScaleFactor { get; set; } = 1d / 20000d;
        protected double level = 0;
        protected double invertedLevel = 1;

        /*
        ** Properties
        */

        /// <summary>
        /// Audio Level.
        /// </summary>
        public double Level
        {
            get => level;
            set
            {
                level = value;
                OnPropertyChanged("Level");
                InvertedLevel = 1 - level;
            }
        }

        /// <summary>
        /// Inverted audio level.
        /// </summary>
        public double InvertedLevel
        {
            get => invertedLevel;
            set
            {
                invertedLevel = value;
                OnPropertyChanged("InvertedLevel");
            }
        }

        /*
        ** Events
        */

        /// <summary>
        /// Event action that occurs when a property changes on this control.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /*
        ** Methods
        */

        /// <summary>
        /// Initializes a new instance of the <see cref="VuMeterViewModel"/> class.
        /// </summary>
        public VuMeterViewModel()
        {
            /* stub */
        }

        /// <summary>
        /// 
        /// </summary>
        public void Reset() => Level = 0;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="propertyName"></param>
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    } // public class VuMeterViewModel : INotifyPropertyChanged

    /// <summary>
    /// Interaction logic for VuMeterControl.xaml.
    /// </summary>
    public partial class VuMeterControl : UserControl
    {
        private VuMeterViewModel viewModel;

        /// <summary>
        /// 
        /// </summary>
        public VuMeterViewModel ViewModel
        {
            get => viewModel;
            set
            {
                viewModel = value;
                DataContext = viewModel;
            }
        }

        /*
        ** Methods
        */

        /// <summary>
        /// Initializes a new instance of the <see cref="VuMeterControl"/> class.
        /// </summary>
        public VuMeterControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="brush"></param>
        public void SetBackground(Brush brush)
        {
            ControlBorder.BorderBrush = brush;
            backgroundRect.BorderBrush = brush;
            backgroundRect.Background = brush;
            maskRect.Fill = brush;
        }
    } // public partial class VuMeterControl : UserControl
} // namespace dvmconsole.Controls
