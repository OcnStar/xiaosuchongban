using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Xiaosu.CompanionAI.LLM.Probe
{
    public static class Program
    {
        private static readonly ConcurrentDictionary<string, CancellationTokenSource> ActiveStreams =
            new ConcurrentDictionary<string, CancellationTokenSource>(StringComparer.Ordinal);

        public static async Task<int> Main(string[] args)
        {
            bool unityNetStandardVerified = string.Equals(
                Environment.GetEnvironmentVariable("XIAOSU_NETSTANDARD21_VERIFIED"),
                "1",
                StringComparison.Ordinal);

            string mode = "--serve";
            int port = 0;
            string envPort = Environment.GetEnvironmentVariable("MULTICA_PORT");
            if (!string.IsNullOrWhiteSpace(envPort) && int.TryParse(envPort, out int parsedEnvPort))
            {
                port = parsedEnvPort;
            }

            string markdownOut = null;
            string jsonOut = null;
            string probeBaseUrl = null;
            string probeModel = "qwen2.5:7b";
            string probeKind = "local";
            string probePrompt = "你好晓苏，请用一句话打个招呼。";
            string probeEnvVar = null;
            string probeKeychainService = null;
            string probeKeychainAccount = null;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                switch (a)
                {
                    case "--serve":
                        mode = "--serve";
                        break;
                    case "--run-suite":
                        mode = "--run-suite";
                        break;
                    case "--probe":
                        mode = "--probe";
                        break;
                    case "--port":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out int p)) port = p;
                        break;
                    case "--markdown-out":
                        if (i + 1 < args.Length) markdownOut = args[++i];
                        break;
                    case "--json-out":
                        if (i + 1 < args.Length) jsonOut = args[++i];
                        break;
                    case "--base-url":
                        if (i + 1 < args.Length) probeBaseUrl = args[++i];
                        break;
                    case "--model":
                        if (i + 1 < args.Length) probeModel = args[++i];
                        break;
                    case "--kind":
                        if (i + 1 < args.Length) probeKind = args[++i];
                        break;
                    case "--prompt":
                        if (i + 1 < args.Length) probePrompt = args[++i];
                        break;
                    case "--api-key-env":
                        if (i + 1 < args.Length) probeEnvVar = args[++i];
                        break;
                    case "--keychain-service":
                        if (i + 1 < args.Length) probeKeychainService = args[++i];
                        break;
                    case "--keychain-account":
                        if (i + 1 < args.Length) probeKeychainAccount = args[++i];
                        break;
                }
            }

            if (mode == "--run-suite")
            {
                return await RunHeadlessSuiteAsync(markdownOut, jsonOut).ConfigureAwait(false);
            }

            if (mode == "--probe")
            {
                return await RunCliProbeAsync(
                    probeBaseUrl,
                    probeModel,
                    probeKind,
                    probePrompt,
                    probeEnvVar,
                    probeKeychainService,
                    probeKeychainAccount).ConfigureAwait(false);
            }

            if (port <= 0)
            {
                port = 18080;
            }

            using var shutdownCts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                shutdownCts.Cancel();
            };
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                try { shutdownCts.Cancel(); } catch { }
            };

            LoopbackHttpServer server = null;
            server = new LoopbackHttpServer(port, (ctx, ct) => RouteHttpRequestAsync(ctx, server.Port, unityNetStandardVerified, ct));
            server.Start();

            Console.WriteLine($"[FUMI-43 Workbench] Listening on http://localhost:{server.Port} (loopback only: 127.0.0.1 / ::1)");
            Console.WriteLine($"[FUMI-43 Workbench] Health check: http://localhost:{server.Port}/healthz");
            Console.WriteLine($"[FUMI-43 Workbench] Built-in OpenAI SSE mock: http://127.0.0.1:{server.Port}/mock-openai/v1");
            Console.Out.Flush();

            try
            {
                await Task.Delay(Timeout.Infinite, shutdownCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                server.Dispose();
            }

            return 0;
        }

        private static async Task<int> RunHeadlessSuiteAsync(string markdownOut, string jsonOut)
        {
            using var server = new LoopbackHttpServer(0, (ctx, ct) => RouteHttpRequestAsync(ctx, 0, true, ct));
            server.Start();
            string mockBaseUrl = $"http://127.0.0.1:{server.Port}/mock-openai/v1";

            var report = await VerificationSuiteRunner.RunAllAsync(mockBaseUrl, CancellationToken.None).ConfigureAwait(false);
            Console.WriteLine(report.MarkdownReport);

            if (!string.IsNullOrWhiteSpace(markdownOut))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(markdownOut)) ?? ".");
                await File.WriteAllTextAsync(markdownOut, report.MarkdownReport, Encoding.UTF8).ConfigureAwait(false);
            }

            if (!string.IsNullOrWhiteSpace(jsonOut))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(jsonOut)) ?? ".");
                string json = JsonSerializer.Serialize(report, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                });
                await File.WriteAllTextAsync(jsonOut, json, Encoding.UTF8).ConfigureAwait(false);
            }

            return report.AllPassed ? 0 : 1;
        }

        private static async Task<int> RunCliProbeAsync(
            string baseUrl,
            string model,
            string kind,
            string prompt,
            string envVar,
            string keychainService,
            string keychainAccount)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                Console.Error.WriteLine("Missing --base-url for --probe.");
                return 2;
            }

            var providerKind = string.Equals(kind, "cloud", StringComparison.OrdinalIgnoreCase)
                ? LlmProviderKind.CloudOpenAiCompatible
                : LlmProviderKind.LocalOpenAiCompatible;

            string apiKey = LocalSecretStore.ResolveApiKey(null, envVar, keychainService, keychainAccount, out string keySource);

            using var provider = new OpenAiChatCompletionsProvider(new LlmEndpointConfig
            {
                Name = "cli-probe",
                ProviderKind = providerKind,
                BaseUrl = baseUrl,
                Model = model,
                IncludeStreamUsage = true
            }, () => apiKey);

            var req = new LlmChatRequest();
            req.Messages.Add(new LlmChatMessage("system", "你是桌面伴侣晓苏。可在句首使用 [emotion:Happy][action:Wave] 等受控标签。"));
            req.Messages.Add(new LlmChatMessage("user", prompt));

            Console.WriteLine($"[CLI Probe] Endpoint={baseUrl}, Model={model}, Kind={providerKind}, CredentialSource={keySource}");
            var res = await provider.StreamChatAsync(req, evt =>
            {
                if (evt.EventType == LlmEventType.TextDelta)
                {
                    Console.Write(evt.Text);
                }
                else if (evt.EventType == LlmEventType.EmotionTag || evt.EventType == LlmEventType.ActionTag)
                {
                    Console.Write($" <{evt.EventType}:{evt.TagValue}> ");
                }
            }, CancellationToken.None).ConfigureAwait(false);

            Console.WriteLine();
            Console.WriteLine(BuildSingleProbeMarkdown(provider.Config, keySource, res));
            return res.IsSuccess ? 0 : 1;
        }

        private static async Task RouteHttpRequestAsync(
            LoopbackHttpContext ctx,
            int activePort,
            bool unityNetStandardVerified,
            CancellationToken serverCt)
        {
            if (ctx.Method == "GET" && ctx.Path == "/healthz")
            {
                var snap = ResourceSampler.Capture();
                string json = JsonSerializer.Serialize(new
                {
                    status = "ok",
                    service = "fumi-43-llm-workbench",
                    port = activePort,
                    os = snap.OsDescription,
                    arch = snap.ProcessArchitecture,
                    framework = snap.FrameworkDescription,
                    unityNetStandardVerified,
                    workingSetBytes = snap.WorkingSetBytes
                });
                await ctx.WriteJsonAsync(200, json).ConfigureAwait(false);
                return;
            }

            if (ctx.Method == "GET" && ctx.Path == "/")
            {
                await ctx.WriteHtmlAsync(200, WorkbenchHtml.Render(activePort)).ConfigureAwait(false);
                return;
            }

            if (ctx.Method == "GET" && ctx.Path == "/api/status")
            {
                var snap = ResourceSampler.Capture();
                string json = JsonSerializer.Serialize(new
                {
                    port = activePort,
                    os = snap.OsDescription,
                    arch = snap.ProcessArchitecture,
                    framework = snap.FrameworkDescription,
                    processorCount = snap.ProcessorCount,
                    unityNetStandardVerified,
                    workingSetMb = (snap.WorkingSetBytes / 1048576.0).ToString("F2", CultureInfo.InvariantCulture),
                    managedHeapKb = (snap.ManagedHeapBytes / 1024.0).ToString("F1", CultureInfo.InvariantCulture),
                    gcGen0 = snap.GcGen0Count,
                    gcGen1 = snap.GcGen1Count,
                    gcGen2 = snap.GcGen2Count
                });
                await ctx.WriteJsonAsync(200, json).ConfigureAwait(false);
                return;
            }

            if (ctx.Method == "GET" && ctx.Path == "/mock-openai/v1/models")
            {
                await MockOpenAiSseHandler.HandleModelsAsync(ctx).ConfigureAwait(false);
                return;
            }

            if (ctx.Method == "POST" && ctx.Path == "/mock-openai/v1/chat/completions")
            {
                await MockOpenAiSseHandler.HandleChatCompletionsAsync(ctx, serverCt).ConfigureAwait(false);
                return;
            }

            if (ctx.Method == "POST" && ctx.Path == "/api/probe/suite")
            {
                string mockBaseUrl = $"http://127.0.0.1:{activePort}/mock-openai/v1";
                var report = await VerificationSuiteRunner.RunAllAsync(mockBaseUrl, serverCt).ConfigureAwait(false);
                string json = JsonSerializer.Serialize(new
                {
                    report.GeneratedAtUtc,
                    report.TotalCases,
                    report.PassedCases,
                    report.AllPassed,
                    workingSetMb = (report.MachineAfter.WorkingSetBytes / 1048576.0).ToString("F2", CultureInfo.InvariantCulture),
                    report.Cases,
                    report.MarkdownReport
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                await ctx.WriteJsonAsync(200, json).ConfigureAwait(false);
                return;
            }

            if (ctx.Method == "POST" && ctx.Path == "/api/probe/cancel")
            {
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(ctx.BodyText) ? "{}" : ctx.BodyText);
                string reqId = doc.RootElement.TryGetProperty("requestId", out var rProp) ? rProp.GetString() : null;
                bool cancelled = false;
                if (!string.IsNullOrWhiteSpace(reqId) && ActiveStreams.TryGetValue(reqId, out var cts))
                {
                    cancelled = true;
                    try { cts.Cancel(); } catch { }
                }
                await ctx.WriteJsonAsync(200, JsonSerializer.Serialize(new { cancelled })).ConfigureAwait(false);
                return;
            }

            if (ctx.Method == "POST" && ctx.Path == "/api/probe/stream")
            {
                await HandleLiveProbeStreamAsync(ctx, serverCt).ConfigureAwait(false);
                return;
            }

            await ctx.WriteJsonAsync(404, "{\"error\":\"Not found\"}").ConfigureAwait(false);
        }

        private static async Task HandleLiveProbeStreamAsync(LoopbackHttpContext ctx, CancellationToken serverCt)
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(ctx.BodyText) ? "{}" : ctx.BodyText);
            var root = doc.RootElement;

            string requestId = GetJsonString(root, "requestId") ?? Guid.NewGuid().ToString("N");
            string kindStr = GetJsonString(root, "providerKind") ?? "local";
            string baseUrl = GetJsonString(root, "baseUrl") ?? string.Empty;
            string model = GetJsonString(root, "model") ?? "mock-xiaosu-stream";
            int ttftTimeoutMs = GetJsonInt(root, "firstTokenTimeoutMs", 15000);
            int readTimeoutMs = GetJsonInt(root, "streamReadTimeoutMs", 30000);
            string systemPrompt = GetJsonString(root, "systemPrompt") ?? string.Empty;
            string userPrompt = GetJsonString(root, "userPrompt") ?? string.Empty;
            string ephemeralKey = GetJsonString(root, "ephemeralApiKey");
            string envVarName = GetJsonString(root, "envVarName");
            string keychainService = GetJsonString(root, "keychainService");
            string keychainAccount = GetJsonString(root, "keychainAccount");

            string apiKey = LocalSecretStore.ResolveApiKey(
                ephemeralKey,
                envVarName,
                keychainService,
                keychainAccount,
                out string secretSource);

            var config = new LlmEndpointConfig
            {
                Name = kindStr == "cloud" ? "cloud-openai-compatible" : "local-openai-compatible",
                ProviderKind = kindStr == "cloud"
                    ? LlmProviderKind.CloudOpenAiCompatible
                    : LlmProviderKind.LocalOpenAiCompatible,
                BaseUrl = baseUrl,
                Model = model,
                FirstTokenTimeoutMs = ttftTimeoutMs,
                StreamReadTimeoutMs = readTimeoutMs,
                IncludeStreamUsage = false
            };

            var chatReq = new LlmChatRequest();
            if (!string.IsNullOrWhiteSpace(systemPrompt))
            {
                chatReq.Messages.Add(new LlmChatMessage("system", systemPrompt));
            }
            chatReq.Messages.Add(new LlmChatMessage("user", userPrompt));

            await ctx.BeginSseAsync(200).ConfigureAwait(false);

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(serverCt);
            ActiveStreams[requestId] = linkedCts;

            try
            {
                using var provider = new OpenAiChatCompletionsProvider(config, () => apiKey);
                var result = await provider.StreamChatAsync(chatReq, evt =>
                {
                    try
                    {
                        switch (evt.EventType)
                        {
                            case LlmEventType.HeadersReceived:
                                ctx.WriteSseEventAsync("headers", JsonSerializer.Serialize(new
                                {
                                    elapsedMs = evt.ElapsedMs,
                                    statusCode = evt.HttpStatusCode
                                })).GetAwaiter().GetResult();
                                break;

                            case LlmEventType.RawSseFrame:
                                ctx.WriteSseEventAsync("raw", JsonSerializer.Serialize(new
                                {
                                    elapsedMs = evt.ElapsedMs,
                                    index = evt.ChunkIndex,
                                    frame = evt.RawFrame
                                })).GetAwaiter().GetResult();
                                break;

                            case LlmEventType.TextDelta:
                                ctx.WriteSseEventAsync("delta", JsonSerializer.Serialize(new
                                {
                                    elapsedMs = evt.ElapsedMs,
                                    index = evt.ChunkIndex,
                                    text = evt.Text
                                })).GetAwaiter().GetResult();
                                break;

                            case LlmEventType.EmotionTag:
                                ctx.WriteSseEventAsync("tag", JsonSerializer.Serialize(new
                                {
                                    kind = "emotion",
                                    value = evt.TagValue,
                                    elapsedMs = evt.ElapsedMs
                                })).GetAwaiter().GetResult();
                                break;

                            case LlmEventType.ActionTag:
                                ctx.WriteSseEventAsync("tag", JsonSerializer.Serialize(new
                                {
                                    kind = "action",
                                    value = evt.TagValue,
                                    elapsedMs = evt.ElapsedMs
                                })).GetAwaiter().GetResult();
                                break;

                            case LlmEventType.SentenceReady:
                                ctx.WriteSseEventAsync("sentence", JsonSerializer.Serialize(new
                                {
                                    elapsedMs = evt.ElapsedMs,
                                    text = evt.Text
                                })).GetAwaiter().GetResult();
                                break;
                        }
                    }
                    catch
                    {
                        // Client disconnected from workbench SSE stream; cancel upstream request cleanly.
                        try { linkedCts.Cancel(); } catch { }
                    }
                }, linkedCts.Token).ConfigureAwait(false);

                string md = BuildSingleProbeMarkdown(config, secretSource, result);
                string summaryJson = JsonSerializer.Serialize(new
                {
                    result.IsSuccess,
                    result.IsCancelled,
                    errorKind = result.ErrorKind.ToString(),
                    result.HttpStatusCode,
                    result.ErrorMessage,
                    result.FinishReason,
                    result.Metrics,
                    markdownLog = md
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

                await ctx.WriteSseEventAsync("summary", summaryJson).ConfigureAwait(false);
            }
            finally
            {
                ActiveStreams.TryRemove(requestId, out _);
            }
        }

        private static string BuildSingleProbeMarkdown(LlmEndpointConfig cfg, string secretSource, LlmStreamResult res)
        {
            var m = res.Metrics;
            var sb = new StringBuilder(1024);
            sb.AppendLine("### FUMI-43 单次流式实测记录");
            sb.AppendLine("- 时间 (UTC): `" + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) + "`");
            sb.AppendLine("- 服务类型: `" + cfg.ProviderKind + "`");
            sb.AppendLine("- Base URL: `" + SecretRedactor.Redact(cfg.BaseUrl) + "`");
            sb.AppendLine("- Model: `" + cfg.Model + "`");
            sb.AppendLine("- 凭据来源: `" + secretSource + "`（已脱敏，未落盘）");
            sb.AppendLine("- 结果状态: `" + (res.IsSuccess ? "SUCCESS (" + res.FinishReason + ")" : res.ErrorKind + " - " + res.ErrorMessage) + "`");
            sb.AppendLine("- HTTP Headers 延迟: `" + m.HeadersLatencyMs.ToString("F2", CultureInfo.InvariantCulture) + " ms`");
            sb.AppendLine("- 首字延迟 (TTFT): `" + (m.TtftMs > 0 ? m.TtftMs.ToString("F2", CultureInfo.InvariantCulture) + " ms" : "-") + "`");
            sb.AppendLine("- 总耗时 (Total): `" + m.TotalLatencyMs.ToString("F2", CultureInfo.InvariantCulture) + " ms`");
            sb.AppendLine("- 分片间隔均值 / P95 / 最大: `" +
                          m.InterChunkMeanMs.ToString("F2", CultureInfo.InvariantCulture) + " / " +
                          m.InterChunkP95Ms.ToString("F2", CultureInfo.InvariantCulture) + " / " +
                          m.InterChunkMaxMs.ToString("F2", CultureInfo.InvariantCulture) + " ms`");
            if (m.CancelPropagationMs > 0)
            {
                sb.AppendLine("- 取消传播耗时: `" + m.CancelPropagationMs.ToString("F2", CultureInfo.InvariantCulture) + " ms`");
            }
            sb.AppendLine("- SSE 帧数 / 内容片数 / 净字符数: `" + m.RawSseFrames + " / " + m.ContentChunks + " / " + m.TotalChars + "`");
            sb.AppendLine("- 情绪标签 / 动作标签 / TTS 分句数: `" + m.EmotionTagCount + " / " + m.ActionTagCount + " / " + m.SentenceCount + "`");
            sb.AppendLine("- 进程 Working Set (RSS): `" + (m.ProcessWorkingSetBytes / 1048576.0).ToString("F2", CultureInfo.InvariantCulture) + " MB`");
            sb.AppendLine("- GC 计数增量 (Gen0/1/2): `+" + m.GcGen0Delta + " / +" + m.GcGen1Delta + " / +" + m.GcGen2Delta + "`");
            return sb.ToString();
        }

        private static string GetJsonString(JsonElement el, string prop)
        {
            if (el.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.String)
            {
                return p.GetString();
            }
            return null;
        }

        private static int GetJsonInt(JsonElement el, string prop, int fallback)
        {
            if (el.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out int v))
            {
                return v;
            }
            return fallback;
        }
    }
}
