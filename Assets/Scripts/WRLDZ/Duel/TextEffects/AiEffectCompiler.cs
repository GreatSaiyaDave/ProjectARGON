using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using WRLDZ.Data;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// SpaceXAI / xAI-backed compiler: official card text → schema-validated
    /// <see cref="CompiledCardProgram"/>. Used offline (bulk) and optional first-play.
    /// Live duel resolution never calls the model — only the cache/runtime does.
    /// </summary>
    public static class AiEffectCompiler
    {
        static readonly HttpClient Http = new()
        {
            Timeout = TimeSpan.FromSeconds(120)
        };

        public static bool IsAvailable => AiEffectCompileSettings.HasApiKey;

        /// <summary>Synchronous compile (Editor bulk / rare first-play). May hitch.</summary>
        public static bool TryCompile(CardDef def, out CompiledCardProgram prog, out string error)
        {
            prog = null;
            error = null;
            if (def == null)
            {
                error = "No card def.";
                return false;
            }

            var key = AiEffectCompileSettings.ResolveApiKey();
            if (string.IsNullOrEmpty(key))
            {
                error = "No XAI_API_KEY (env / PlayerPrefs / ai_config.json).";
                return false;
            }

            try
            {
                var task = CompileAsync(def, key);
                if (!task.Wait(TimeSpan.FromSeconds(AiEffectCompileSettings.TimeoutSeconds + 5)))
                {
                    error = "AI compile timed out.";
                    return false;
                }

                var (ok, p, err) = task.Result;
                prog = p;
                error = err;
                return ok;
            }
            catch (Exception ex)
            {
                error = ex.GetBaseException().Message;
                return false;
            }
        }

        public static async Task<(bool ok, CompiledCardProgram prog, string error)> CompileAsync(
            CardDef def, string apiKey = null)
        {
            apiKey ??= AiEffectCompileSettings.ResolveApiKey();
            if (string.IsNullOrEmpty(apiKey))
                return (false, null, "No API key.");

            var text = OfficialCardAuthority.OfficialText(def);
            if (string.IsNullOrWhiteSpace(text))
                return (false, null, "No official text.");

            var user = new StringBuilder();
            user.AppendLine($"cardId: {def.id}");
            user.AppendLine($"cardName: {def.name}");
            user.AppendLine($"type: {def.type}");
            user.AppendLine($"race: {def.race}");
            user.AppendLine($"frameType: {def.frameType}");
            user.AppendLine($"atk: {def.atk} def: {def.def} level: {def.level}");
            user.AppendLine("officialText:");
            user.AppendLine(text);

            var body = BuildChatBody(user.ToString());
            var url = AiEffectCompileSettings.BaseUrl.TrimEnd('/') + "/chat/completions";

            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiKey);
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");

            HttpResponseMessage resp;
            try
            {
                resp = await Http.SendAsync(req).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return (false, null, "HTTP error: " + ex.Message);
            }

            var respText = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return (false, null, $"API {(int)resp.StatusCode}: {Truncate(respText, 200)}");

            var content = ExtractAssistantContent(respText);
            if (string.IsNullOrEmpty(content))
                return (false, null, "Empty model content.");

            if (!EffectProgramValidator.TryParseAndValidate(content, def, out var prog, out var err))
                return (false, null, err ?? "Schema validation failed.");

            prog.CompileSource = "ai";
            prog.TextHash = OfficialCardAuthority.TextHash(def);
            prog.SourceText = text;
            prog.CardId = def.id;
            prog.CardName = def.name;
            prog.CompilerVersion = CardTextEffectCompiler.Version;
            prog.CompiledUtc = DateTime.UtcNow.ToString("o");
            return (true, prog, null);
        }

        static string BuildChatBody(string userContent)
        {
            // Manual JSON to avoid Unity JsonUtility limits on nested arrays of objects for request
            var system = EscapeJson(EffectProgramValidator.SchemaPrompt());
            var user = EscapeJson(userContent);
            var model = EscapeJson(AiEffectCompileSettings.Model);
            return
                "{\n" +
                $"  \"model\": \"{model}\",\n" +
                "  \"temperature\": 0,\n" +
                "  \"messages\": [\n" +
                $"    {{\"role\": \"system\", \"content\": \"{system}\"}},\n" +
                $"    {{\"role\": \"user\", \"content\": \"{user}\"}}\n" +
                "  ]\n" +
                "}";
        }

        static string ExtractAssistantContent(string responseJson)
        {
            // Minimal extract: "content":"..."
            // Prefer simple parse for chat.completion shape
            try
            {
                // Unity JsonUtility cannot parse dynamic shapes; use string scan for content field of message
                var marker = "\"content\":";
                // Find last content in choices[0].message (avoid refusal null)
                var idx = responseJson.IndexOf("\"message\"", StringComparison.Ordinal);
                if (idx < 0) idx = 0;
                var cidx = responseJson.IndexOf(marker, idx, StringComparison.Ordinal);
                if (cidx < 0) return null;
                cidx += marker.Length;
                while (cidx < responseJson.Length && char.IsWhiteSpace(responseJson[cidx])) cidx++;
                if (cidx >= responseJson.Length) return null;
                if (responseJson[cidx] == '"')
                    return UnescapeJsonString(responseJson, cidx + 1);
                if (responseJson[cidx] == 'n') // null
                    return null;
            }
            catch
            {
                // ignore
            }

            return null;
        }

        static string UnescapeJsonString(string s, int start)
        {
            var sb = new StringBuilder();
            for (var i = start; i < s.Length; i++)
            {
                var ch = s[i];
                if (ch == '\\' && i + 1 < s.Length)
                {
                    var n = s[++i];
                    sb.Append(n switch
                    {
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        '"' => '"',
                        '\\' => '\\',
                        '/' => '/',
                        'u' when i + 4 < s.Length => (char)Convert.ToInt32(s.Substring(i + 1, 4), 16),
                        _ => n
                    });
                    if (n == 'u') i += 4;
                    continue;
                }

                if (ch == '"') break;
                sb.Append(ch);
            }

            return sb.ToString();
        }

        static string EscapeJson(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n")
                .Replace("\r", "\\r").Replace("\t", "\\t");
        }

        static string Truncate(string s, int n) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n) + "…");
    }
}
