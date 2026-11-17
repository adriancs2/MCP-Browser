using MCP_Browser.Browser;
using MCP_Browser.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;

namespace MCP_Browser.Tools
{
    public class ClickTool : BrowserTool
    {
        public ClickTool(BrowserHost host) : base(host) { }

        public override string Name => "browser_click";
        public override string Description =>
            "Click an element matched by a CSS selector (scrolls it into view first). For clicking a precise pixel location " +
            "from a screenshot instead, use browser_click_at.";

        public override ToolParamList Params => new ToolParamList()
            .String("selector", "CSS selector of the element to click.", required: true);

        public override ToolResult Execute(JObject args)
        {
            string selector = args.Value<string>("selector");
            if (string.IsNullOrWhiteSpace(selector))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'selector' parameter");

            string sel = JsonConvert.SerializeObject(selector);
            string js = "(function(){var el=document.querySelector(" + sel + ");if(!el)return 'NOTFOUND';" +
                        "el.scrollIntoView({block:'center',inline:'center'});el.click();return 'OK';})()";
            string r = Host.RunOnUi(() => Host.ExecuteScriptAsync(js));
            if (r == "\"NOTFOUND\"")
                return ToolResult.Error("NOT_FOUND", $"No element matched selector '{selector}'.");
            return ToolResult.Success($"Clicked element '{selector}'.");
        }
    }

    public class ClickAtTool : BrowserTool
    {
        public ClickAtTool(BrowserHost host) : base(host) { }

        public override string Name => "browser_click_at";
        public override string Description =>
            "Perform a virtual mouse click at a pixel location in the current viewport — exactly like a user clicking that spot, " +
            "independent of any DOM selector. Coordinates are CSS pixels measured from the top-left of the viewport. " +
            "When clicking a position seen in a screenshot that was downscaled (via max_width/scale), divide the screenshot pixel " +
            "by the reported scale factor first (screenshot output reports both rendered size and the viewport size).";

        public override ToolParamList Params => new ToolParamList()
            .Number("x", "X coordinate in viewport CSS pixels (from the left edge).", required: true)
            .Number("y", "Y coordinate in viewport CSS pixels (from the top edge).", required: true)
            .StringEnum("button", "Mouse button.", new[] { "left", "right", "middle" }, defaultValue: "left")
            .Int("click_count", "Number of clicks (2 = double-click).", defaultValue: 1);

        public override ToolResult Execute(JObject args)
        {
            if (args["x"] == null || args["y"] == null)
                return ToolResult.Error("INVALID_PARAMS", "Both 'x' and 'y' are required.");
            double x = args.Value<double>("x");
            double y = args.Value<double>("y");
            string button = args.Value<string>("button") ?? "left";
            int clickCount = args.Value<int?>("click_count") ?? 1;

            string r = Host.RunOnUi(() => Host.ClickAtAsync(x, y, button, clickCount));
            return ToolResult.Success(r);
        }
    }

    public class TypeTool : BrowserTool
    {
        public TypeTool(BrowserHost host) : base(host) { }

        public override string Name => "browser_type";
        public override string Description =>
            "Type text into an input/textarea. Target the field EITHER by 'selector' (CSS) OR by 'label' — the field's visible " +
            "caption, which is what to use when a page has generated or unstable ids. Label matching tries, in order of reliability: " +
            "<label for>, a wrapping <label>, aria-label/aria-labelledby, placeholder, title, the table cell to the left, nearby " +
            "preceding text, a caption preceding a wrapping container, then name/id. A uniquely best match is used; if several are equally good the call fails with the " +
            "candidate list instead of typing into the wrong box. Sets the value and dispatches 'input' and 'change' events so " +
            "frameworks (React/Vue/etc.) register the change.";

        public override ToolParamList Params => new ToolParamList()
            .String("selector", "CSS selector of the input or textarea. Provide this or 'label'.")
            .String("label", "Visible label/caption of the field. Provide this or 'selector'.")
            .String("text", "Text to type.", required: true)
            .Bool("exact", "Require an exact whole-label match rather than a substring match. Default: false.", defaultValue: false)
            .Int("index", "Which match to use when several fields matched the label, as reported by browser_find_field.")
            .Bool("clear", "Clear the field before typing. Default: true.", defaultValue: true);

        public override ToolResult Execute(JObject args)
        {
            string selector = args.Value<string>("selector");
            string label = args.Value<string>("label");
            string text = args.Value<string>("text");

            bool hasSelector = !string.IsNullOrWhiteSpace(selector);
            bool hasLabel = !string.IsNullOrWhiteSpace(label);

            if (hasSelector == hasLabel)
                return ToolResult.Error("INVALID_PARAMS",
                    hasSelector
                        ? "Provide either 'selector' or 'label', not both."
                        : "Missing target: provide either 'selector' or 'label'.");

            if (hasLabel)
            {
                bool exact = args.Value<bool?>("exact") ?? false;
                int index = args.Value<int?>("index") ?? -1;
                try
                {
                    selector = Host.RunOnUi(() => Host.ResolveFieldSelectorAsync(label, exact, index));
                }
                catch (ArgumentException ex)
                {
                    return ToolResult.Error("NOT_FOUND_OR_AMBIGUOUS", ex.Message);
                }
            }

            if (text == null) text = "";
            bool clear = args.Value<bool?>("clear") ?? true;

            string sel = JsonConvert.SerializeObject(selector);
            string val = JsonConvert.SerializeObject(text);
            string js =
                "(function(){var el=document.querySelector(" + sel + ");if(!el)return 'NOTFOUND';el.scrollIntoView({block:'center',inline:'center'});el.focus();" +
                "if(el.isContentEditable){if(" + (clear ? "true" : "false") + ")el.textContent='';el.textContent+=" + val + ";}" +
                "else{if(" + (clear ? "true" : "false") + ")el.value='';el.value=(el.value||'')+" + val + ";}" +
                "el.dispatchEvent(new Event('input',{bubbles:true}));el.dispatchEvent(new Event('change',{bubbles:true}));return 'OK';})()";
            string r = Host.RunOnUi(() => Host.ExecuteScriptAsync(js));
            if (r == "\"NOTFOUND\"")
                return ToolResult.Error("NOT_FOUND", $"No element matched selector '{selector}'.");
            return ToolResult.Success($"Typed into '{selector}'.");
        }
    }

    public class UploadFileTool : BrowserTool
    {
        public UploadFileTool(BrowserHost host) : base(host) { }

        public override string Name => "browser_upload_file";
        public override string Description =>
            "Set the file of a <input type=file> element (matched by CSS selector) to a local file path. Use this to upload files via forms.";

        public override ToolParamList Params => new ToolParamList()
            .String("selector", "CSS selector of the file input element.", required: true)
            .String("file_path", "Full local path of the file to upload.", required: true);

        public override ToolResult Execute(JObject args)
        {
            string selector = args.Value<string>("selector");
            string path = args.Value<string>("file_path");
            if (string.IsNullOrWhiteSpace(selector))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'selector' parameter");
            if (string.IsNullOrWhiteSpace(path))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'file_path' parameter");
            if (!File.Exists(path))
                return ToolResult.Error("FILE_NOT_FOUND", $"File not found: {path}");

            string r = Host.RunOnUi(() => Host.SetFileInputAsync(selector, path));
            return ToolResult.Success(r);
        }
    }
}
