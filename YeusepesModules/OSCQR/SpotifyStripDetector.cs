using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace YeusepesModules.OSCQR
{
    /// <summary>
    /// YOLOv8 ONNX detector (github.com/Yeusepe/spcode-detector) that finds Spotify Code strips in a frame.
    /// </summary>
    internal sealed class SpotifyStripDetector : IDisposable
    {
        private readonly InferenceSession _session;
        private readonly string _inputName;
        private readonly int _imgsz;

        private SpotifyStripDetector(InferenceSession session)
        {
            _session = session;
            var input = session.InputMetadata.First();
            _inputName = input.Key;
            _imgsz = input.Value.Dimensions[2] > 0 ? input.Value.Dimensions[2] : 448;
        }

        public static SpotifyStripDetector TryCreate(string onnxPath, Action<string> log = null)
        {
            try
            {
                if (!File.Exists(onnxPath))
                {
                    log?.Invoke($"SpotifyStripDetector: ONNX not found: {onnxPath}");
                    return null;
                }
                // One thread: runs on the (EcoQoS, below-normal) capture thread instead of spiking every core.
                var options = new SessionOptions { IntraOpNumThreads = 1, InterOpNumThreads = 1, ExecutionMode = ExecutionMode.ORT_SEQUENTIAL };
                return new SpotifyStripDetector(new InferenceSession(onnxPath, options));
            }
            catch (Exception ex)
            {
                log?.Invoke($"SpotifyStripDetector: Failed to load ONNX: {ex.Message}");
                return null;
            }
        }

        /// <summary>Returns detected strips (padded so bar tips aren't clipped), best score first.</summary>
        public List<Rectangle> Detect(Bitmap src, float confThresh = 0.25f, float iouThresh = 0.45f)
        {
            int W = src.Width, H = src.Height;
            float scale = (float)_imgsz / Math.Max(W, H);
            int nw = Math.Max(1, (int)(W * scale)), nh = Math.Max(1, (int)(H * scale));
            int padLeft = (_imgsz - nw) / 2, padTop = (_imgsz - nh) / 2;

            var input = new DenseTensor<float>(new[] { 1, 3, _imgsz, _imgsz });
            using (var canvas = new Bitmap(_imgsz, _imgsz, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(canvas))
                {
                    g.Clear(Color.FromArgb(114, 114, 114));
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                    g.DrawImage(src, padLeft, padTop, nw, nh);
                }
                var data = canvas.LockBits(new Rectangle(0, 0, _imgsz, _imgsz), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    var row = new byte[_imgsz * 4];
                    for (int y = 0; y < _imgsz; y++)
                    {
                        Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                        for (int x = 0; x < _imgsz; x++)
                        {
                            input[0, 0, y, x] = row[x * 4 + 2] / 255f; // BGRA -> RGB
                            input[0, 1, y, x] = row[x * 4 + 1] / 255f;
                            input[0, 2, y, x] = row[x * 4] / 255f;
                        }
                    }
                }
                finally { canvas.UnlockBits(data); }
            }

            using var results = _session.Run(new[] { NamedOnnxValue.CreateFromTensor(_inputName, input) });
            var output = results.First().AsTensor<float>(); // YOLOv8: (1, 4 + classes, N)
            int channels = output.Dimensions[1], nPred = output.Dimensions[2];

            var cand = new List<(float s, Rectangle r)>();
            for (int i = 0; i < nPred; i++)
            {
                float score = 0;
                for (int c = 4; c < channels; c++) score = Math.Max(score, output[0, c, i]);
                if (score < confThresh) continue;

                float cx = (output[0, 0, i] - padLeft) / scale, cy = (output[0, 1, i] - padTop) / scale;
                float w = output[0, 2, i] / scale, h = output[0, 3, i] / scale;
                // Pad so end bars / tall bar tips the box clipped are included.
                float padX = w * 0.1f + h * 0.15f, padY = h * 0.15f;
                var r = Rectangle.FromLTRB(
                    Math.Max(0, (int)(cx - w / 2 - padX)), Math.Max(0, (int)(cy - h / 2 - padY)),
                    Math.Min(W, (int)(cx + w / 2 + padX)), Math.Min(H, (int)(cy + h / 2 + padY)));
                if (r.Width > 0 && r.Height > 0) cand.Add((score, r));
            }

            cand.Sort((a, b) => b.s.CompareTo(a.s));
            var keep = new List<Rectangle>();
            foreach (var c in cand)
                if (keep.All(k => IoU(c.r, k) <= iouThresh)) keep.Add(c.r);
            return keep;
        }

        private static float IoU(Rectangle a, Rectangle b)
        {
            var i = Rectangle.Intersect(a, b);
            float inter = i.IsEmpty ? 0 : (float)i.Width * i.Height;
            return inter / ((float)a.Width * a.Height + (float)b.Width * b.Height - inter + 1e-9f);
        }

        public void Dispose() => _session.Dispose();
    }
}
