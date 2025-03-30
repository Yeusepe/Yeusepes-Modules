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
using YeusepesModules.IDC.Encoder;  // For StringDecoder and related functions

namespace YeusepesModules.IDC
{
    public partial class DecoderToleranceControl : UserControl
    {
        private StringDecoder _decoder;
        private Image<Bgr, byte> _sourceImage;

        public DecoderToleranceControl()
        {
            InitializeComponent();

            Loaded += DecoderToleranceControl_Loaded;
        }

        // This method is called when the control is fully loaded.
        private void DecoderToleranceControl_Loaded(object sender, RoutedEventArgs e)
        {
            // If dependencies have not been attached externally, you can either delay processing
            // or create a default instance.
            if (_decoder == null)
            {
                // You can either throw an exception or create a default instance.
                // For example, using a default no‑op logger:
                _decoder = new StringDecoder(new EncodingUtilities());
            }
            if (_sourceImage == null)
            {
                _sourceImage = LoadSampleImage();
            }
            ProcessImage();
        }

        public void AttachDependencies(EncodingUtilities utils)
        {
            // Create a new decoder with the provided utilities.
            _decoder = new StringDecoder(utils);
            // Optionally, you might also load your sample image here (or you can do it later).
            _sourceImage = LoadSampleImage();
        }

        /// <summary>
        /// Handler for when the slider value changes.
        /// </summary>
        private void ToleranceSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // Ensure that _decoder and _sourceImage are set
            if (_decoder == null || _sourceImage == null)
                return;

            ToleranceValueText.Text = ((int)e.NewValue).ToString();
            ProcessImage();
        }


        /// <summary>
        /// Processes the image using the current tolerance value.
        /// It updates the filtered image display and decodes the image.
        /// </summary>
        private void ProcessImage()
        {
            // Get the current tolerance from the slider.
            double tolerance = ToleranceSlider.Value;

            // Convert the target hex color (for example "#1EB955") to a Bgr color using your function.
            Bgr targetColor = _decoder.HexToBgr("#1EB955");

            // Filter the source image using the specified tolerance.
            // (Ensure that FilterImageByColor is accessible; you might need to make it public or wrap it.)
            Image<Bgr, byte> filtered = FilterImageByColor(_sourceImage, targetColor, tolerance);

            // Update the FilteredImage control with the new filtered image.
            FilteredImage.Source = ConvertToBitmapSource(filtered);

            // Run the decoding process.
            string decodedText = _decoder.DecodeText(filtered);

            // Create a visual representation of the decoded result.
            DecodedImage.Source = CreateDecodedImage(decodedText);
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
            string url = "https://private-user-images.githubusercontent.com/180480983/423741225-855af8ac-54ab-4bc3-a9bc-ae7c746828ad.png?jwt=eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJnaXRodWIuY29tIiwiYXVkIjoicmF3LmdpdGh1YnVzZXJjb250ZW50LmNvbSIsImtleSI6ImtleTUiLCJleHAiOjE3NDMzNzE2NTIsIm5iZiI6MTc0MzM3MTM1MiwicGF0aCI6Ii8xODA0ODA5ODMvNDIzNzQxMjI1LTg1NWFmOGFjLTU0YWItNGJjMy1hOWJjLWFlN2M3NDY4MjhhZC5wbmc_WC1BbXotQWxnb3JpdGhtPUFXUzQtSE1BQy1TSEEyNTYmWC1BbXotQ3JlZGVudGlhbD1BS0lBVkNPRFlMU0E1M1BRSzRaQSUyRjIwMjUwMzMwJTJGdXMtZWFzdC0xJTJGczMlMkZhd3M0X3JlcXVlc3QmWC1BbXotRGF0ZT0yMDI1MDMzMFQyMTQ5MTJaJlgtQW16LUV4cGlyZXM9MzAwJlgtQW16LVNpZ25hdHVyZT00MmQ4M2FjZGEwOTg1MmJmZmMwMjYwODY4YzA0ZWU5MjdmZjgwZGY5YmE2ODU5ZWEwZmI2MjAzNWM4MWEzOGJkJlgtQW16LVNpZ25lZEhlYWRlcnM9aG9zdCJ9.qV9VEv29a0mzGU2RUFsuNkCrl50WX5YgU9TlprwR6JI";

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
