using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using VRCOSC.App.SDK.Modules;
using VRCOSC.App.SDK.Modules.Attributes.Settings;

namespace YeusepesModules.Common.ScreenUtilities
{
    public partial class ScreenUtilitySelector : UserControl
    {
        public event EventHandler AdvancedSettingsClicked;

        public ScreenUtilities ScreenUtilities
        {
            get => screenUtilities;
            set => screenUtilities = value;
        }

        public static readonly DependencyProperty SelectedGPUProperty =
            DependencyProperty.Register(
                nameof(SelectedGPU),
                typeof(string),
                typeof(ScreenUtilitySelector),
                new PropertyMetadata("Default", (d, e) => ((ScreenUtilitySelector)d).SetSelectedGPU((string)e.NewValue)));

        public static readonly DependencyProperty SelectedDisplayProperty =
            DependencyProperty.Register(
                nameof(SelectedDisplay),
                typeof(string),
                typeof(ScreenUtilitySelector),
                new PropertyMetadata("Default", (d, e) => ((ScreenUtilitySelector)d).SetSelectedDisplay((string)e.NewValue)));


        public string SelectedGPU
        {
            get { return (string)GetValue(SelectedGPUProperty); }
            set { SetValue(SelectedGPUProperty, value); }
        }

        public string SelectedDisplay
        {
            get { return (string)GetValue(SelectedDisplayProperty); }
            set { SetValue(SelectedDisplayProperty, value); }
        }

        // Delegate that, given a display name, returns a live capture image.
        public Func<string, BitmapSource> LiveCaptureProvider { get; set; }

        // Events so external code can subscribe to selection changes.
        public event EventHandler<string> GPUSelectionChanged;
        public event EventHandler<string> DisplaySelectionChanged;

        // The list of display view controls.
        private readonly List<DisplayView> displayViews = new List<DisplayView>();



        ScreenUtilities screenUtilities;

        // Parameterless constructor used by XAML.
        public ScreenUtilitySelector()
        {
            InitializeComponent();
            InitializeControl();

            Loaded += ScreenUtilitySelector_Loaded;
        }

        /// <summary>
        /// Constructor required by the module setting framework.
        /// </summary>
        public ScreenUtilitySelector(Module module, ModuleSetting setting)
            : this()
        {
            // Validate that the setting is the expected type.
            if (setting is StringModuleSetting stringSetting)
            {
                DataContext = stringSetting;
            }
            else
            {
                throw new InvalidOperationException("ScreenUtilitySelector requires a StringModuleSetting.");
            }
        }

        private void ScreenUtilitySelector_Loaded(object sender, RoutedEventArgs e)
        {

        }

        private void UpdateDisplayPreviews()
        {
            foreach (var view in displayViews)
            {
                var bmp = LiveCaptureProvider != null
                    ? LiveCaptureProvider(view.DisplayName)
                    : CreateDummyBitmap(view.DisplayName);
                view.UpdateLiveView(bmp);
            }
        }


        /// <summary>
        /// Initial UI setup.
        /// </summary>
        private void InitializeControl()
        {
            // Set up with default dummy values.
            GPUComboBox.ItemsSource = new List<string> { "Default" };
            GPUComboBox.SelectedIndex = 0;
            SelectedGPU = "Default";

            // Create a default display item (can be updated later via RefreshLists).
            LoadDisplays(new List<string> { "Default" });
        }

        /// <summary>
        /// Refresh the lists of GPUs and Displays.
        /// Call this method from your module once you've obtained the actual lists.
        /// </summary>
        /// <param name="availableGPUs">List of GPU names.</param>
        /// <param name="availableDisplays">List of display names.</param>       
        public void RefreshLists(IEnumerable<string> gpus, IEnumerable<string> displays)
        {
            UpdateUI(() =>
            {
                GPUComboBox.ItemsSource = gpus;
                LoadDisplays(displays.ToList());
                UpdateDisplayPreviews();
            });
        }



        /// <summary>
        /// Populate the WrapPanel with display items.
        /// </summary>
        /// <param name="displays">List of display names.</param>
        private void LoadDisplays(List<string> displays)
        {
            DisplaysWrapPanel.Children.Clear();
            displayViews.Clear();

            var activeDisplays = displays
                .Where(d => !string.IsNullOrWhiteSpace(d) && d != "Default")
                .ToList();

            foreach (var display in activeDisplays)
            {
                var view = new DisplayView(display);
                view.MouseLeftButtonUp += DisplayView_MouseLeftButtonUp;
                displayViews.Add(view);
                DisplaysWrapPanel.Children.Add(view);
            }

            // Restore previous selection without firing the change event
            RestoreSelectedDisplay();
        }

        private void RestoreSelectedDisplay()
        {
            var view = displayViews.FirstOrDefault(v => v.DisplayName == SelectedDisplay);
            if (view != null)
            {
                foreach (var v in displayViews)
                    v.BorderBrush = Brushes.Transparent;
                view.BorderBrush = Brushes.Blue;
            }
            else
            {
                ClearSelectedDisplay(raiseEvent: false);
            }
        }

