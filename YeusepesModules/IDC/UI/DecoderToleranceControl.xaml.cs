using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Emgu.CV;
using Emgu.CV.Structure;
using YeusepesLowLevelTools;
using System.IO;              // For MemoryStream
using System.Net.Http;        // For HttpClient
using System.Drawing;         // For Bitmap (System.Drawing.Bitmap)
using System.Threading.Tasks; // In case you need async
using YeusepesModules.IDC.Encoder;
using ABI.System.Collections.Generic;  // For StringDecoder and related functions

namespace YeusepesModules.IDC
{
    public partial class DecoderToleranceControl : UserControl
    {
        private StringDecoder _decoder;
        private Image<Bgr, byte> _sourceImage;

        public static readonly DependencyProperty ToleranceProperty =
        DependencyProperty.Register(
            "Tolerance",
            typeof(int),
            typeof(DecoderToleranceControl),
            new PropertyMetadata(100, OnToleranceChanged));


        public int Tolerance
        {
            get => (int)GetValue(ToleranceProperty);
            set => SetValue(ToleranceProperty, value);
        }


        public DecoderToleranceControl()
        {
            InitializeComponent();
            // Load the persisted tolerance value (or default if not set)            
        }
        private static void OnToleranceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = d as DecoderToleranceControl;
            if (control != null)
            {
                int toleranceValue = (int)e.NewValue;

                // Update any dependent functionality, e.g. reprocessing the image
                if (control._decoder != null)
                {
                    control._decoder.encodingUtilities.Log("Setting tolerance to " + toleranceValue);
                    control._decoder.encodingUtilities.SetSettingValue(EncoderSettings.Tolerance, toleranceValue.ToString());
                    control._decoder.encodingUtilities.Log("Tolerance saved in settings: " +control._decoder.encodingUtilities.GetSettingValue(EncoderSettings.Tolerance));
                    control.ProcessImage();
                }                
            }
        }        


        /// <summary>
        /// Toggle the advanced panel and download the image when expanded.
        /// </summary>
        private async void AdvancedSettingsButton_Click(object sender, RoutedEventArgs e)
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

