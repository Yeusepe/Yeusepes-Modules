using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using YeusepesModules.ShazamOSC.ShazamAPI;

namespace YeusepesModules.ShazamOSC.UI
{    

    public class ShazamRecognitionContext : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private string _title, _artist, _coverArtUrl;
        private double _soundLevel;
        private bool _isListening;    // ← new
        private double _bassLevel;
        private double _trebleLevel;

        public string Title
        {
            get => _title;
            set { _title = value; OnPropertyChanged(); }
        }

        public string Artist
        {
            get => _artist;
            set { _artist = value; OnPropertyChanged(); }
        }

        public string CoverArtUrl
        {
            get => _coverArtUrl;
            set { _coverArtUrl = value; OnPropertyChanged(); }
        }

        public double BassLevel
        {
            get => _bassLevel;
            set { _bassLevel = value; OnPropertyChanged(); }
        }

        /// <summary>0…1, driven by high frequencies</summary>
        public double TrebleLevel
        {
            get => _trebleLevel;
            set { _trebleLevel = value; OnPropertyChanged(); }
        }

        public bool IsListening
        {
            get => _isListening;
            set { _isListening = value; OnPropertyChanged(); }
        }

        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
    public class LevelToScaleConverter : IValueConverter
    {
        // maps [0…1] → [1…11]
        private const double ExaggerationFactor = 10.0;

        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            if (value is double lvl)
            {
                // clamp to [0,1], then scale by our exaggeration factor
                double clamped = Math.Clamp(lvl, 0.0, 1.0);
                return 1.0 + clamped * ExaggerationFactor;
            }
            return 1.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class LevelToLogoScaleConverter : IValueConverter
    {
        // original logo range was +2/3; now we multiply that span by ExaggerationFactor
        private const double BaseSpan = 2.0 / 3.0;
        private const double ExaggerationFactor = 10.0;

        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            if (value is double lvl)
            {
                double clamped = Math.Clamp(lvl, 0.0, 1.0);
                return 1.0 + clamped * BaseSpan * ExaggerationFactor;
            }
            return 1.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
    public class InverseLevelToScaleConverter : IValueConverter
    {
        // at lvl==0 → 1.0, at lvl==1 → 0.1
        private const double ShrinkFactor = 6;

        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            if (value is double lvl)
            {
                double clamped = Math.Clamp(lvl, 0.0, 1.0);
                return 1.0 - clamped * ShrinkFactor;
            }
            return 1.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b)
                return Visibility.Visible;
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility v)
                return v == Visibility.Visible;
            return false;
        }
    }

    /// <summary>
    /// Interaction logic for LastRecognized.xaml
    /// </summary>
    public partial class LastRecognized : UserControl
    {
        private readonly LevelToScaleConverter _circleConverter = new LevelToScaleConverter();
        private readonly InverseLevelToScaleConverter _logoConverter = new InverseLevelToScaleConverter();
        private readonly SineEase _easing = new SineEase { EasingMode = EasingMode.EaseOut };

        public LastRecognized(ShazamOSC module)
        {
            InitializeComponent();
            DataContext = module.RecognitionContext;

            module.RecognitionContext.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ShazamRecognitionContext.BassLevel))
                    DispatchAnimate(LogoScale, _logoConverter, module.RecognitionContext.BassLevel);

                if (e.PropertyName == nameof(ShazamRecognitionContext.BassLevel))
                    DispatchAnimate(CircleScale, _circleConverter, module.RecognitionContext.BassLevel);
            };
        }

        private void DispatchAnimate(ScaleTransform transform, IValueConverter conv, double level)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                double target = (double)conv.Convert(level, typeof(double), null, null);
                var anim = new DoubleAnimation(target, TimeSpan.FromMilliseconds(100))
                {
                    EasingFunction = _easing
                };
                transform.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
                transform.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
            }));
        }
    }


}
