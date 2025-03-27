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
        // Dependency properties for binding if needed.
        public static readonly DependencyProperty SelectedGPUProperty =
            DependencyProperty.Register("SelectedGPU", typeof(string), typeof(ScreenUtilitySelector), new PropertyMetadata("Default"));

        public string SelectedGPU
        {
            get { return (string)GetValue(SelectedGPUProperty); }
            set { SetValue(SelectedGPUProperty, value); }
        }

        public static readonly DependencyProperty SelectedDisplayProperty =
            DependencyProperty.Register("SelectedDisplay", typeof(string), typeof(ScreenUtilitySelector), new PropertyMetadata("Default"));

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
            UpdateDisplayPreviews();
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

            foreach (var display in displays)
            {
                var displayView = new DisplayView(display);
                displayView.MouseLeftButtonUp += DisplayView_MouseLeftButtonUp;
                displayViews.Add(displayView);
                DisplaysWrapPanel.Children.Add(displayView);
            }

            // Set default selection if available.
            if (displayViews.Count > 0)
            {
                SetSelectedDisplay(displayViews[0]);
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


        private void SetSelectedDisplay(DisplayView selectedView)
        {
            // Clear selection borders.
            foreach (var view in displayViews)
            {
                view.BorderBrush = Brushes.Transparent;
            }
            // Mark the selected view.
            selectedView.BorderBrush = Brushes.Blue;
            SelectedDisplay = selectedView.DisplayName;
            DisplaySelectionChanged?.Invoke(this, selectedView.DisplayName);
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
            Width = 200;
            Height = 200;
            Margin = new Thickness(5);
            BorderThickness = new Thickness(2);
            BorderBrush = Brushes.Transparent;

            LiveImage = new Image { Stretch = Stretch.UniformToFill };
            Child = LiveImage;
        }

        /// <summary>
        /// Update the live view image.
        /// </summary>
        /// <param name="bitmapSource">The new image source.</param>
        public void UpdateLiveView(BitmapSource bitmapSource)
        {
            LiveImage.Source = bitmapSource;
        }
    }
}