                // Download the sample image and process it.
                await DownloadAndProcessImageAsync();
            }
        }

        /// <summary>
        /// Downloads the sample image asynchronously, converts it, and processes it.
        /// </summary>
        private async Task DownloadAndProcessImageAsync()
        {
            try
            {
                string url = "https://raw.githubusercontent.com/Yeusepe/Yeusepes-Modules/refs/heads/main/Resources/Images/scanable.png";
                using (HttpClient client = new HttpClient())
                {
                    byte[] data = await client.GetByteArrayAsync(url);
                    using (MemoryStream ms = new MemoryStream(data))
                    {
                        using (var bmp = new System.Drawing.Bitmap(ms))
                        {
                            _sourceImage = BitmapToEmguImage(bmp);
                        }
                    }
                }
                ProcessImage();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error downloading image: " + ex.Message);
            }
        }


        public void AttachDependencies(EncodingUtilities utils)
        {
            if (utils == null)
                throw new ArgumentNullException(nameof(utils));

            _decoder = new StringDecoder(utils);
            _sourceImage = LoadSampleImage();

            // Safely retrieve the persisted tolerance value.
            string persisted = null;
            if (utils.GetSettingValue != null)
            {
                // Convert the int to a string.
                persisted = utils.GetSettingValue(EncoderSettings.Tolerance).ToString();
            }

            int tol;
            if (!string.IsNullOrWhiteSpace(persisted) && int.TryParse(persisted, out tol))
            {
                Tolerance = tol;
                utils.Log("Loaded tolerance from settings: " + tol);
            }
            else
            {
                Tolerance = 100;
                utils.Log("No tolerance setting found; using default value.");
            }
        }

        /// <summary>
        /// Processes the image using the current tolerance value.
        /// It updates the filtered image display and decodes the image.
        /// </summary>
        private void ProcessImage()
        {
            try
            {
                // Get the current tolerance from the slider.
                double tolerance = ToleranceSlider.Value;

                // Convert the target hex color to a Bgr color.
                Bgr targetColor = _decoder.HexToBgr("#1EB955");

                // Filter the source image.
                Image<Bgr, byte> filtered = FilterImageByColor(_sourceImage, targetColor, tolerance);

                // Update the filtered image display.
                FilteredImage.Source = ConvertToBitmapSource(filtered);

                // Attempt to decode the image.
                string decodedText = _decoder.DecodeText(filtered);

                // Display the decoded text.
                DecodedImage.Source = CreateDecodedImage(decodedText);
            }
            catch (Exception ex)
            {                
                DecodedImage.Source = CreateDecodedImage("Decoding failed: " + ex.Message);
            }
        }


        /// <summary>
        /// Converts an EmguCV Image to a BitmapSource for WPF display.
        /// </summary>
        public static BitmapSource ConvertToBitmapSource(Image<Bgr, byte> image)
        {
            // Get the underlying Mat.
            Mat mat = image.Mat;
            int width = mat.Width;
            int height = mat.Height;
            int stride = mat.Step; // number of bytes per row

            // Create a byte array to hold the image data.
            byte[] data = new byte[height * stride];
            // Copy data from the Mat's unmanaged memory to managed array.
            Marshal.Copy(mat.DataPointer, data, 0, data.Length);

            // Create a BitmapSource from the byte array.
            // For Image<Bgr,byte>, the format is Bgr24.
            BitmapSource bitmapSource = BitmapSource.Create(
                width,
                height,
                96, // dpiX
                96, // dpiY
                PixelFormats.Bgr24,
                null,
                data,
                stride);

            return bitmapSource;
        }


        /// <summary>
        /// Creates a BitmapSource displaying the decoded text.
        /// In this example, we render the text onto a bitmap.
        /// </summary>
        private BitmapSource CreateDecodedImage(string decodedText)
        {
            int width = 300, height = 100;
            DrawingVisual drawingVisual = new DrawingVisual();
            using (DrawingContext dc = drawingVisual.RenderOpen())
            {
                dc.DrawRectangle(System.Windows.Media.Brushes.Black, null, new Rect(0, 0, width, height));

                FormattedText formattedText = new FormattedText(
                    decodedText,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"),
                    16,
                    System.Windows.Media.Brushes.White,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip);

                dc.DrawText(formattedText, new System.Windows.Point(10, 10));
            }
            RenderTargetBitmap bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(drawingVisual);
            return bmp;
        }

        /// <summary>
        /// Loads a sample source image.
        /// Replace this with your own image acquisition method.
        /// </summary>
        private Image<Bgr, byte> LoadSampleImage()
        {
            // URL for the image you want to use.
            string url = "https://raw.githubusercontent.com/Yeusepe/Yeusepes-Modules/refs/heads/main/Resources/Images/scanable.png";

            using (HttpClient client = new HttpClient())
            {
                // GetByteArrayAsync returns a Task<byte[]>; blocking on .Result here for simplicity.
                byte[] data = client.GetByteArrayAsync(url).Result;
                using (MemoryStream ms = new MemoryStream(data))
                {
                    using (var bmp = new System.Drawing.Bitmap(ms))
                    {
                        return BitmapToEmguImage(bmp);
                    }
                }
            }
        }



        /// <summary>
        /// Converts a System.Drawing.Bitmap into an Emgu CV Image by copying its pixel data into a 3D byte array.
        /// Assumes the Bitmap is in 24bpp RGB format.
        /// </summary>
        private Image<Bgr, byte> BitmapToEmguImage(System.Drawing.Bitmap bmp)
        {
            // If needed, convert to 24bpp RGB.
            if (bmp.PixelFormat != System.Drawing.Imaging.PixelFormat.Format24bppRgb)
            {
                var temp = new System.Drawing.Bitmap(bmp.Width, bmp.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                using (var gr = System.Drawing.Graphics.FromImage(temp))
                {
                    gr.DrawImage(bmp, new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height));
                }
                bmp = temp;
            }

            int width = bmp.Width;
            int height = bmp.Height;
            System.Drawing.Imaging.BitmapData bmpData = bmp.LockBits(
                new System.Drawing.Rectangle(0, 0, width, height),
                System.Drawing.Imaging.ImageLockMode.ReadOnly,
                bmp.PixelFormat);

            int stride = bmpData.Stride;
            byte[] pixelData = new byte[stride * height];
            System.Runtime.InteropServices.Marshal.Copy(bmpData.Scan0, pixelData, 0, pixelData.Length);
            bmp.UnlockBits(bmpData);

            // Create a 3D byte array of dimensions [height, width, channels]
            byte[,,] data = new byte[height, width, 3];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = y * stride + x * 3;
                    data[y, x, 0] = pixelData[index];      // Blue channel
                    data[y, x, 1] = pixelData[index + 1];  // Green channel
                    data[y, x, 2] = pixelData[index + 2];  // Red channel
                }
            }

            // Now create the Emgu CV image from the 3D array.
            return new Image<Bgr, byte>(data);
        }




        /// <summary>
        /// A wrapper for FilterImageByColor.
        /// Ensure that your original method is accessible or move it here.
        /// </summary>
        public static Image<Bgr, byte> FilterImageByColor(Image<Bgr, byte> image, Bgr targetColor, double tolerance)
        {
            // Copy the implementation from your provided function.
            Image<Bgr, byte> filtered = image.CopyBlank();
            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    Bgr color = image[y, x];
                    double diff = Math.Sqrt(
                        Math.Pow(color.Blue - targetColor.Blue, 2) +
                        Math.Pow(color.Green - targetColor.Green, 2) +
                        Math.Pow(color.Red - targetColor.Red, 2));
                    if (diff < tolerance)
                        filtered[y, x] = new Bgr(255, 255, 255);
                    else
                        filtered[y, x] = new Bgr(0, 0, 0);
                }
            }
            return filtered;
        }
    }
}