        private void ClearSelectedDisplay(bool raiseEvent = true)
        {
            foreach (var view in displayViews)
                view.BorderBrush = Brushes.Transparent;

            if (SelectedDisplay != "Default")
            {
                SelectedDisplay = "Default";
                if (raiseEvent)
                    DisplaySelectionChanged?.Invoke(this, SelectedDisplay);
            }
        }




        private void GPUComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GPUComboBox.SelectedItem is string gpu)
            {
                SelectedGPU = gpu;
                GPUSelectionChanged?.Invoke(this, gpu);
            }
        }

        private void DisplayView_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is DisplayView clickedView)
            {
                // If it’s already selected, clear back to default; otherwise select it
                if (SelectedDisplay == clickedView.DisplayName)
                    ClearSelectedDisplay();
                else
                    SetSelectedDisplay(clickedView);
            }
        }

        private void ClearSelectedDisplay()
        {
            // Remove all borders
            foreach (var view in displayViews)
                view.BorderBrush = Brushes.Transparent;

            // Reset to your “Default” value
            SelectedDisplay = "Default";
            DisplaySelectionChanged?.Invoke(this, SelectedDisplay);
        }


        private void SetSelectedDisplay(DisplayView selectedView, bool raiseEvent = true)
        {
            foreach (var view in displayViews)
                view.BorderBrush = Brushes.Transparent;

            selectedView.BorderBrush = Brushes.Blue;

            if (SelectedDisplay != selectedView.DisplayName)
            {
                SelectedDisplay = selectedView.DisplayName;
                if (raiseEvent)
                    DisplaySelectionChanged?.Invoke(this, SelectedDisplay);
            }
        }


        /// <summary>
        /// Creates a dummy BitmapSource (colored rectangle based on display name).
        /// Replace this if LiveCaptureProvider is not set.
        /// </summary>
        private BitmapSource CreateDummyBitmap(string displayName)
        {
            const int width = 200, height = 200;
            var pixelFormat = PixelFormats.Bgra32;
            int rawStride = (width * pixelFormat.BitsPerPixel + 7) / 8;
            byte[] pixelData = new byte[rawStride * height];

            // Derive a color from the display name.
            byte r = (byte)(displayName.GetHashCode() % 255);
            byte g = (byte)((displayName.GetHashCode() / 2) % 255);
            byte b = (byte)((displayName.GetHashCode() / 3) % 255);

            for (int i = 0; i < pixelData.Length; i += 4)
            {
                pixelData[i] = b;         // Blue
                pixelData[i + 1] = g;     // Green
                pixelData[i + 2] = r;     // Red
                pixelData[i + 3] = 255;   // Alpha
            }

            return BitmapSource.Create(width, height, 96, 96, pixelFormat, null, pixelData, rawStride);
        }

        private void AdvancedSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (AdvancedPanel.Visibility == Visibility.Visible)
            {
                AdvancedPanel.Visibility = Visibility.Collapsed;
                ArrowIcon.Text = "▼";
            }
            else
            {
                AdvancedPanel.Visibility = Visibility.Visible;
                ArrowIcon.Text = "▲";

                // Populate lists only on expand
                RefreshLists(screenUtilities.GetGraphicsCards(), screenUtilities.GetDisplays());
            }

            AdvancedSettingsClicked?.Invoke(this, EventArgs.Empty);
        }


        private void UpdateUI(Action uiAction)
        {
            if (Application.Current.Dispatcher.CheckAccess())
            {
                uiAction();
            }
            else
            {
                Application.Current.Dispatcher.Invoke(uiAction);
            }
        }

        public void SetSelectedGPU(string gpu)
        {
            if (GPUComboBox.ItemsSource != null && GPUComboBox.Items.Contains(gpu))
                GPUComboBox.SelectedItem = gpu;
        }

        public void SetSelectedDisplay(string displayName)
        {
            var view = displayViews.FirstOrDefault(v => v.DisplayName == displayName);
            if (view != null)
                SetSelectedDisplay(view);
            else
                ClearSelectedDisplay();
        }




    }

    /// <summary>
    /// A simple control representing a single display.
    /// </summary>
    public class DisplayView : Border
    {
        public string DisplayName { get; private set; }
        public Image LiveImage { get; private set; }

        public DisplayView(string displayName)
        {
            DisplayName = displayName;
            Margin = new Thickness(5);
            BorderThickness = new Thickness(2);
            BorderBrush = Brushes.Transparent;
            LiveImage = new Image { Stretch = Stretch.Uniform };
            Child = LiveImage;
        }


        /// <summary>
        /// Update the live view image.
        /// </summary>
        /// <param name="bitmapSource">The new image source.</param>
        public void UpdateLiveView(BitmapSource bitmapSource)
        {
            LiveImage.Source = bitmapSource;
            if (bitmapSource != null)
            {
                const double targetHeight = 150; // pick a uniform height
                double aspect = (double)bitmapSource.PixelWidth / bitmapSource.PixelHeight;
                Width = aspect * targetHeight;
                Height = targetHeight;
            }
        }

    }
}
