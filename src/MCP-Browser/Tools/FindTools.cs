using MCP_Browser.Browser;
using MCP_Browser.Core;
using Newtonsoft.Json.Linq;
using System;

namespace MCP_Browser.Tools
{
    public class FindTextTool : BrowserTool
    {
        public FindTextTool(BrowserHost host) : base(host) { }

        public override string Name => "browser_find_text";
        public override string Description =>
            "Find visible elements on the page by their rendered text — use this when a page has generated/hashed class names " +
            "and no stable CSS selector. Matches innerText, and falls back to aria-label, title, value or alt. " +
            "Returns a JSON array of candidates, each with a generated unique CSS selector (usable with browser_click / browser_type) " +
            "and viewport centre coordinates (usable with browser_click_at). Nested matches collapse to the innermost element. " +
            "By default only clickable elements (links, buttons, [role=button], submit inputs) are searched; set clickable_only=false " +
            "to search all elements, e.g. to locate a heading or a table cell.";

        public override ToolParamList Params => new ToolParamList()
            .String("text", "Visible text to search for.", required: true)
            .Bool("exact", "Require the whole normalised text to equal 'text' rather than merely contain it. Default: false.", defaultValue: false)
            .Bool("clickable_only", "Restrict the search to clickable elements. Default: true.", defaultValue: true);

        public override ToolResult Execute(JObject args)
        {
            string text = args.Value<string>("text");
            if (string.IsNullOrWhiteSpace(text))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'text' parameter");

            bool exact = args.Value<bool?>("exact") ?? false;
            bool clickableOnly = args.Value<bool?>("clickable_only") ?? true;

            string json = Host.RunOnUi(() => Host.FindByTextAsync(text, exact, clickableOnly));

            int count = 0;
            try { count = JArray.Parse(json).Count; } catch { }

            if (count == 0)
                return ToolResult.Error("NOT_FOUND",
                    $"No visible element matched text '{text}'." +
                    (clickableOnly ? " Only clickable elements were searched — set clickable_only=false to widen the search." : ""));

            return ToolResult.Success($"{count} match(es) for '{text}':\n{json}");
        }
    }

    public class FindFieldTool : BrowserTool
    {
        public FindFieldTool(BrowserHost host) : base(host) { }

        public override string Name => "browser_find_field";
        public override string Description =>
            "Find form fields by their visible label/caption rather than by CSS selector — the counterpart to browser_find_text " +
            "for inputs. Use it to see what browser_type's 'label' parameter would resolve to, or to disambiguate when a label " +
            "matched several fields. Each candidate reports 'matched_by' (label-for, ancestor-label, aria-label, placeholder, " +
            "title, table-cell, preceding-text, ancestor-preceding-text, parent-text or name-or-id) so you can judge how solid the match is, plus a unique " +
            "CSS selector, the current value, and centre coordinates. Results are ordered best match first.";

        public override ToolParamList Params => new ToolParamList()
            .String("label", "Visible label or caption of the field.", required: true)
            .Bool("exact", "Require an exact whole-label match rather than a substring match. Default: false.", defaultValue: false);

        public override ToolResult Execute(JObject args)
        {
            string label = args.Value<string>("label");
            if (string.IsNullOrWhiteSpace(label))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'label' parameter");

            bool exact = args.Value<bool?>("exact") ?? false;
            string json = Host.RunOnUi(() => Host.FindFieldByLabelAsync(label, exact));

            int count = 0;
            try { count = JArray.Parse(json).Count; } catch { }

            if (count == 0)
                return ToolResult.Error("NOT_FOUND",
                    $"No visible form field matched label '{label}'. Use browser_get_inputs to list every field on this page.");

            return ToolResult.Success($"{count} field(s) matched '{label}':\n{json}");
        }
    }

    public class ClickTextTool : BrowserTool
    {
        public ClickTextTool(BrowserHost host) : base(host) { }

        public override string Name => "browser_click_text";
        public override string Description =>
            "Click an element by its visible text (e.g. \"Submit\", \"Sign in\", \"Next\") instead of a CSS selector. " +
            "Scrolls the element into view and dispatches a real mouse click at its centre, so frameworks receive genuine events. " +
            "If several elements match and no 'index' is given this returns an error listing the candidates rather than guessing — " +
            "re-call with 'index' (or exact=true) to disambiguate.";

        public override ToolParamList Params => new ToolParamList()
            .String("text", "Visible text of the element to click.", required: true)
            .Bool("exact", "Require an exact whole-text match rather than a substring match. Default: false.", defaultValue: false)
            .Bool("clickable_only", "Restrict the search to clickable elements. Default: true.", defaultValue: true)
            .Int("index", "Which match to click when several matched, as reported by browser_find_text. Omit when exactly one match is expected.")
            .StringEnum("button", "Mouse button.", new[] { "left", "right", "middle" }, defaultValue: "left")
            .Int("click_count", "Number of clicks (2 = double-click).", defaultValue: 1);

        public override ToolResult Execute(JObject args)
        {
            string text = args.Value<string>("text");
            if (string.IsNullOrWhiteSpace(text))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'text' parameter");

            bool exact = args.Value<bool?>("exact") ?? false;
            bool clickableOnly = args.Value<bool?>("clickable_only") ?? true;
            int index = args.Value<int?>("index") ?? -1;
            string button = args.Value<string>("button") ?? "left";
            int clickCount = args.Value<int?>("click_count") ?? 1;

            try
            {
                string r = Host.RunOnUi(() => Host.ClickByTextAsync(text, exact, clickableOnly, index, button, clickCount));
                return ToolResult.Success(r);
            }
            catch (ArgumentException ex)
            {
                return ToolResult.Error("NOT_FOUND_OR_AMBIGUOUS", ex.Message);
            }
        }
    }
}
