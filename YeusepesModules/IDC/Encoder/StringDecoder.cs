using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;

using ScreenCapture.NET;
using VRCOSC.App.SDK.Parameters;
using static YeusepesModules.OSCQR.OSCQR;

namespace YeusepesModules.IDC.Encoder
{
    public class StringDecoder
    {
        private readonly EncodingUtilities encodingUtilities;
        private bool capturing = false; // Indicates if a capture is in progress        

        // An event to signal that decoding is complete.
        public event Action<string>? DecodingCompleted;

        string picturesDir = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        string debugFolder;
        string candidateCirclesPath;

        public StringDecoder(EncodingUtilities encodingUtilities)
        {
            this.encodingUtilities = encodingUtilities;
        }

        public async Task<bool> OnModuleStart()
        {
            await encodingUtilities.ScreenUtilities.OnModuleStart();
            debugFolder = Path.Combine(picturesDir, "Debug");
            Directory.CreateDirectory(debugFolder);
            return true;
        }

        public string StartDecode()
        {
            encodingUtilities.LogDebug("Starting decoding process.");
            try
            {
                Bitmap screenshot = encodingUtilities.ScreenUtilities.TakeScreenshot();
                if (screenshot != null)
                {
                    encodingUtilities.LogDebug("Screenshot captured.");
                    // Save the original screenshot for debugging.
                    string screenshotPath = Path.Combine(debugFolder, "screenshot.png");
                    screenshot.Save(screenshotPath, ImageFormat.Png);

                    using (MemoryStream ms = new MemoryStream())
                    {
                        try
                        {
                            encodingUtilities.LogDebug("Saving Screenshot");
                            // Save the bitmap into a memory stream (using PNG format)
                            screenshot.Save(ms, ImageFormat.Png);
                            
                            byte[] imageData = ms.ToArray();
                            
                            // Create an empty Mat and decode the byte array into it
                            Mat mat = new Mat();

                            encodingUtilities.LogDebug("Reading Image");
                            CvInvoke.Imdecode(imageData, ImreadModes.Color, mat);

                            encodingUtilities.LogDebug("Converting Mat to Image");
                            // Convert the Mat to an Image<Bgr, byte>
                            using (Image<Bgr, byte> encodedImage = mat.ToImage<Bgr, byte>())
                            {
                                encodingUtilities.LogDebug("Stuck 6");
                                string decodedText = DecodeText(encodedImage);

                                // Remove any null characters and trim whitespace.
                                decodedText = decodedText.Replace("\0", "").Trim();
                                
                                encodingUtilities.LogDebug($"Decoded text: {decodedText}");
                                return decodedText;
                            }
                        }
                        catch (Exception e)
                        {
                            encodingUtilities.LogDebug(e.Message);
                        }
                    }
                }
                else
                {
                    encodingUtilities.LogDebug("Failed to capture screenshot.");
                }
                return "";
            }
            catch (Exception e)
            {
                encodingUtilities.LogDebug($"Error during decoding: {e.Message}");
            }
            return "";
        }


        #region Helper Structures

        // Represents a candidate circle.
        private struct Circle
        {
            public int X;
            public int Y;
            public int Radius;
            public Circle(int x, int y, int radius)
            {
                X = x;
                Y = y;
                Radius = radius;
            }
        }

        // Holds information about a decoded ring layer.
        private class LayerInfo
        {
            public double Radius { get; set; }
            public List<double> Angles { get; set; }
            public LayerInfo(double radius, List<double> angles)
            {
                Radius = radius;
                Angles = angles;
            }
        }

        #endregion

        #region Utility Functions

