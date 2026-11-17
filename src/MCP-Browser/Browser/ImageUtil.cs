using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace MCP_Browser.Browser
{
    /// <summary>
    /// Downscales and re-encodes raw PNG screenshot bytes to keep image size
    /// (and therefore the AI's token cost) under control.
    /// Token cost is roughly (width * height) / 750, so capping the long edge
    /// and using JPEG with a moderate quality dramatically reduces context use.
    /// </summary>
    public static class ImageUtil
    {
        public class Encoded
        {
            public byte[] Bytes;
            public string MimeType;
            public int Width;
            public int Height;
        }

        public static Encoded Process(byte[] sourcePng, string format, int maxWidth, int jpegQuality)
        {
            using (var srcMs = new MemoryStream(sourcePng))
            using (var src = (Bitmap)Image.FromStream(srcMs))
            {
                int w = src.Width;
                int h = src.Height;

                Bitmap working = src;
                bool disposeWorking = false;

                if (maxWidth > 0 && w > maxWidth)
                {
                    double ratio = (double)maxWidth / w;
                    int nw = maxWidth;
                    int nh = Math.Max(1, (int)Math.Round(h * ratio));
                    var resized = new Bitmap(nw, nh, PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(resized))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.SmoothingMode = SmoothingMode.HighQuality;
                        g.DrawImage(src, 0, 0, nw, nh);
                    }
                    working = resized;
                    disposeWorking = true;
                    w = nw;
                    h = nh;
                }

                try
                {
                    var outMs = new MemoryStream();
                    string mime;

                    bool jpeg = string.Equals(format, "jpg", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(format, "jpeg", StringComparison.OrdinalIgnoreCase);

                    if (jpeg)
                    {
                        // JPEG has no alpha; flatten onto white.
                        using (var flat = new Bitmap(w, h, PixelFormat.Format24bppRgb))
                        {
                            using (var g = Graphics.FromImage(flat))
                            {
                                g.Clear(Color.White);
                                g.DrawImage(working, 0, 0, w, h);
                            }
                            var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                            var encParams = new EncoderParameters(1);
                            int q = Math.Max(1, Math.Min(100, jpegQuality));
                            encParams.Param[0] = new EncoderParameter(Encoder.Quality, (long)q);
                            flat.Save(outMs, codec, encParams);
                        }
                        mime = "image/jpeg";
                    }
                    else
                    {
                        working.Save(outMs, ImageFormat.Png);
                        mime = "image/png";
                    }

                    return new Encoded { Bytes = outMs.ToArray(), MimeType = mime, Width = w, Height = h };
                }
                finally
                {
                    if (disposeWorking)
                        working.Dispose();
                }
            }
        }
    }
}
