using MCP_Browser.Browser;
using MCP_Browser.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MCP_Browser.Tools
{
    public class ExecuteJsTool : BrowserTool
    {
        public ExecuteJsTool(BrowserHost host) : base(host) { }

        public override string Name => "browser_execute_js";
        public override string Description =>
            "Execute (inject) a custom JavaScript snippet in the current page and return its result as JSON. " +
            "The script runs in the page's context. To return a value, make it the last expression " +
            "(e.g. wrap in '(function(){ ...; return X; })()'). For asynchronous work that returns a Promise, " +
            "use browser_http_request or set await_promise=true.";

        public override ToolParamList Params => new ToolParamList()
            .String("script", "JavaScript source to run in the page.", required: true)
            .Bool("await_promise", "If true, evaluate via CDP and await a returned Promise before returning the value. Default: false.", defaultValue: false);

        public override ToolResult Execute(JObject args)
        {
            string script = args.Value<string>("script");
            if (string.IsNullOrWhiteSpace(script))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'script' parameter");
            bool awaitPromise = args.Value<bool?>("await_promise") ?? false;

            if (!awaitPromise)
            {
                string r = Host.RunOnUi(() => Host.ExecuteScriptAsync(script));
                return ToolResult.Success(r ?? "null");
            }

            string cdp = Host.RunOnUi(() => Host.CdpEvaluateAsync(script, true));
            return ToolResult.Success(CdpResult.Extract(cdp));
        }
    }

    public class GetTextTool : BrowserTool
    {
        public GetTextTool(BrowserHost host) : base(host) { }

        public override string Name => "browser_get_text";
        public override string Description =>
            "Get the visible text (innerText) of the page or of a specific element by CSS selector. Useful for reading page content without a screenshot.";

        public override ToolParamList Params => new ToolParamList()
            .String("selector", "Optional CSS selector. If omitted, returns the whole document body text.")
            .Int("max_chars", "Maximum characters to return (truncates). Default: 20000.", defaultValue: 20000);

        public override ToolResult Execute(JObject args)
        {
            string selector = args.Value<string>("selector");
            int maxChars = args.Value<int?>("max_chars") ?? 20000;

            string js;
            if (string.IsNullOrWhiteSpace(selector))
                js = "(function(){return (document.body?document.body.innerText:'')||'';})()";
            else
            {
                string sel = JsonConvert.SerializeObject(selector);
                js = "(function(){var el=document.querySelector(" + sel + ");return el?(el.innerText||el.textContent||''):'__NOTFOUND__';})()";
            }

            string raw = Host.RunOnUi(() => Host.ExecuteScriptAsync(js));
            string text = DecodeJsString(raw);
            if (text == "__NOTFOUND__")
                return ToolResult.Error("NOT_FOUND", $"No element matched selector '{selector}'.");
            if (text != null && text.Length > maxChars)
                text = text.Substring(0, maxChars) + "\n... [truncated]";
            return ToolResult.Success(text ?? "");
        }

        private static string DecodeJsString(string json)
        {
            if (string.IsNullOrEmpty(json)) return "";
            try { return JsonConvert.DeserializeObject<string>(json) ?? json; }
            catch { return json; }
        }
    }

    public class GetInputsTool : BrowserTool
    {
        public GetInputsTool(BrowserHost host) : base(host) { }

        public override string Name => "browser_get_inputs";
        public override string Description =>
            "List visible interactive form fields and ARIA controls on the page (input, textarea, select, button, contenteditable, " +
            "and ARIA roles like button/textbox/combobox/option/menuitem/tab). Skips hidden inputs by default (except <input type=file>) " +
            "to keep output compact. Optionally restrict to a container by CSS selector.";

        public override ToolParamList Params => new ToolParamList()
            .String("container", "Optional CSS selector to scope the search to a container (e.g. a specific form).")
            .Bool("include_hidden", "Include hidden/off-screen elements and <input type=hidden>. Default: false.", defaultValue: false);

        public override ToolResult Execute(JObject args)
        {
            string container = args.Value<string>("container");
            bool includeHidden = args.Value<bool?>("include_hidden") ?? false;
            string rootExpr = string.IsNullOrWhiteSpace(container)
                ? "document"
                : "document.querySelector(" + JsonConvert.SerializeObject(container) + ")";

            string js =
                "(function(){var root=" + rootExpr + ",incHidden=" + (includeHidden ? "true" : "false") + ";if(!root)return JSON.stringify({error:'container not found'});" +
                "function norm(s){return (s||'').replace(/\\s+/g,' ').trim();}" +
                "function vis(e){if(incHidden)return true;var t=(e.getAttribute('type')||'').toLowerCase();if(t==='hidden')return false;if(t==='file')return true;" +
                "var r=e.getBoundingClientRect();if(r.width<=0||r.height<=0)return false;var st=window.getComputedStyle(e);return st.visibility!=='hidden'&&st.display!=='none';}" +
                "function one(s){try{return document.querySelectorAll(s).length===1;}catch(x){return false;}}" +
                "function sel(e){var t=e.tagName.toLowerCase();" +
                "if(e.id&&one('#'+CSS.escape(e.id)))return '#'+CSS.escape(e.id);" +
                "if(e.name){var s=t+'[name=\"'+CSS.escape(e.name)+'\"]';if(one(s))return s;}" +
                "var al=e.getAttribute('aria-label');if(al){var sa=t+'[aria-label=\"'+CSS.escape(al)+'\"]';if(one(sa))return sa;}" +
                "var parts=[],n=e;while(n&&n.nodeType===1&&n!==document.documentElement){" +
                "if(n!==e&&n.id&&one('#'+CSS.escape(n.id))){parts.unshift('#'+CSS.escape(n.id));break;}" +
                "var p=n.tagName.toLowerCase(),par=n.parentElement;if(par){var same=Array.prototype.filter.call(par.children,function(c){return c.tagName===n.tagName;});if(same.length>1)p+=':nth-of-type('+(same.indexOf(n)+1)+')';}" +
                "parts.unshift(p);if(one(parts.join(' > ')))break;n=par;}" +
                "return parts.join(' > ');}" +
                "function label(e){" +
                "if(e.id){var l=document.querySelector('label[for=\"'+CSS.escape(e.id)+'\"]');if(l){var lt=norm(l.innerText);if(lt)return lt;}}" +
                "var p=e.closest('label');if(p){var pt=norm(p.innerText);if(pt)return pt;}" +
                "var al=norm(e.getAttribute('aria-label'));if(al)return al;" +
                "var ab=e.getAttribute('aria-labelledby');if(ab){var ael=document.getElementById(ab);if(ael){var at=norm(ael.innerText);if(at)return at;}}" +
                "var ti=norm(e.getAttribute('title'));if(ti)return ti;" +
                "var tag=e.tagName.toLowerCase(),role=(e.getAttribute('role')||'').toLowerCase();" +
                "if(tag==='button'||role==='button'||role==='menuitem'||role==='option'||role==='tab'){return norm(e.innerText||e.textContent).slice(0,120);}" +
                "return '';}" +
                "var q='input,textarea,select,button,[contenteditable=\"true\"],[role=\"button\"],[role=\"textbox\"],[role=\"combobox\"],[role=\"checkbox\"],[role=\"radio\"],[role=\"switch\"],[role=\"option\"],[role=\"menuitem\"],[role=\"tab\"]';" +
                "var els=root.querySelectorAll(q);var out=[];" +
                "for(var i=0;i<els.length;i++){var e=els[i];if(!vis(e))continue;" +
                "out.push({tag:e.tagName.toLowerCase(),type:e.getAttribute('type')||'',role:e.getAttribute('role')||'',name:e.name||'',id:e.id||'',placeholder:e.placeholder||'',value:(e.value!==undefined?String(e.value).slice(0,120):''),label:label(e).slice(0,120),selector:sel(e)});}" +
                "return JSON.stringify(out);})()";

            string raw = Host.RunOnUi(() => Host.ExecuteScriptAsync(js));
            string decoded;
            try { decoded = JsonConvert.DeserializeObject<string>(raw) ?? raw; }
            catch { decoded = raw; }
            return ToolResult.Success(decoded);
        }
    }

    public class HttpRequestTool : BrowserTool
    {
        public HttpRequestTool(BrowserHost host) : base(host) { }

        public override string Name => "browser_http_request";
        public override string Description =>
            "Make an HTTP request (GET, POST, etc.) from within the current page's context using fetch — so the browser's cookies, " +
            "session, and origin apply. Supports JSON and form (application/x-www-form-urlencoded) bodies via the 'body' string and 'headers'. " +
            "Returns the response status and body text. (For multipart file uploads through a form, use browser_upload_file + browser_click instead.)";

        public override ToolParamList Params => new ToolParamList()
            .String("url", "Request URL (absolute, or relative to the current page).", required: true)
            .StringEnum("method", "HTTP method.", new[] { "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD" }, defaultValue: "GET")
            .Object("headers", "Optional headers as a JSON object, e.g. {\"Content-Type\":\"application/json\"}.")
            .String("body", "Optional request body string (e.g. JSON text or 'a=1&b=2' form-encoded).")
            .Int("max_chars", "Maximum response body characters to return. Default: 20000.", defaultValue: 20000);

        public override ToolResult Execute(JObject args)
        {
            string url = args.Value<string>("url");
            if (string.IsNullOrWhiteSpace(url))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'url' parameter");
            string method = (args.Value<string>("method") ?? "GET").ToUpperInvariant();
            int maxChars = args.Value<int?>("max_chars") ?? 20000;

            var headers = args["headers"] as JObject ?? new JObject();
            string body = args.Value<string>("body");

            var opts = new JObject { ["method"] = method, ["headers"] = headers };
            if (body != null && method != "GET" && method != "HEAD")
                opts["body"] = body;

            // Build a fetch expression that resolves to a JSON string.
            string expr =
                "(function(){return fetch(" + JsonConvert.SerializeObject(url) + "," + opts.ToString(Formatting.None) + ")" +
                ".then(function(r){return r.text().then(function(t){return JSON.stringify({status:r.status,statusText:r.statusText,ok:r.ok,url:r.url,body:t});});})" +
                ".catch(function(e){return JSON.stringify({error:String(e)});});})()";

            string cdp = Host.RunOnUi(() => Host.CdpEvaluateAsync(expr, true));
            string value = CdpResult.Extract(cdp);

            // value is the JSON string returned by the page; truncate body if huge.
            try
            {
                var parsed = JObject.Parse(value);
                string respBody = parsed["body"]?.Value<string>();
                if (respBody != null && respBody.Length > maxChars)
                {
                    parsed["body"] = respBody.Substring(0, maxChars) + "... [truncated]";
                    value = parsed.ToString(Formatting.Indented);
                }
            }
            catch { /* leave value as-is */ }

            return ToolResult.Success(value);
        }
    }

    /// <summary>Helpers to extract a value from a CDP Runtime.evaluate result.</summary>
    internal static class CdpResult
    {
        public static string Extract(string cdpJson)
        {
            if (string.IsNullOrEmpty(cdpJson)) return "null";
            try
            {
                var o = JObject.Parse(cdpJson);
                var ex = o["exceptionDetails"];
                if (ex != null)
                {
                    var text = ex["exception"]?["description"]?.Value<string>()
                               ?? ex["text"]?.Value<string>()
                               ?? ex.ToString();
                    return "JavaScript error: " + text;
                }
                var val = o["result"]?["value"];
                if (val != null)
                    return val.Type == JTokenType.String ? val.Value<string>() : val.ToString(Formatting.None);
                // No by-value result (e.g. undefined)
                var desc = o["result"]?["description"]?.Value<string>();
                return desc ?? "undefined";
            }
            catch
            {
                return cdpJson;
            }
        }
    }
}