        /// <summary>
        /// Converts a binary string (8 bits per character) to text.
        /// </summary>
        private static string BitsToText(string bits)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < bits.Length; i += 8)
            {
                if (i + 8 <= bits.Length)
                {
                    string byteStr = bits.Substring(i, 8);
                    byte b = Convert.ToByte(byteStr, 2);
                    sb.Append((char)b);
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Cyclically shifts the string to the left by k characters.
        /// </summary>
        private static string CyclicShift(string s, int k)
        {
            if (s.Length == 0)
                return s;
            k = k % s.Length;
            return s.Substring(k) + s.Substring(0, k);
        }

        /// <summary>
        /// Finds the shift value that best aligns the expected sync pattern with the decoded sync bits.
        /// </summary>
        private static int FindSyncShift(string decodedSync, string expectedSync)
        {
            int bestShift = 0;
            int bestMatches = -1;
            int N = decodedSync.Length;
            for (int k = 0; k < N; k++)
            {
                string shifted = CyclicShift(expectedSync, k);
                int matches = 0;
                for (int i = 0; i < N; i++)
                {
                    if (shifted[i] == decodedSync[i])
                        matches++;
                }
                if (matches > bestMatches)
                {
                    bestMatches = matches;
                    bestShift = k;
                }
            }
            return bestShift;
        }

        /// <summary>
        /// Returns a fixed binary sync pattern of the given length.
        /// </summary>
        private static string GetSyncPattern(int length)
        {
            string basePattern = "110010";
            StringBuilder pattern = new StringBuilder();
            while (pattern.Length < length)
            {
                pattern.Append(basePattern);
            }
            return pattern.ToString(0, length);
        }

        /// <summary>
        /// Converts a hex string to a Bgr color.
        /// </summary>
        private Bgr HexToBgr(string hex)
        {
            hex = hex.Trim();
            encodingUtilities.LogDebug($"Hex value received: '{hex}'");

            Color color = ColorTranslator.FromHtml(hex);
            encodingUtilities.LogDebug($"Color: {color.R}, {color.G}, {color.B}");
            return new Bgr(color.B, color.G, color.R);
        }

        /// <summary>
        /// Filters the image so that pixels close to the target color (within tolerance) become white and all others black.
        /// </summary>
        private static Image<Bgr, byte> FilterImageByColor(Image<Bgr, byte> image, Bgr targetColor, double tolerance = 100)
        {
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

        #endregion

        #region Candidate Circle Detection

        private static List<Circle> GetCandidateCirclesContour(Image<Bgr, byte> image)
        {
            List<Circle> candidates = new List<Circle>();

            // Convert the image to grayscale.
            Image<Gray, byte> gray = image.Convert<Gray, byte>();

            // Apply Gaussian blur to reduce noise.
            Image<Gray, byte> blurred = gray.SmoothGaussian(5); // Adjust kernel size as needed.

            // Adjust the Canny thresholds (try lower thresholds if edges are weak).
            Image<Gray, byte> canny = blurred.Canny(50, 150);

            // Use a different contour retrieval mode if needed.
            using (VectorOfVectorOfPoint contours = new VectorOfVectorOfPoint())
            {
                CvInvoke.FindContours(canny, contours, null, RetrType.List, ChainApproxMethod.ChainApproxSimple);
                for (int i = 0; i < contours.Size; i++)
                {
                    using (VectorOfPoint contour = contours[i])
                    {
                        double area = CvInvoke.ContourArea(contour);
                        double perimeter = CvInvoke.ArcLength(contour, true);
                        if (perimeter == 0)
                            continue;

                        // Compute circularity; a perfect circle has a value of 1.
                        double circularity = 4 * Math.PI * area / (perimeter * perimeter);

                        // Filter for roughly circular contours with a minimum area.
                        if (circularity > 0.7 && area > 50)
                        {
                            // Calculate the moments for the contour.
                            var moments = CvInvoke.Moments(contour, false);

                            // Ensure the area (M00) is non-zero.
                            if (moments.M00 != 0)
                            {
                                // Compute the centroid (center of mass).
                                float centerX = (float)(moments.M10 / moments.M00);
                                float centerY = (float)(moments.M01 / moments.M00);

                                // Compute the radius as the maximum distance from the centroid.
                                double maxDistance = 0;
                                foreach (var pt in contour.ToArray())
                                {
                                    double dx = pt.X - centerX;
                                    double dy = pt.Y - centerY;
                                    double distance = Math.Sqrt(dx * dx + dy * dy);
                                    if (distance > maxDistance)
                                        maxDistance = distance;
                                }

                                // Add the candidate circle.
                                candidates.Add(new Circle((int)centerX, (int)centerY, (int)maxDistance));
                            }
                        }
                    }
                }
            }
            return candidates;
        }

        #endregion

        #region Other Processing Functions

        /// <summary>
        /// Computes a grayscale distance map from the image to the target color.
        /// Lower values mean the pixel is closer to the target color.
        /// </summary>
        private static Image<Gray, byte> ComputeColorDistanceMap(Image<Bgr, byte> image, Bgr targetColor)
        {
            Image<Gray, byte> distanceMap = new Image<Gray, byte>(image.Size);
            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    Bgr color = image[y, x];
                    // Compute Euclidean distance in RGB space.
                    double diff = Math.Sqrt(
                        Math.Pow(color.Blue - targetColor.Blue, 2) +
                        Math.Pow(color.Green - targetColor.Green, 2) +
                        Math.Pow(color.Red - targetColor.Red, 2));
                    // Optionally, scale the diff so that it fits in 0-255.
                    // You might need to adjust this scaling factor.
                    byte intensity = (byte)Math.Min(255, diff * 2);
                    distanceMap.Data[y, x, 0] = intensity;
                }
            }
            return distanceMap;
        }


        /// <summary>
        /// Computes an Otsu threshold based on the candidate circle region (instead of outside it).
        /// </summary>
        private static double GetThresholdValue(Mat image, Point center, int radius)
        {
            // Convert image to grayscale if necessary.
            Mat gray = new Mat();
            if (image.NumberOfChannels != 1)
                CvInvoke.CvtColor(image, gray, ColorConversion.Bgr2Gray);
            else
                gray = image;

            // Create a mask for the candidate circle region.
            Mat mask = new Mat(image.Size, DepthType.Cv8U, 1);
            mask.SetTo(new MCvScalar(0));
            // Use the candidate circle radius (or maybe a slightly smaller value if you wish)
            CvInvoke.Circle(mask, center, radius, new MCvScalar(255), -1);

            // Apply the mask to extract the candidate region.
            Mat maskedImage = new Mat();
            CvInvoke.BitwiseAnd(gray, mask, maskedImage);

            // Use Otsu thresholding on the candidate region.
            Mat dummy = new Mat();
            double ret = CvInvoke.Threshold(maskedImage, dummy, 0, 255, ThresholdType.Binary | ThresholdType.Otsu);
            return ret;
        }



        /// <summary>
        /// Decodes a ring (e.g. the sync ring) from the distance map.
        /// In the binary-filtered image, white (high value) indicates a 1.
        /// </summary>
        private static (string decodedSync, int numPositions, double angleStep)
            DecodeSyncRing(Mat distanceMap, Point center, double radius, double dotSize, double threshold)
        {
            int numPositions = (int)(2 * Math.PI * radius / (2 * dotSize));
            if (numPositions == 0)
                return ("", 0, 0);
            double angleStep = 2 * Math.PI / numPositions;
            StringBuilder decodedSync = new StringBuilder();
            for (int i = 0; i < numPositions; i++)
            {
                double angle = i * angleStep;
                int x = (int)(center.X + radius * Math.Cos(angle));
                int y = (int)(center.Y + radius * Math.Sin(angle));
                if (x < 0 || x >= distanceMap.Cols || y < 0 || y >= distanceMap.Rows)
                {
                    decodedSync.Append("0");
                }
                else
                {
                    int x1 = Math.Max(0, x - 1);
                    int y1 = Math.Max(0, y - 1);
                    int x2 = Math.Min(distanceMap.Cols, x + 2);
                    int y2 = Math.Min(distanceMap.Rows, y + 2);
                    Rectangle roiRect = new Rectangle(x1, y1, x2 - x1, y2 - y1);
                    using (Mat roi = new Mat(distanceMap, roiRect))
                    {
                        MCvScalar meanScalar = CvInvoke.Mean(roi);
                        double avg = meanScalar.V0;
                        // In binary image: if the average is high, consider it a "1"
                        decodedSync.Append(avg > threshold ? "1" : "0");
                    }
                }
            }
            return (decodedSync.ToString(), numPositions, angleStep);
        }

        #endregion

        #region Decoder Functions

        public string DecodeText(Image<Bgr, byte> encodedImage, int designSize = 300, int designDotSize = 9,
            int numLayers = 4, int designLogoRadius = 55, int designRingMargin = 10,
            string targetCircleHex = "#1EB955")
        {
            // If a target hex is provided, filter the image.
            if (!string.IsNullOrEmpty(targetCircleHex))
            {
                encodedImage = FilterImageByColor(encodedImage, HexToBgr(targetCircleHex));
                // Save the filtered image for debugging.
                string filteredPath = Path.Combine(debugFolder, "filtered_image.png");
                encodedImage.Save(filteredPath);
            }
            return ProcessImage(designSize, designDotSize, numLayers, designLogoRadius, designRingMargin, encodedImage);
        }

        private string ProcessImage(int designSize, int designDotSize, int numLayers, int designLogoRadius, int designRingMargin, Image<Bgr, byte> origImage)
        {
            Image<Bgr, byte> overlayImage = origImage.Copy();
            // 1. Detect candidate circles.
            List<Circle> candidates = GetCandidateCirclesContour(origImage);
            if (candidates.Count == 0)
                throw new Exception("No candidate circles detected.");
            //SaveCandidateCircles(overlayImage, candidates);

            encodingUtilities.LogDebug("Candidate circles detected.");

            var (bestCandidate, finalDominantColor) = SelectBestCandidate(origImage, candidates, designSize, designLogoRadius, designRingMargin, designDotSize, numLayers);
            //SaveDetectedCircle(overlayImage, bestCandidate);

            encodingUtilities.LogDebug("Best candidate selected.");

            // 3. Compute dimensions (scale, radii, etc.) from the selected candidate.
            var (center, scaleFinal, scaledDotSizeFinal, scaledRingMargin, innerRadiusFinal, outerRadius) =
                ComputeDimensions(bestCandidate, designSize, designDotSize, numLayers, designLogoRadius, designRingMargin);

            encodingUtilities.LogDebug("Dimensions computed.");

            // 4. In the filtered approach the final distance map is the already filtered image.
            Mat finalDistanceMap = origImage.Mat;
            // Here, since the image is binary, we treat white as the target.
            encodingUtilities.LogDebug("Distance map computed from filtered image.");
            double finalThreshold = GetThresholdValue(finalDistanceMap, center, bestCandidate.Radius);

            encodingUtilities.LogDebug("Threshold computed.");

            // 5. Reconstruct layers (rings) and overlay them for debugging.
            List<LayerInfo> layersInfo = ReconstructLayers(numLayers, innerRadiusFinal, outerRadius, scaledDotSizeFinal);
            //SaveRingsOverlay(origImage, center, layersInfo, new Bgr(255, 255, 255));

            encodingUtilities.LogDebug("Rings reconstructed.");

            // 6. Decode the sync ring (first layer) to determine the rotation offset.
            var (decodedSyncFinal, N_syncFinal, angleStepSyncFinal) = DecodeSyncRing(finalDistanceMap, center, layersInfo[0].Radius, scaledDotSizeFinal, finalThreshold);
            encodingUtilities.LogDebug($"Sync Ring decoded bits: {decodedSyncFinal}");
            string expectedSyncFinal = GetSyncPattern(N_syncFinal);
            int bestShift = FindSyncShift(decodedSyncFinal, expectedSyncFinal);
            double rotationOffset = ((N_syncFinal - bestShift) % N_syncFinal) * angleStepSyncFinal;

            encodingUtilities.LogDebug("Sync ring decoded.");

            // 7. Decode data rings (the remaining layers) using the rotation offset.
            finalDistanceMap.Save(Path.Combine(debugFolder, "final_distance_map.png"));
            string decodedBits = DecodeDataRingsSimple(origImage,finalDistanceMap, center, layersInfo, rotationOffset, scaledDotSizeFinal);


            return BitsToText(decodedBits);
        }

        /// <summary>
        /// Draws candidate circles (in red) on a copy of the image and saves the result.
        /// Also draws a small filled square with the candidate’s dominant color and its hex value beside it.
        /// </summary>
        private static void SaveCandidateCircles(Image<Bgr, byte> image, List<Circle> candidates)
        {
            // Create a copy of the original image to draw on.
            Image<Bgr, byte> candidateImage = image.Copy();

            // Define the size of the dominant color square.
            int squareSize = 20;

            foreach (Circle candidate in candidates)
            {
                // Draw the candidate circle in red.
                candidateImage.Draw(new CircleF(new PointF(candidate.X, candidate.Y), candidate.Radius),
                    new Bgr(Color.Red), 2);

                // Compute the candidate's dominant color.
                Bgr candidateDominantColor = ComputeDominantColor(candidateImage, candidate);

                // Convert the dominant color to a hex string.
                string hexColor = $"#{(int)candidateDominantColor.Red:X2}{(int)candidateDominantColor.Green:X2}{(int)candidateDominantColor.Blue:X2}";

                // Determine the position for the color square.
                int squareX = candidate.X + candidate.Radius + 5;
                int squareY = candidate.Y - candidate.Radius;

                squareX = Math.Min(squareX, candidateImage.Width - squareSize - 1);
                squareY = Math.Max(0, squareY);
                squareY = Math.Min(squareY, candidateImage.Height - squareSize - 1);

                Rectangle colorSquare = new Rectangle(squareX, squareY, squareSize, squareSize);

                candidateImage.Draw(colorSquare, new Bgr(candidateDominantColor.Blue, candidateDominantColor.Green, candidateDominantColor.Red), -1);
                candidateImage.Draw(colorSquare, new Bgr(Color.White), 1);

                Point textPosition = new Point(squareX + squareSize + 5, squareY + squareSize / 2 + 5);

                CvInvoke.PutText(candidateImage, hexColor, textPosition, FontFace.HersheySimplex, 0.5, new MCvScalar(255, 255, 255), 1);
            }

            string picturesDir = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            string debugFolder = Path.Combine(picturesDir, "Debug");
            Directory.CreateDirectory(debugFolder);
            string candidateCirclesPath = Path.Combine(debugFolder, "candidate_circles.png");
            candidateImage.Save(candidateCirclesPath);
        }

        /// <summary>
        /// Selects the best candidate circle by checking the sync ring against the expected sync pattern ("110010").
        /// </summary>
        private (Circle bestCandidate, Bgr dominantColor) SelectBestCandidate(
            Image<Bgr, byte> origImage,
            List<Circle> candidates,
            int designSize,
            int designLogoRadius,
            int designRingMargin,
            int designDotSize,
            int numLayers,
            double requiredMatchFraction = 0.8)
        {
            var sortedCandidates = candidates.OrderBy(c => c.Radius).ToList();
            double sizeThreshold = sortedCandidates.Count >= 4 ? sortedCandidates[sortedCandidates.Count / 4].Radius : 0;

            double bestMatchScore = -1.0;
            Circle bestCandidate = new Circle();
            Bgr bestCandidateDominantColor = new Bgr();

            string syncPatternBase = "110010";

            string picturesDir = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            string debugFolder = Path.Combine(picturesDir, "Debug", "CandidateDebug");
            Directory.CreateDirectory(debugFolder);

            int candidateIndex = 0;
            List<(Point samplePoint, char expectedBit, double diff, bool isMatch)> sampleDetails = new List<(Point, char, double, bool)>();

            foreach (var candidate in candidates)
            {
                if (candidate.Radius < sizeThreshold)
                {
                    encodingUtilities.LogDebug($"Skipping candidate with radius {candidate.Radius} below threshold {sizeThreshold:F2}");
                    continue;
                }

                candidateIndex++;

                double scale = candidate.Radius / (double)designLogoRadius;

                double designOuterRadius = designSize / 2.0 - designDotSize;
                double innerRadiusDesign = designLogoRadius + designRingMargin;

                double syncRingDesign = innerRadiusDesign + (designOuterRadius - innerRadiusDesign) / (double)numLayers;
                double syncRingRadius = syncRingDesign * scale;
                double scaledDotSize = designDotSize * scale;

                int numPositions = (int)(2 * Math.PI * syncRingRadius / (2 * scaledDotSize));
                if (numPositions < 1)
                    numPositions = 1;

                Bgr candidateDominantColor = ComputeDominantColor(origImage, candidate);

                int matchingCount = 0;
                int totalCount = 0;

                Image<Bgr, byte> debugOverlay = origImage.Copy();
                debugOverlay.Draw(new CircleF(new PointF(candidate.X, candidate.Y), candidate.Radius),
                                  new Bgr(Color.Yellow), 2);
                debugOverlay.Draw(new CircleF(new PointF(candidate.X, candidate.Y), (float)syncRingRadius),
                                  new Bgr(Color.Blue), 2);

                for (int i = 0; i < numPositions; i++)
                {
                    double angle = i * (2 * Math.PI / numPositions);
                    int sampleX = (int)(candidate.X + syncRingRadius * Math.Cos(angle));
                    int sampleY = (int)(candidate.Y + syncRingRadius * Math.Sin(angle));

                    debugOverlay.Draw(new CircleF(new PointF(sampleX, sampleY), 3),
                                      new Bgr(Color.White), -1);

                    if (sampleX < 0 || sampleX >= origImage.Width || sampleY < 0 || sampleY >= origImage.Height)
                        continue;

                    int win = 1;
                    int x1 = Math.Max(0, sampleX - win);
                    int y1 = Math.Max(0, sampleY - win);
                    int x2 = Math.Min(origImage.Width - 1, sampleX + win);
                    int y2 = Math.Min(origImage.Height - 1, sampleY + win);
                    Rectangle roiRect = new Rectangle(x1, y1, x2 - x1 + 1, y2 - y1 + 1);

                    using (Mat roiMat = new Mat(origImage.Mat, roiRect))
                    {
                        MCvScalar meanScalar = CvInvoke.Mean(roiMat);
                        Bgr avgColor = new Bgr(meanScalar.V0, meanScalar.V1, meanScalar.V2);

                        double diff = Math.Sqrt(
                            Math.Pow(avgColor.Blue - candidateDominantColor.Blue, 2) +
                            Math.Pow(avgColor.Green - candidateDominantColor.Green, 2) +
                            Math.Pow(avgColor.Red - candidateDominantColor.Red, 2));
                        double colorThreshold = 30.0;
                        bool isSimilar = diff < colorThreshold;

                        char expectedBit = syncPatternBase[i % syncPatternBase.Length];
                        bool expectedOn = expectedBit == '1';
                        bool match = expectedOn ? isSimilar : !isSimilar;

                        if (match)
                            matchingCount++;
                        totalCount++;

                        sampleDetails.Add((new Point(sampleX, sampleY), expectedBit, diff, match));

                        Color markerColor = match ? Color.Green : Color.Red;
                        debugOverlay.Draw(new CircleF(new PointF(sampleX, sampleY), 4), new Bgr(markerColor), 2);
                        CvInvoke.PutText(debugOverlay, expectedBit.ToString(), new Point(sampleX + 5, sampleY + 5),
                            FontFace.HersheySimplex, 0.4, new MCvScalar(255, 255, 255), 1);
                    }
                }

                double matchFraction = totalCount > 0 ? (double)matchingCount / totalCount : 0;
                CvInvoke.PutText(debugOverlay, $"Match: {matchFraction:P0}", new Point(10, 30),
                    FontFace.HersheySimplex, 1.0, new MCvScalar(0, 255, 0), 2);

                double dx = candidate.X - 0;
                double dy = candidate.Y - origImage.Height;
                double distance = Math.Sqrt(dx * dx + dy * dy);
                double maxDistance = Math.Sqrt(origImage.Width * origImage.Width + origImage.Height * origImage.Height);
                double locationWeight = 1.0 - (distance / maxDistance);

                double candidateScore = matchFraction * 0.7 + locationWeight * 0.3;
                encodingUtilities.LogDebug($"Candidate {candidateIndex}: Match = {matchFraction:F2}, " +
                                           $"LocationWeight = {locationWeight:F2}, Score = {candidateScore:F2}");

                if (candidateScore > bestMatchScore)
                {
                    bestMatchScore = candidateScore;
                    bestCandidate = candidate;
                    bestCandidateDominantColor = candidateDominantColor;
                }

                sampleDetails.Clear();

                string candidateDebugPath = Path.Combine(debugFolder, $"Candidate_{candidateIndex}_debug.png");
                debugOverlay.Save(candidateDebugPath);
            }

            if (bestMatchScore < requiredMatchFraction)
                throw new Exception("No candidate had a sufficiently high first ring color match score.");

            return (bestCandidate, bestCandidateDominantColor);
        }

        /// <summary>
        /// Draws the selected candidate circle (in green) and saves the debug image.
        /// </summary>
        private static void SaveDetectedCircle(Image<Bgr, byte> image, Circle candidate)
        {
            image.Draw(new CircleF(new PointF(candidate.X, candidate.Y), candidate.Radius),
                new Bgr(Color.Green), 2);
            string picturesDir = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            string debugFolder = Path.Combine(picturesDir, "Debug");
            Directory.CreateDirectory(debugFolder);
            string detectedCirclePath = Path.Combine(debugFolder, "detected_circle.png");
            image.Save(detectedCirclePath);
        }

        /// <summary>
        /// Computes the center and scaling dimensions based on the selected candidate.
        /// </summary>
        private static (Point center, double scaleFinal, double scaledDotSizeFinal, double scaledRingMargin,
            double innerRadiusFinal, double outerRadius)
            ComputeDimensions(Circle candidate, int designSize, int designDotSize, int numLayers,
            int designLogoRadius, int designRingMargin)
        {
            Point center = new Point(candidate.X, candidate.Y);
            double detectedLogoRadius = candidate.Radius;
            double scaleFinal = detectedLogoRadius / (double)designLogoRadius;
            double scaledDotSizeFinal = designDotSize * scaleFinal;
            double scaledRingMargin = designRingMargin * scaleFinal;
            double designOuterRadius = designSize / 2.0 - designDotSize;
            double outerRadius = designOuterRadius * scaleFinal;
            double designInnerRadius = designLogoRadius + designRingMargin;
            double innerRadiusFinal = designInnerRadius * scaleFinal;
            return (center, scaleFinal, scaledDotSizeFinal, scaledRingMargin, innerRadiusFinal, outerRadius);
        }

        /// <summary>
        /// Computes the dominant color (by mode) inside the candidate circle using k-means clustering.
        /// In the filtered image, this will typically be white.
        /// </summary>
        private static Bgr ComputeDominantColor(Image<Bgr, byte> image, Circle candidate)
        {
            Mat mask = new Mat(image.Size, DepthType.Cv8U, 1);
            mask.SetTo(new MCvScalar(0));
            CvInvoke.Circle(mask, new Point(candidate.X, candidate.Y), candidate.Radius, new MCvScalar(255), -1);

            using (VectorOfPoint nonZeroPoints = new VectorOfPoint())
            {
                CvInvoke.FindNonZero(mask, nonZeroPoints);
                int count = nonZeroPoints.Size;
                if (count == 0)
                    return new Bgr(0, 0, 0);

                Matrix<float> samples = new Matrix<float>(count, 3);
                Point[] points = nonZeroPoints.ToArray();
                for (int i = 0; i < count; i++)
                {
                    Point p = points[i];
                    Bgr color = image[p.Y, p.X];
                    samples[i, 0] = (float)color.Blue;
                    samples[i, 1] = (float)color.Green;
                    samples[i, 2] = (float)color.Red;
                }

                int clusterCount = 3;
                Matrix<int> labels = new Matrix<int>(count, 1);
                Matrix<float> centers = new Matrix<float>(clusterCount, 3);
                MCvTermCriteria criteria = new MCvTermCriteria(10, 1.0);
                CvInvoke.Kmeans(samples, clusterCount, labels, criteria, 3, KMeansInitType.PPCenters, centers);

                int[] clusterCounts = new int[clusterCount];
                for (int i = 0; i < count; i++)
                {
                    clusterCounts[labels[i, 0]]++;
                }

                int dominantCluster = 0;
                int maxCount = clusterCounts[0];
                for (int i = 1; i < clusterCount; i++)
                {
                    if (clusterCounts[i] > maxCount)
                    {
                        maxCount = clusterCounts[i];
                        dominantCluster = i;
                    }
                }

                float b = centers[dominantCluster, 0];
                float g = centers[dominantCluster, 1];
                float r = centers[dominantCluster, 2];
                return new Bgr(b, g, r);
            }
        }

        /// <summary>
        /// Reconstructs the concentric ring layers based on the computed dimensions.
        /// </summary>
        private static List<LayerInfo> ReconstructLayers(int numLayers, double innerRadiusFinal,
            double outerRadius, double scaledDotSizeFinal)
        {
            List<LayerInfo> layersInfo = new List<LayerInfo>();
            for (int layer = 0; layer < numLayers; layer++)
            {
                double radiusLayer = innerRadiusFinal + (outerRadius - innerRadiusFinal) * (layer + 1) / numLayers;
                int numBits = radiusLayer > 0 ? (int)(2 * Math.PI * radiusLayer / (2 * scaledDotSizeFinal)) : 0;
                List<double> angles = new List<double>();
                if (numBits > 0)
                {
                    double angleStep = 2 * Math.PI / numBits;
                    for (int i = 0; i < numBits; i++)
                    {
                        angles.Add(i * angleStep);
                    }
                }
                layersInfo.Add(new LayerInfo(radiusLayer, angles));
            }
            return layersInfo;
        }

        /// <summary>
        /// Draws the reconstructed rings on the image and saves a debug overlay.
        /// </summary>
        private void SaveRingsOverlay(Image<Bgr, byte> image, Point center, List<LayerInfo> layersInfo, Bgr dominantColor)
        {
            for (int layer = 0; layer < layersInfo.Count; layer++)
            {
                double ringRadius = layersInfo[layer].Radius;
                image.Draw(new CircleF(new PointF(center.X, center.Y), (float)ringRadius), dominantColor, 2);
                encodingUtilities.LogDebug($"Ring {layer}: Radius = {ringRadius}, Positions = {layersInfo[layer].Angles.Count}");
            }
            string picturesDir = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            string debugFolder = Path.Combine(picturesDir, "Debug");
            Directory.CreateDirectory(debugFolder);
            string ringsOverlayPath = Path.Combine(debugFolder, "detected_circle_with_rings.png");
            image.Save(ringsOverlayPath);
        }

        /// <summary>
        /// Decodes the data rings (layers beyond the sync ring) from the distance map.
        /// In the binary image, white pixels indicate data hits (1). This version draws an overlay
        /// on the image with sample points and a debug sidebar showing per‑point details.
        /// </summary>
        private string DecodeDataRings(Image<Bgr, byte> origImage, Mat finalDistanceMap, Point center,
            List<LayerInfo> layersInfo, double rotationOffset, double scaledDotSizeFinal, double finalThreshold=170)
        {
            StringBuilder decodedBits = new StringBuilder();
            int w = origImage.Width;
            int h = origImage.Height;
            // Copy the original image to draw our data hit markers.
            Image<Bgr, byte> dataHitsImage = origImage.Copy();

            // Collect detailed debug info for each sample point.
            List<string> debugInfoLines = new List<string>();                       

            // Loop through each data ring (skip the first which is the sync ring).
            for (int layer = 1; layer < layersInfo.Count; layer++)
            {
                LayerInfo layerInfo = layersInfo[layer];
                int numPositions = layerInfo.Angles.Count;
                if (numPositions == 0)
                    continue;
                double angleStepLayer = 2 * Math.PI / numPositions;
                StringBuilder ringDecoded = new StringBuilder();

                for (int i = 0; i < numPositions; i++)
                {
                    // Calculate the sample point coordinates.
                    double canonicalAngle = i * angleStepLayer;
                    double actualAngle = canonicalAngle + rotationOffset;
                    int x = (int)(center.X + layerInfo.Radius * Math.Cos(actualAngle));
                    int y = (int)(center.Y + layerInfo.Radius * Math.Sin(actualAngle));

                    // Build a debug string for this sample.
                    string pointDebug = $"L{layer} P{i}: Angle={actualAngle:F2}, (x,y)=({x},{y})";
                    encodingUtilities.LogDebug(pointDebug);
                    if (x < 0 || x >= w || y < 0 || y >= h)
                    {
                        ringDecoded.Append("0");
                        dataHitsImage.Draw(new CircleF(new PointF(x, y), 3), new Bgr(Color.Red), -1);
                        pointDebug += " OutOfBounds -> 0";
                        debugInfoLines.Add(pointDebug);
                        continue;
                    }

                    // Define a small ROI around the sample point.
                    int x1 = Math.Max(0, x - 1);
                    int y1 = Math.Max(0, y - 1);
                    int x2 = Math.Min(w, x + 2);
                    int y2 = Math.Min(h, y + 2);
                    Rectangle roiRect = new Rectangle(x1, y1, x2 - x1, y2 - y1);

                    using (Mat roi = new Mat(finalDistanceMap, roiRect))
                    {
                        MCvScalar meanVal = CvInvoke.Mean(roi);
                        double avg = meanVal.V0;
                        // *** IMPORTANT: In a binary image where white (high value) indicates a hit,
                        // a data hit should be declared when the average is high.
                        // So we check if avg >= strictThreshold.
                        bool isDataHit = (avg >= finalThreshold);
                        ringDecoded.Append(isDataHit ? "1" : "0");

                        // Draw a marker: green for hit, red for miss.
                        Color markerColor = isDataHit ? Color.Green : Color.Red;
                        dataHitsImage.Draw(new CircleF(new PointF(x, y), 3), new Bgr(markerColor), -1);
                        CvInvoke.PutText(dataHitsImage, isDataHit ? "1" : "0", new Point(x + 4, y + 4),
                            FontFace.HersheySimplex, 0.5, new MCvScalar(255, 255, 255), 1);

                        pointDebug += $" Avg={avg:F2}, Threshold={finalThreshold:F2}, Hit={(isDataHit ? "1" : "0")}";
                        debugInfoLines.Add(pointDebug);
                        encodingUtilities.LogDebug(pointDebug);
                    }
                }
                encodingUtilities.LogDebug($"Data Ring {layer} decoded bits: {ringDecoded}");
                decodedBits.Append(ringDecoded);                
            }

            // Create a debug sidebar on the right with per-point debug info.
            int debugBarWidth = 300;
            Image<Bgr, byte> debugImage = new Image<Bgr, byte>(w + debugBarWidth, h);
            // Copy the dataHitsImage into the left side of the new image.
            debugImage.ROI = new Rectangle(0, 0, w, h);
            dataHitsImage.CopyTo(debugImage);
            debugImage.ROI = Rectangle.Empty;

            // Fill the sidebar area with black.
            Rectangle sidebarRect = new Rectangle(w, 0, debugBarWidth, h);
            debugImage.Draw(sidebarRect, new Bgr(Color.Black), -1);

            int margin = 5;
            int lineHeight = 15;
            int yPos = margin;
            foreach (string line in debugInfoLines)
            {
                CvInvoke.PutText(debugImage, line, new Point(w + margin, yPos),
                    FontFace.HersheySimplex, 0.4, new MCvScalar(255, 255, 255), 1);
                yPos += lineHeight;
                if (yPos > h - lineHeight) break;
            }

            // Save the combined debug image.
            string picturesDir = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            string debugFolder = Path.Combine(picturesDir, "Debug");
            Directory.CreateDirectory(debugFolder);
            string dataHitsPath = Path.Combine(debugFolder, $"data_hits_debug_{DateTime.Now:yyyyMMdd_HHmmssfff}.png");
            debugImage.Save(dataHitsPath);

            return decodedBits.ToString();
        }

        /// <summary>
        /// Decodes the data rings from the binary image. For each sample point, if the pixel is white then it's a 1;
        /// if it's black then it's a 0. Debug information and overlay images are saved.
        /// </summary>
        private string DecodeDataRingsSimple(Image<Bgr, byte> origImage, Mat finalDistanceMap, Point center,
            List<LayerInfo> layersInfo, double rotationOffset, double scaledDotSizeFinal)
        {
            StringBuilder decodedBits = new StringBuilder();
            int width = origImage.Width;
            int height = origImage.Height;

            // Copy the original image to draw data hit markers.
            Image<Bgr, byte> dataHitsImage = origImage.Copy();

            // Convert the Mat to a binary grayscale image once.
            Image<Gray, byte> binaryImage = finalDistanceMap.ToImage<Gray, byte>();

            // List to collect per-point debug information.
            List<string> debugInfoLines = new List<string>();

            // Loop through each data ring (skip the first sync ring, i.e. layer 0).
            for (int layer = 1; layer < layersInfo.Count; layer++)
            {
                LayerInfo layerInfo = layersInfo[layer];
                int numPositions = layerInfo.Angles.Count;
                if (numPositions == 0)
                    continue;

                double angleStepLayer = 2 * Math.PI / numPositions;
                StringBuilder ringDecoded = new StringBuilder();

                for (int i = 0; i < numPositions; i++)
                {
                    // Calculate sample point coordinates.
                    double canonicalAngle = i * angleStepLayer;
                    double actualAngle = canonicalAngle + rotationOffset;
                    int x = (int)(center.X + layerInfo.Radius * Math.Cos(actualAngle));
                    int y = (int)(center.Y + layerInfo.Radius * Math.Sin(actualAngle));

                    // Prepare debug info for this point.
                    string pointDebug = $"L{layer} P{i}: Angle={actualAngle:F2}, (x,y)=({x},{y})";
                    encodingUtilities.LogDebug(pointDebug);
                    // Check for out-of-bounds.
                    if (x < 0 || x >= width || y < 0 || y >= height)
                    {
                        ringDecoded.Append("0");
                        dataHitsImage.Draw(new CircleF(new PointF(x, y), 3), new Bgr(Color.Red), -1);                        
                        continue;
                    }

                    // Get the pixel value directly (0=black, 255=white).
                    byte pixelVal = binaryImage.Data[y, x, 0];
                    bool isDataHit = pixelVal > 127; // White pixel indicates a hit ("1")

                    ringDecoded.Append(isDataHit ? "1" : "0");

                    // Draw a marker on the image: green for a hit and red for a miss.
                    Color markerColor = isDataHit ? Color.Green : Color.Red;
                    dataHitsImage.Draw(new CircleF(new PointF(x, y), 3), new Bgr(markerColor), -1);
                    CvInvoke.PutText(dataHitsImage, isDataHit ? "1" : "0", new Point(x + 4, y + 4),
                        FontFace.HersheySimplex, 0.5, new MCvScalar(255, 255, 255), 1);

                    pointDebug = $" PixelVal={pixelVal}, Hit={(isDataHit ? "1" : "0")}";
                    encodingUtilities.LogDebug(pointDebug);
                    debugInfoLines.Add(pointDebug);
                }
                encodingUtilities.LogDebug($"Data Ring {layer} decoded bits: {ringDecoded}");
                decodedBits.Append(ringDecoded);
            }

            // Create a debug sidebar with the per-point debug information.
            int debugBarWidth = 300;
            Image<Bgr, byte> debugImage = new Image<Bgr, byte>(width + debugBarWidth, height);
            // Place the dataHitsImage on the left.
            debugImage.ROI = new Rectangle(0, 0, width, height);
            dataHitsImage.CopyTo(debugImage);
            debugImage.ROI = Rectangle.Empty;

            // Fill the sidebar area with black.
            Rectangle sidebarRect = new Rectangle(width, 0, debugBarWidth, height);
            debugImage.Draw(sidebarRect, new Bgr(Color.Black), -1);

            int margin = 5;
            int lineHeight = 15;
            int yPos = margin;
            foreach (string line in debugInfoLines)
            {
                CvInvoke.PutText(debugImage, line, new Point(width + margin, yPos),
                    FontFace.HersheySimplex, 0.4, new MCvScalar(255, 255, 255), 1);
                yPos += lineHeight;
                if (yPos > height - lineHeight)
                    break;
            }

            // Save the combined debug image.
            string picturesDir = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            string debugFolder = Path.Combine(picturesDir, "Debug");
            Directory.CreateDirectory(debugFolder);
            string dataHitsPath = Path.Combine(debugFolder, $"data_hits_debug_{DateTime.Now:yyyyMMdd_HHmmssfff}.png");
            debugImage.Save(dataHitsPath);

            return decodedBits.ToString();
        }






        #endregion                

        // (Optional) A helper method to save any debug image.
        private static void saveDebugImage(Image<Bgr, byte> image, string imageName)
        {
            string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            string debugFolderPath = Path.Combine(documentsPath, "IDC_DebugImages");
            Directory.CreateDirectory(debugFolderPath);
            string debugImagePath = Path.Combine(debugFolderPath, $"{imageName}_{DateTime.Now:yyyyMMdd_HHmmssfff}.png");
            image.Save(debugImagePath);
        }
    }
}
