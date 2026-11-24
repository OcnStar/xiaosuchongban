using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Xiaosu.CompanionAI.LLM.Probe
{
    /// <summary>
    /// Built-in local OpenAI Chat Completions compatible SSE simulator & fault injector.
    /// Allows deterministic offline verification of normal streaming, multi-byte UTF-8 packet splits,
    /// cross-chunk emotion/action tags, slow streams for cancellation, HTTP 401/429/500 errors,
    /// mid-stream abrupt disconnects, malformed JSON frames, and TTFT stalls.
    /// </summary>
    public static class MockOpenAiSseHandler
    {
        public static async Task HandleModelsAsync(LoopbackHttpContext ctx)
        {
            const string modelsJson = @"{
  ""object"": ""list"",
  ""data"": [
    { ""id"": ""mock-xiaosu-stream"", ""object"": ""model"", ""owned_by"": ""xiaosu-local-mock"" },
    { ""id"": ""mock-utf8-split"", ""object"": ""model"", ""owned_by"": ""xiaosu-local-mock"" },
    { ""id"": ""mock-tag-split"", ""object"": ""model"", ""owned_by"": ""xiaosu-local-mock"" },
    { ""id"": ""mock-slow-stream"", ""object"": ""model"", ""owned_by"": ""xiaosu-local-mock"" },
    { ""id"": ""mock-http-401"", ""object"": ""model"", ""owned_by"": ""xiaosu-local-mock"" },
    { ""id"": ""mock-http-429"", ""object"": ""model"", ""owned_by"": ""xiaosu-local-mock"" },
    { ""id"": ""mock-http-500"", ""object"": ""model"", ""owned_by"": ""xiaosu-local-mock"" },
    { ""id"": ""mock-drop-midstream"", ""object"": ""model"", ""owned_by"": ""xiaosu-local-mock"" },
    { ""id"": ""mock-malformed-json"", ""object"": ""model"", ""owned_by"": ""xiaosu-local-mock"" },
    { ""id"": ""mock-stall-ttft"", ""object"": ""model"", ""owned_by"": ""xiaosu-local-mock"" }
  ]
}";
            await ctx.WriteJsonAsync(200, modelsJson).ConfigureAwait(false);
        }

        public static async Task HandleChatCompletionsAsync(LoopbackHttpContext ctx, CancellationToken ct)
        {
            string model = "mock-xiaosu-stream";
            string userPrompt = string.Empty;

            try
            {
                using var doc = JsonDocument.Parse(ctx.BodyText);
                if (doc.RootElement.TryGetProperty("model", out var mProp) && mProp.ValueKind == JsonValueKind.String)
                {
                    model = mProp.GetString() ?? model;
                }
                if (doc.RootElement.TryGetProperty("messages", out var msgs) && msgs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in msgs.EnumerateArray())
                    {
                        if (item.TryGetProperty("role", out var r) && r.GetString() == "user" &&
                            item.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                        {
                            userPrompt = c.GetString() ?? string.Empty;
                        }
                    }
                }
            }
            catch
            {
                await ctx.WriteJsonAsync(400, "{\"error\":{\"message\":\"Invalid JSON request body\",\"type\":\"invalid_request_error\"}}").ConfigureAwait(false);
                return;
            }

            string scenario = model.Trim().ToLowerInvariant();
            if (ctx.Headers.TryGetValue("X-Mock-Scenario", out string headerScenario) && !string.IsNullOrWhiteSpace(headerScenario))
            {
                scenario = headerScenario.Trim().ToLowerInvariant();
            }

            switch (scenario)
            {
                case "mock-http-401":
                    await ctx.WriteJsonAsync(401, "{\"error\":{\"message\":\"Invalid authentication credentials for mock test.\",\"type\":\"authentication_error\",\"code\":\"invalid_api_key\"}}").ConfigureAwait(false);
                    return;

                case "mock-http-429":
                    await ctx.WriteJsonAsync(429, "{\"error\":{\"message\":\"Rate limit exceeded on mock upstream.\",\"type\":\"rate_limit_error\",\"code\":\"rate_limit_exceeded\"}}").ConfigureAwait(false);
                    return;

                case "mock-http-500":
                    await ctx.WriteJsonAsync(500, "{\"error\":{\"message\":\"Simulated upstream inference engine crash.\",\"type\":\"server_error\"}}").ConfigureAwait(false);
                    return;

                case "mock-stall-ttft":
                    await Task.Delay(3000, ct).ConfigureAwait(false);
                    await ctx.WriteJsonAsync(504, "{\"error\":{\"message\":\"Gateway timeout\"}}").ConfigureAwait(false);
                    return;

                case "mock-utf8-split":
                    await StreamByteSlicedUtf8Async(ctx, ct).ConfigureAwait(false);
                    return;

                case "mock-tag-split":
                    await StreamSplitControlTagsAsync(ctx, ct).ConfigureAwait(false);
                    return;

                case "mock-slow-stream":
                    await StreamSlowCancellableAsync(ctx, ct).ConfigureAwait(false);
                    return;

                case "mock-drop-midstream":
                    await StreamAbruptDropAsync(ctx, ct).ConfigureAwait(false);
                    return;

                case "mock-malformed-json":
                    await StreamMalformedJsonAsync(ctx, ct).ConfigureAwait(false);
                    return;

                default:
                    await StreamNormalCompanionReplyAsync(ctx, userPrompt, ct).ConfigureAwait(false);
                    return;
            }
        }

        private static async Task StreamNormalCompanionReplyAsync(LoopbackHttpContext ctx, string userPrompt, CancellationToken ct)
        {
            await ctx.BeginSseAsync(200).ConfigureAwait(false);
            string[] tokens =
            {
                "[emotion:Happy]",
                "[action:Wave]",
                "收到啦！",
                "我是晓苏本地 OpenAI 兼容流式模拟器。",
                "[emotion:Gentle]",
                string.IsNullOrWhiteSpace(userPrompt)
                    ? "当前 SSE 流式链路、分句切分与受控情绪事件均工作正常。"
                    : "刚才听到你说「" + TruncatePrompt(userPrompt, 24) + "」，要先休息五分钟喝口水吗？"
            };

            byte[] heartbeat = Encoding.UTF8.GetBytes(": keep-alive\n\n");
            await ctx.WriteRawBytesAsync(heartbeat, 0, heartbeat.Length).ConfigureAwait(false);

            for (int i = 0; i < tokens.Length; i++)
            {
                await Task.Delay(25, ct).ConfigureAwait(false);
                await WriteDeltaChunkAsync(ctx, tokens[i], null, ct).ConfigureAwait(false);
            }

            await WriteFinishAndDoneAsync(ctx, promptTokens: 28, completionTokens: 42).ConfigureAwait(false);
        }

        private static async Task StreamByteSlicedUtf8Async(LoopbackHttpContext ctx, CancellationToken ct)
        {
            await ctx.BeginSseAsync(200).ConfigureAwait(false);

            // Build raw SSE payload containing 3-byte UTF-8 CJK characters and 4-byte emoji,
            // plus SSE heartbeat comments and no-space "data:{...}" formatting, then slice
            // at 2-byte steps so multi-byte UTF-8 sequences are guaranteed to split across TCP writes.
            string fullStream =
                ": keep-alive\r\n\r\n" +
                "data:{\"id\":\"utf8-1\",\"choices\":[{\"index\":0,\"delta\":{\"content\":\"晓苏宠伴✨流式UTF-8\"}}]}\r\n\r\n" +
                "data: {\"id\":\"utf8-2\",\"choices\":[{\"index\":0,\"delta\":{\"content\":\"跨包重组零乱码验证通过！\"}}]}\n\n" +
                "data: {\"id\":\"utf8-3\",\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}]}\n\n" +
                "data: [DONE]\n\n";

            byte[] rawBytes = Encoding.UTF8.GetBytes(fullStream);
            int offset = 0;
            int step = 5; // 5 bytes splits 3-byte UTF-8 sequences and keywords mid-sequence
            while (offset < rawBytes.Length)
            {
                ct.ThrowIfCancellationRequested();
                int len = Math.Min(step, rawBytes.Length - offset);
                await ctx.WriteRawBytesAsync(rawBytes, offset, len).ConfigureAwait(false);
                offset += len;
                await Task.Delay(2, ct).ConfigureAwait(false);
            }
        }

        private static async Task StreamSplitControlTagsAsync(LoopbackHttpContext ctx, CancellationToken ct)
        {
            await ctx.BeginSseAsync(200).ConfigureAwait(false);
            string[] splitTokens =
            {
                "[emo",
                "tion:Happy][act",
                "ion:Nod] 我明白",
                "你的意思啦！[emotion:",
                "Gentle] 要不要休息",
                "五分钟？[1] 附注保留"
            };

            foreach (string t in splitTokens)
            {
                await Task.Delay(8, ct).ConfigureAwait(false);
                await WriteDeltaChunkAsync(ctx, t, null, ct).ConfigureAwait(false);
            }

            await WriteFinishAndDoneAsync(ctx, 18, 26).ConfigureAwait(false);
        }

        private static async Task StreamSlowCancellableAsync(LoopbackHttpContext ctx, CancellationToken ct)
        {
            await ctx.BeginSseAsync(200).ConfigureAwait(false);
            for (int i = 1; i <= 20; i++)
            {
                await WriteDeltaChunkAsync(ctx, $"[片段{i}] 正在持续生成慢速流式内容，可随时点击取消按钮验证中断…", null, ct).ConfigureAwait(false);
                await Task.Delay(180, ct).ConfigureAwait(false);
            }
            await WriteFinishAndDoneAsync(ctx, 16, 80).ConfigureAwait(false);
        }

        private static async Task StreamAbruptDropAsync(LoopbackHttpContext ctx, CancellationToken ct)
        {
            await ctx.BeginSseAsync(200).ConfigureAwait(false);
            await WriteDeltaChunkAsync(ctx, "[emotion:Thinking] 这一句才说到一半连接突然断", null, ct).ConfigureAwait(false);
            await Task.Delay(15, ct).ConfigureAwait(false);
            ctx.AbortConnectionImmediately();
        }

        private static async Task StreamMalformedJsonAsync(LoopbackHttpContext ctx, CancellationToken ct)
        {
            await ctx.BeginSseAsync(200).ConfigureAwait(false);
            await WriteDeltaChunkAsync(ctx, "前序正常片段，", null, ct).ConfigureAwait(false);
            byte[] corrupt = Encoding.UTF8.GetBytes("data: {\"id\":\"bad\",\"choices\":[{\"delta\":{\"content\":BROKEN_JSON\n\n");
            await ctx.WriteRawBytesAsync(corrupt, 0, corrupt.Length).ConfigureAwait(false);
        }

        private static async Task WriteDeltaChunkAsync(LoopbackHttpContext ctx, string content, string reasoning, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var sb = new StringBuilder(160);
            sb.Append("data: {\"id\":\"chatcmpl-mock\",\"object\":\"chat.completion.chunk\",\"choices\":[{\"index\":0,\"delta\":{");
            bool wroteField = false;
            if (content != null)
            {
                sb.Append("\"content\":");
                LlmJson.AppendJsonString(sb, content);
                wroteField = true;
            }
            if (reasoning != null)
            {
                if (wroteField) sb.Append(',');
                sb.Append("\"reasoning_content\":");
                LlmJson.AppendJsonString(sb, reasoning);
            }
            sb.Append("},\"finish_reason\":null}]}\n\n");
            byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
            await ctx.WriteRawBytesAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
        }

        private static async Task WriteFinishAndDoneAsync(LoopbackHttpContext ctx, int promptTokens, int completionTokens)
        {
            string finishFrame =
                "data: {\"id\":\"chatcmpl-mock\",\"object\":\"chat.completion.chunk\",\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}]," +
                "\"usage\":{\"prompt_tokens\":" + promptTokens + ",\"completion_tokens\":" + completionTokens + "}}\n\n" +
                "data: [DONE]\n\n";
            byte[] bytes = Encoding.UTF8.GetBytes(finishFrame);
            await ctx.WriteRawBytesAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
        }

        private static string TruncatePrompt(string s, int maxChars)
        {
            string clean = s.Replace("\r", " ").Replace("\n", " ").Trim();
            return clean.Length <= maxChars ? clean : clean.Substring(0, maxChars) + "…";
        }
    }
}
