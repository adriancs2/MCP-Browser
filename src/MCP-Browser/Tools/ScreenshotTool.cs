using MCP_Browser.Browser;
using MCP_Browser.Core;
using Newtonsoft.Json.Linq;
using System;

namespace MCP_Browser.Tools
{
    public class ScreenshotTool : BrowserTool
    {
        public ScreenshotTool(BrowserHost host) : base(host) { }

        public override string Name => "browser_screenshot";
        public override string Description =>
            "Capture a screenshot of the current page and return it as an image you can see. " +
            "If the user minimized the browser window it is restored first (without stealing focus), because a minimized window cannot render. " +
            "Use 'jpg' (default) with a moderate 'quality' and a capped 'max_width' to keep the image small and save token context " +
            "(token cost is roughly width*height/750). Set full_page=true to capture the entire scrollable page rather than just the viewport. " +
            "The text result reports the rendered image size and the viewport size, so you can map a pixel in this image back to a " +
            "browser_click_at coordinate (divide the image pixel by the reported scale factor).";

        public override ToolParamList Params => new ToolParamList()
            .StringEnum("format", "Image format. 'jpg' is smaller; 'png' is lossless.", new[] { "jpg", "png" })
            .Int("max_width", "Maximum width in pixels; the image is downscaled to fit (0 = no cap). Default from config (~1400).")
            .Int("quality", "JPEG quality 1-100 (ignored for png). Lower = smaller. Default from config (~70).")
            .Number("scale", "Browser zoom factor applied before capture (e.g. 0.75 renders more content smaller). Persists for later captures.")
            .Bool("full_page", "Capture the entire scrollable page instead of just the visible viewport. Default: false.", defaultValue: false);

        public override ToolResult Execute(JObject args)
        {
            string format = args.Value<string>("format") ?? McpConfig.DefaultScreenshotFormat;
            int maxWidth = args.Value<int?>("max_width") ?? McpConfig.DefaultScreenshotMaxWidth;
            int quality = args.Value<int?>("quality") ?? McpConfig.DefaultJpegQuality;
            bool fullPage = args.Value<bool?>("full_page") ?? false;
            double? scale = args["scale"] != null ? (double?)args.Value<double>("scale") : null;

            if (scale.HasValue)
            {
                double s = scale.Value;
                if (s < 0.1 || s > 5.0)
                    return ToolResult.Error("INVALID_PARAMS", "scale must be between 0.1 and 5.0");
                Host.RunOnUi(() => Host.SetZoomAsync(s));
            }

            byte[] pngBytes = Host.RunOnUi(() => Host.CaptureAsync(fullPage));
            string viewportRaw = Host.RunOnUi(() => Host.GetViewportInfoAsync());

            // ExecuteScriptAsync returns the JS string return value as a JSON-encoded
            // string (i.e. wrapped in quotes), so decode one layer before parsing.
            string viewportJson = viewportRaw;
            try { viewportJson = Newtonsoft.Json.JsonConvert.DeserializeObject<string>(viewportRaw) ?? viewportRaw; }
            catch { }

            ImageUtil.Encoded enc = ImageUtil.Process(pngBytes, format, maxWidth, quality);
            string base64 = Convert.ToBase64String(enc.Bytes);

            double scaleX = 0, scaleY = 0;
            try
            {
                var vp = JObject.Parse(viewportJson);
                int innerW = vp["innerWidth"]?.Value<int>() ?? 0;
                int innerH = vp["innerHeight"]?.Value<int>() ?? 0;
                if (!fullPage && innerW > 0) scaleX = (double)enc.Width / innerW;
                if (!fullPage && innerH > 0) scaleY = (double)enc.Height / innerH;
            }
            catch { }

            string info =
                $"Screenshot captured ({enc.MimeType}, {enc.Width}x{enc.Height}px, {enc.Bytes.Length / 1024.0:F1} KB).\n" +
                $"Viewport: {viewportJson}\n" +
                (fullPage
                    ? "full_page=true: image spans the whole page; to click, scroll the target into view and use viewport coordinates."
                    : $"Image-to-viewport scale: x={scaleX:F4}, y={scaleY:F4}. To click a spot at image pixel (ix,iy), call browser_click_at with x=ix/{(scaleX > 0 ? scaleX.ToString("F4") : "scaleX")}, y=iy/{(scaleY > 0 ? scaleY.ToString("F4") : "scaleY")}.");

            return ToolResult.SuccessWithImage(info, base64, enc.MimeType);
        }
    }
}
