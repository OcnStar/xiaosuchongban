using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Xiaosu.CompanionAI.LLM
{
    public sealed class OpenAiStreamChunkPayload
    {
        public string ContentDelta { get; set; }
        public string ReasoningDelta { get; set; }
        public string FinishReason { get; set; }
        public string ErrorMessage { get; set; }
        public int? PromptTokens { get; set; }
        public int? CompletionTokens { get; set; }
    }

    public static class LlmJson
    {
        public static string BuildChatCompletionRequestJson(
            string model,
            IList<LlmChatMessage> messages,
            double? temperature,
            int? maxTokens,
            bool includeStreamUsage)
        {
            var sb = new StringBuilder(512);
            sb.Append("{\"model\":");
            AppendJsonString(sb, model ?? string.Empty);
            sb.Append(",\"stream\":true");
            if (includeStreamUsage)
            {
                sb.Append(",\"stream_options\":{\"include_usage\":true}");
            }
            if (temperature.HasValue)
            {
                sb.Append(",\"temperature\":");
                sb.Append(temperature.Value.ToString("0.###", CultureInfo.InvariantCulture));
            }
            if (maxTokens.HasValue && maxTokens.Value > 0)
            {
                sb.Append(",\"max_tokens\":");
                sb.Append(maxTokens.Value.ToString(CultureInfo.InvariantCulture));
            }
            sb.Append(",\"messages\":[");
            if (messages != null)
            {
                for (int i = 0; i < messages.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    var msg = messages[i];
                    sb.Append("{\"role\":");
                    AppendJsonString(sb, msg != null ? msg.Role : "user");
                    sb.Append(",\"content\":");
                    AppendJsonString(sb, msg != null ? msg.Content : string.Empty);
                    sb.Append('}');
                }
            }
            sb.Append("]}");
            return sb.ToString();
        }

        public static void AppendJsonString(StringBuilder sb, string value)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }
            sb.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u");
                            sb.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
        }

        public static OpenAiStreamChunkPayload ParseStreamChunk(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new FormatException("Empty JSON frame in SSE data.");
            }
            int idx = 0;
            object rootObj = ParseValue(json, ref idx);
            SkipWhitespace(json, ref idx);
            if (idx != json.Length)
            {
                throw new FormatException("Unexpected trailing tokens in JSON frame.");
            }

            var root = rootObj as Dictionary<string, object>;
            if (root == null)
            {
                throw new FormatException("SSE JSON root must be an object.");
            }

            var result = new OpenAiStreamChunkPayload();

            object errorVal;
            if (root.TryGetValue("error", out errorVal) && errorVal != null)
            {
                var errDict = errorVal as Dictionary<string, object>;
                if (errDict != null)
                {
                    result.ErrorMessage = GetString(errDict, "message") ?? GetString(errDict, "code") ?? "Upstream error object";
                }
                else
                {
                    result.ErrorMessage = errorVal.ToString();
                }
            }

            object choicesVal;
            if (root.TryGetValue("choices", out choicesVal))
            {
                var choices = choicesVal as List<object>;
                if (choices != null && choices.Count > 0)
                {
                    var firstChoice = choices[0] as Dictionary<string, object>;
                    if (firstChoice != null)
                    {
                        result.FinishReason = GetString(firstChoice, "finish_reason");
                        object deltaVal;
                        if (firstChoice.TryGetValue("delta", out deltaVal))
                        {
                            var delta = deltaVal as Dictionary<string, object>;
                            if (delta != null)
                            {
                                result.ContentDelta = GetString(delta, "content");
                                result.ReasoningDelta = GetString(delta, "reasoning_content");
                            }
                        }
                    }
                }
            }

            object usageVal;
            if (root.TryGetValue("usage", out usageVal))
            {
                var usage = usageVal as Dictionary<string, object>;
                if (usage != null)
                {
                    result.PromptTokens = GetInt(usage, "prompt_tokens");
                    result.CompletionTokens = GetInt(usage, "completion_tokens");
                }
            }

            return result;
        }

        public static string ExtractErrorMessage(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return string.Empty;
            }
            try
            {
                int idx = 0;
                var root = ParseValue(body, ref idx) as Dictionary<string, object>;
                if (root != null)
                {
                    object errVal;
                    if (root.TryGetValue("error", out errVal))
                    {
                        var errDict = errVal as Dictionary<string, object>;
                        if (errDict != null)
                        {
                            string msg = GetString(errDict, "message");
                            string code = GetString(errDict, "code") ?? GetString(errDict, "type");
                            if (!string.IsNullOrEmpty(msg) && !string.IsNullOrEmpty(code))
                            {
                                return code + ": " + msg;
                            }
                            if (!string.IsNullOrEmpty(msg)) return msg;
                        }
                        else if (errVal != null)
                        {
                            return errVal.ToString();
                        }
                    }
                    string topMsg = GetString(root, "message");
                    if (!string.IsNullOrEmpty(topMsg)) return topMsg;
                }
            }
            catch
            {
                // Fall back to truncated raw snippet below.
            }
            string trimmed = body.Trim();
            return trimmed.Length > 240 ? trimmed.Substring(0, 240) + "..." : trimmed;
        }

        private static string GetString(Dictionary<string, object> dict, string key)
        {
            object val;
            if (dict.TryGetValue(key, out val) && val != null)
            {
                return val as string ?? val.ToString();
            }
            return null;
        }

        private static int? GetInt(Dictionary<string, object> dict, string key)
        {
            object val;
            if (dict.TryGetValue(key, out val) && val != null)
            {
                if (val is double) return (int)(double)val;
                int parsed;
                if (int.TryParse(val.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                {
                    return parsed;
                }
            }
            return null;
        }

        private static object ParseValue(string s, ref int i)
        {
            SkipWhitespace(s, ref i);
            if (i >= s.Length) throw new FormatException("Unexpected end of JSON input.");
            char c = s[i];
            if (c == '{') return ParseObject(s, ref i);
            if (c == '[') return ParseArray(s, ref i);
            if (c == '"') return ParseString(s, ref i);
            if (c == 't') { ExpectLiteral(s, ref i, "true"); return true; }
            if (c == 'f') { ExpectLiteral(s, ref i, "false"); return false; }
            if (c == 'n') { ExpectLiteral(s, ref i, "null"); return null; }
            if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber(s, ref i);
            throw new FormatException("Unexpected character '" + c + "' at position " + i);
        }

        private static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            var dict = new Dictionary<string, object>(StringComparer.Ordinal);
            i++; // '{'
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == '}')
            {
                i++;
                return dict;
            }
            while (true)
            {
                SkipWhitespace(s, ref i);
                if (i >= s.Length || s[i] != '"')
                {
                    throw new FormatException("Expected string property name in JSON object at " + i);
                }
                string key = ParseString(s, ref i);
                SkipWhitespace(s, ref i);
                if (i >= s.Length || s[i] != ':')
                {
                    throw new FormatException("Expected ':' after property name at " + i);
                }
                i++;
                object val = ParseValue(s, ref i);
                dict[key] = val;
                SkipWhitespace(s, ref i);
                if (i >= s.Length) throw new FormatException("Unterminated JSON object.");
                if (s[i] == '}')
                {
                    i++;
                    return dict;
                }
                if (s[i] != ',')
                {
                    throw new FormatException("Expected ',' or '}' in JSON object at " + i);
                }
                i++;
            }
        }

        private static List<object> ParseArray(string s, ref int i)
        {
            var list = new List<object>();
            i++; // '['
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == ']')
            {
                i++;
                return list;
            }
            while (true)
            {
                object val = ParseValue(s, ref i);
                list.Add(val);
                SkipWhitespace(s, ref i);
                if (i >= s.Length) throw new FormatException("Unterminated JSON array.");
                if (s[i] == ']')
                {
                    i++;
                    return list;
                }
                if (s[i] != ',')
                {
                    throw new FormatException("Expected ',' or ']' in JSON array at " + i);
                }
                i++;
            }
        }

        private static string ParseString(string s, ref int i)
        {
            i++; // '"'
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"')
                {
                    return sb.ToString();
                }
                if (c == '\\')
                {
                    if (i >= s.Length) throw new FormatException("Unterminated escape in JSON string.");
                    char esc = s[i++];
                    switch (esc)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 > s.Length) throw new FormatException("Invalid unicode escape in JSON string.");
                            int code;
                            if (!int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                            {
                                throw new FormatException("Invalid hex in unicode escape.");
                            }
                            i += 4;
                            sb.Append((char)code);
                            break;
                        default:
                            throw new FormatException("Unsupported escape '\\" + esc + "' in JSON string.");
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            throw new FormatException("Unterminated JSON string.");
        }

        private static double ParseNumber(string s, ref int i)
        {
            int start = i;
            if (s[i] == '-') i++;
            while (i < s.Length && ((s[i] >= '0' && s[i] <= '9') || s[i] == '.' || s[i] == 'e' || s[i] == 'E' || s[i] == '+' || s[i] == '-'))
            {
                i++;
            }
            double val;
            if (!double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out val))
            {
                throw new FormatException("Invalid JSON number at " + start);
            }
            return val;
        }

        private static void ExpectLiteral(string s, ref int i, string literal)
        {
            if (i + literal.Length > s.Length || string.CompareOrdinal(s, i, literal, 0, literal.Length) != 0)
            {
                throw new FormatException("Expected '" + literal + "' at " + i);
            }
            i += literal.Length;
        }

        private static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') i++;
                else break;
            }
        }
    }
}
