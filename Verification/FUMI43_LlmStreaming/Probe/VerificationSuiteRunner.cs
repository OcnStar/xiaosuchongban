using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Xiaosu.CompanionAI.LLM.Probe
{
    public sealed class VerificationCaseResult
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public bool Passed { get; set; }
        public string Summary { get; set; } = string.Empty;
        public double TtftMs { get; set; }
        public double TotalMs { get; set; }
        public double CancelPropagationMs { get; set; }
        public int SseFrames { get; set; }
        public int ContentChunks { get; set; }
        public long ManagedDeltaBytes { get; set; }
    }

    public sealed class VerificationSuiteReport
    {
        public string GeneratedAtUtc { get; set; } = string.Empty;
        public MachineResourceSnapshot MachineBefore { get; set; }
        public MachineResourceSnapshot MachineAfter { get; set; }
        public int TotalCases { get; set; }
        public int PassedCases { get; set; }
        public bool AllPassed { get; set; }
        public List<VerificationCaseResult> Cases { get; set; } = new List<VerificationCaseResult>();
        public string MarkdownReport { get; set; } = string.Empty;
    }

    public static class VerificationSuiteRunner
    {
        public static async Task<VerificationSuiteReport> RunAllAsync(string mockBaseUrl, CancellationToken ct)
        {
            var report = new VerificationSuiteReport
            {
                GeneratedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                MachineBefore = ResourceSampler.Capture()
            };

            report.Cases.Add(await Case1_NormalSseAndMetricsAsync(mockBaseUrl, ct).ConfigureAwait(false));
            report.Cases.Add(await Case2_MultiByteUtf8TcpFragmentationAsync(mockBaseUrl, ct).ConfigureAwait(false));
            report.Cases.Add(await Case3_CrossChunkControlTagExtractionAsync(mockBaseUrl, ct).ConfigureAwait(false));
            report.Cases.Add(await Case4_MidStreamCancellationAsync(mockBaseUrl, ct).ConfigureAwait(false));
            report.Cases.Add(await Case5_HttpErrorAndRedactionAsync(mockBaseUrl, ct).ConfigureAwait(false));
            report.Cases.Add(await Case6_PrematureDropAndMalformedJsonAsync(mockBaseUrl, ct).ConfigureAwait(false));
            report.Cases.Add(await Case7_TtftTimeoutIsolationAsync(mockBaseUrl, ct).ConfigureAwait(false));
            report.Cases.Add(await Case8_CloudMissingKeyGuardAsync(mockBaseUrl, ct).ConfigureAwait(false));

            report.MachineAfter = ResourceSampler.Capture();
            report.TotalCases = report.Cases.Count;
            int passed = 0;
            foreach (var c in report.Cases)
            {
                if (c.Passed) passed++;
            }
            report.PassedCases = passed;
            report.AllPassed = passed == report.TotalCases;
            report.MarkdownReport = BuildMarkdown(report);
            return report;
        }

        private static async Task<VerificationCaseResult> Case1_NormalSseAndMetricsAsync(string mockBaseUrl, CancellationToken ct)
        {
            using var provider = new OpenAiChatCompletionsProvider(new LlmEndpointConfig
            {
                Name = "loopback-normal",
                ProviderKind = LlmProviderKind.LocalOpenAiCompatible,
                BaseUrl = mockBaseUrl,
                Model = "mock-xiaosu-stream",
                IncludeStreamUsage = true
            });

            var req = new LlmChatRequest();
            req.Messages.Add(new LlmChatMessage("system", "你是晓苏。"));
            req.Messages.Add(new LlmChatMessage("user", "你好晓苏，今天天气不错"));

            var res = await provider.StreamChatAsync(req, null, ct).ConfigureAwait(false);
            bool ok = res.IsSuccess
                      && res.HttpStatusCode == 200
                      && res.FinishReason == "stop"
                      && res.Metrics.TtftMs > 0
                      && res.Metrics.EmotionTagCount == 2
                      && res.Metrics.ActionTagCount == 1
                      && res.Sentences.Count >= 2
                      && res.Metrics.CompletionTokens == 42;

            return new VerificationCaseResult
            {
                Id = "C1",
                Title = "标准 OpenAI SSE 流式对话、分句与 Usage 统计",
                Passed = ok,
                Summary = ok
                    ? $"HTTP 200, Finish={res.FinishReason}, 情绪标签={res.Metrics.EmotionTagCount}, 动作标签={res.Metrics.ActionTagCount}, 分句={res.Sentences.Count}, Tokens={res.Metrics.PromptTokens}+{res.Metrics.CompletionTokens}"
                    : $"失败: {res.ErrorKind} {res.ErrorMessage}",
                TtftMs = res.Metrics.TtftMs,
                TotalMs = res.Metrics.TotalLatencyMs,
                SseFrames = res.Metrics.RawSseFrames,
                ContentChunks = res.Metrics.ContentChunks,
                ManagedDeltaBytes = res.Metrics.ManagedMemoryDeltaBytes
            };
        }

        private static async Task<VerificationCaseResult> Case2_MultiByteUtf8TcpFragmentationAsync(string mockBaseUrl, CancellationToken ct)
        {
            using var provider = new OpenAiChatCompletionsProvider(new LlmEndpointConfig
            {
                Name = "loopback-utf8-split",
                ProviderKind = LlmProviderKind.LocalOpenAiCompatible,
                BaseUrl = mockBaseUrl,
                Model = "mock-utf8-split"
            });

            var req = new LlmChatRequest();
            req.Messages.Add(new LlmChatMessage("user", "UTF-8 跨包测试"));

            var res = await provider.StreamChatAsync(req, null, ct).ConfigureAwait(false);
            const string expected = "晓苏宠伴✨流式UTF-8跨包重组零乱码验证通过！";
            bool hasReplacementChar = res.FullCleanText.IndexOf('\uFFFD') >= 0;
            bool ok = res.IsSuccess && !hasReplacementChar && res.FullCleanText == expected;

            return new VerificationCaseResult
            {
                Id = "C2",
                Title = "TCP 5字节切片跨包重组（中文 3字节 / Emoji 4字节 / 心跳注释行 / 无空格 data:）",
                Passed = ok,
                Summary = ok
                    ? $"精确还原「{res.FullCleanText}」，无 U+FFFD 乱码"
                    : $"实际输出: 「{res.FullCleanText}」, Error={res.ErrorMessage}",
                TtftMs = res.Metrics.TtftMs,
                TotalMs = res.Metrics.TotalLatencyMs,
                SseFrames = res.Metrics.RawSseFrames,
                ContentChunks = res.Metrics.ContentChunks,
                ManagedDeltaBytes = res.Metrics.ManagedMemoryDeltaBytes
            };
        }

        private static async Task<VerificationCaseResult> Case3_CrossChunkControlTagExtractionAsync(string mockBaseUrl, CancellationToken ct)
        {
            using var provider = new OpenAiChatCompletionsProvider(new LlmEndpointConfig
            {
                Name = "loopback-tag-split",
                ProviderKind = LlmProviderKind.LocalOpenAiCompatible,
                BaseUrl = mockBaseUrl,
                Model = "mock-tag-split"
            });

            var req = new LlmChatRequest();
            req.Messages.Add(new LlmChatMessage("user", "跨分片标签测试"));

            var res = await provider.StreamChatAsync(req, null, ct).ConfigureAwait(false);
            const string expectedClean = " 我明白你的意思啦！ 要不要休息五分钟？[1] 附注保留";
            bool ok = res.IsSuccess
                      && res.Tags.Count == 3
                      && res.Tags[0].Kind == "emotion" && res.Tags[0].Value == "Happy"
                      && res.Tags[1].Kind == "action" && res.Tags[1].Value == "Nod"
                      && res.Tags[2].Kind == "emotion" && res.Tags[2].Value == "Gentle"
                      && res.FullCleanText == expectedClean;

            return new VerificationCaseResult
            {
                Id = "C3",
                Title = "跨 Token 分片内联标签剥离与普通方括号降级保留",
                Passed = ok,
                Summary = ok
                    ? $"提取 [emotion:Happy, action:Nod, emotion:Gentle]，保留非控制文本「[1] 附注保留」，分句数={res.Sentences.Count}"
                    : $"输出不符: Text=「{res.FullCleanText}」, Tags={res.Tags.Count}",
                TtftMs = res.Metrics.TtftMs,
                TotalMs = res.Metrics.TotalLatencyMs,
                SseFrames = res.Metrics.RawSseFrames,
                ContentChunks = res.Metrics.ContentChunks,
                ManagedDeltaBytes = res.Metrics.ManagedMemoryDeltaBytes
            };
        }

        private static async Task<VerificationCaseResult> Case4_MidStreamCancellationAsync(string mockBaseUrl, CancellationToken ct)
        {
            using var provider = new OpenAiChatCompletionsProvider(new LlmEndpointConfig
            {
                Name = "loopback-cancel",
                ProviderKind = LlmProviderKind.LocalOpenAiCompatible,
                BaseUrl = mockBaseUrl,
                Model = "mock-slow-stream"
            });

            using var cancelCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            int deltasAfterCancel = 0;
            int chunksSeen = 0;

            var req = new LlmChatRequest();
            req.Messages.Add(new LlmChatMessage("user", "慢速流取消测试"));

            var res = await provider.StreamChatAsync(req, evt =>
            {
                if (evt.EventType == LlmEventType.TextDelta)
                {
                    if (cancelCts.IsCancellationRequested)
                    {
                        Interlocked.Increment(ref deltasAfterCancel);
                    }
                    int count = Interlocked.Increment(ref chunksSeen);
                    if (count == 2)
                    {
                        cancelCts.Cancel();
                    }
                }
            }, cancelCts.Token).ConfigureAwait(false);

            bool ok = res.IsCancelled
                      && !res.IsSuccess
                      && res.ErrorKind == LlmErrorKind.Cancelled
                      && deltasAfterCancel == 0
                      && res.Metrics.CancelPropagationMs < 250.0;

            return new VerificationCaseResult
            {
                Id = "C4",
                Title = "流式中途取消传播（模拟语音打断 Barge-in / 零残留 Token 污染）",
                Passed = ok,
                Summary = ok
                    ? $"第2片后触发取消，传播耗时 {res.Metrics.CancelPropagationMs:F2} ms，取消后残留事件={deltasAfterCancel}"
                    : $"取消异常: IsCancelled={res.IsCancelled}, Kind={res.ErrorKind}, 传播={res.Metrics.CancelPropagationMs:F2} ms, 残留={deltasAfterCancel}",
                TtftMs = res.Metrics.TtftMs,
                TotalMs = res.Metrics.TotalLatencyMs,
                CancelPropagationMs = res.Metrics.CancelPropagationMs,
                SseFrames = res.Metrics.RawSseFrames,
                ContentChunks = res.Metrics.ContentChunks,
                ManagedDeltaBytes = res.Metrics.ManagedMemoryDeltaBytes
            };
        }

        private static async Task<VerificationCaseResult> Case5_HttpErrorAndRedactionAsync(string mockBaseUrl, CancellationToken ct)
        {
            const string fakeKey = "sk-test-secret-should-never-leak-9999";
            LlmStreamResult r401, r429, r500;

            using (var p401 = new OpenAiChatCompletionsProvider(
                new LlmEndpointConfig { BaseUrl = mockBaseUrl, Model = "mock-http-401", ProviderKind = LlmProviderKind.CloudOpenAiCompatible },
                () => fakeKey))
            {
                var req = new LlmChatRequest();
                req.Messages.Add(new LlmChatMessage("user", "401 test"));
                r401 = await p401.StreamChatAsync(req, null, ct).ConfigureAwait(false);
            }

            using (var p429 = new OpenAiChatCompletionsProvider(
                new LlmEndpointConfig { BaseUrl = mockBaseUrl, Model = "mock-http-429" }))
            {
                var req = new LlmChatRequest();
                req.Messages.Add(new LlmChatMessage("user", "429 test"));
                r429 = await p429.StreamChatAsync(req, null, ct).ConfigureAwait(false);
            }

            using (var p500 = new OpenAiChatCompletionsProvider(
                new LlmEndpointConfig { BaseUrl = mockBaseUrl, Model = "mock-http-500" }))
            {
                var req = new LlmChatRequest();
                req.Messages.Add(new LlmChatMessage("user", "500 test"));
                r500 = await p500.StreamChatAsync(req, null, ct).ConfigureAwait(false);
            }

            string redactedSample = SecretRedactor.Redact("Authorization: Bearer " + fakeKey + " key=" + fakeKey, fakeKey);
            bool noSecretLeak = redactedSample.IndexOf(fakeKey, StringComparison.Ordinal) < 0
                                && r401.ErrorMessage.IndexOf(fakeKey, StringComparison.Ordinal) < 0;

            bool ok = r401.ErrorKind == LlmErrorKind.AuthenticationError && r401.HttpStatusCode == 401
                      && r429.ErrorKind == LlmErrorKind.RateLimitError && r429.HttpStatusCode == 429
                      && r500.ErrorKind == LlmErrorKind.ServerError && r500.HttpStatusCode == 500
                      && noSecretLeak;

            return new VerificationCaseResult
            {
                Id = "C5",
                Title = "HTTP 401 / 429 / 500 异常分级与 Bearer 密钥脱敏检查",
                Passed = ok,
                Summary = ok
                    ? "401→AuthenticationError, 429→RateLimitError, 500→ServerError，日志脱敏无密钥残留"
                    : $"分级失败: 401={r401.ErrorKind}, 429={r429.ErrorKind}, 500={r500.ErrorKind}, NoLeak={noSecretLeak}",
                TotalMs = r401.Metrics.TotalLatencyMs + r429.Metrics.TotalLatencyMs + r500.Metrics.TotalLatencyMs
            };
        }

        private static async Task<VerificationCaseResult> Case6_PrematureDropAndMalformedJsonAsync(string mockBaseUrl, CancellationToken ct)
        {
            LlmStreamResult rDrop, rBadJson;

            using (var pDrop = new OpenAiChatCompletionsProvider(
                new LlmEndpointConfig { BaseUrl = mockBaseUrl, Model = "mock-drop-midstream" }))
            {
                var req = new LlmChatRequest();
                req.Messages.Add(new LlmChatMessage("user", "drop test"));
                rDrop = await pDrop.StreamChatAsync(req, null, ct).ConfigureAwait(false);
            }

            using (var pBad = new OpenAiChatCompletionsProvider(
                new LlmEndpointConfig { BaseUrl = mockBaseUrl, Model = "mock-malformed-json" }))
            {
                var req = new LlmChatRequest();
                req.Messages.Add(new LlmChatMessage("user", "malformed test"));
                rBadJson = await pBad.StreamChatAsync(req, null, ct).ConfigureAwait(false);
            }

            bool dropDetected = !rDrop.IsSuccess &&
                                (rDrop.ErrorKind == LlmErrorKind.ProtocolError || rDrop.ErrorKind == LlmErrorKind.NetworkUnreachable);
            bool badJsonDetected = !rBadJson.IsSuccess && rBadJson.ErrorKind == LlmErrorKind.ProtocolError;
            bool ok = dropDetected && badJsonDetected;

            return new VerificationCaseResult
            {
                Id = "C6",
                Title = "流式中途断连（缺 [DONE]/finish_reason）与损坏 JSON 帧拦截",
                Passed = ok,
                Summary = ok
                    ? $"断流捕获→{rDrop.ErrorKind}（未误判为完整回复）；坏帧捕获→{rBadJson.ErrorKind}"
                    : $"Drop={rDrop.IsSuccess}/{rDrop.ErrorKind}, BadJson={rBadJson.IsSuccess}/{rBadJson.ErrorKind}",
                TotalMs = rDrop.Metrics.TotalLatencyMs + rBadJson.Metrics.TotalLatencyMs
            };
        }

        private static async Task<VerificationCaseResult> Case7_TtftTimeoutIsolationAsync(string mockBaseUrl, CancellationToken ct)
        {
            using var provider = new OpenAiChatCompletionsProvider(new LlmEndpointConfig
            {
                Name = "loopback-timeout",
                ProviderKind = LlmProviderKind.LocalOpenAiCompatible,
                BaseUrl = mockBaseUrl,
                Model = "mock-stall-ttft",
                FirstTokenTimeoutMs = 180,
                StreamReadTimeoutMs = 500
            });

            var req = new LlmChatRequest();
            req.Messages.Add(new LlmChatMessage("user", "timeout test"));
            var res = await provider.StreamChatAsync(req, null, ct).ConfigureAwait(false);

            bool ok = !res.IsSuccess
                      && !res.IsCancelled
                      && res.ErrorKind == LlmErrorKind.Timeout
                      && res.Metrics.TotalLatencyMs < 1500.0;

            return new VerificationCaseResult
            {
                Id = "C7",
                Title = "首包超时（TTFT Timeout）独立判定（不与用户主动取消混淆）",
                Passed = ok,
                Summary = ok
                    ? $"设定 180 ms 超时，实际 {res.Metrics.TotalLatencyMs:F1} ms 返回 Timeout（IsCancelled=false）"
                    : $"Timeout 判定异常: Kind={res.ErrorKind}, IsCancelled={res.IsCancelled}, Cost={res.Metrics.TotalLatencyMs:F1} ms",
                TotalMs = res.Metrics.TotalLatencyMs
            };
        }

        private static async Task<VerificationCaseResult> Case8_CloudMissingKeyGuardAsync(string mockBaseUrl, CancellationToken ct)
        {
            using var provider = new OpenAiChatCompletionsProvider(new LlmEndpointConfig
            {
                Name = "cloud-no-key",
                ProviderKind = LlmProviderKind.CloudOpenAiCompatible,
                BaseUrl = mockBaseUrl,
                Model = "gpt-4o-mini"
            }, () => null);

            var req = new LlmChatRequest();
            req.Messages.Add(new LlmChatMessage("user", "cloud guard"));
            var res = await provider.StreamChatAsync(req, null, ct).ConfigureAwait(false);

            bool ok = !res.IsSuccess && res.ErrorKind == LlmErrorKind.AuthenticationError;
            return new VerificationCaseResult
            {
                Id = "C8",
                Title = "云端 Provider 缺失本机密钥前置拦截（防止无密钥外发或静默回退）",
                Passed = ok,
                Summary = ok
                    ? "未配置本机密钥时立即本地拦截并返回 AuthenticationError，不静默回退其他端点"
                    : $"拦截异常: {res.ErrorKind} {res.ErrorMessage}",
                TotalMs = res.Metrics.TotalLatencyMs
            };
        }

        private static string BuildMarkdown(VerificationSuiteReport r)
        {
            var sb = new StringBuilder(2048);
            sb.AppendLine("# FUMI-43 OpenAI 兼容流式 LLM C# 自检报告");
            sb.AppendLine();
            sb.AppendLine("- 生成时间 (UTC): `" + r.GeneratedAtUtc + "`");
            sb.AppendLine("- 运行系统: `" + r.MachineAfter.OsDescription + " (" + r.MachineAfter.ProcessArchitecture + ")`");
            sb.AppendLine("- .NET 运行时: `" + r.MachineAfter.FrameworkDescription + "`");
            sb.AppendLine("- CPU 逻辑核心数: `" + r.MachineAfter.ProcessorCount + "`");
            sb.AppendLine("- 进程 Working Set (RSS): `" + FormatMb(r.MachineAfter.WorkingSetBytes) + " MB`");
            sb.AppendLine("- 托管堆 (Managed Heap): `" + FormatKb(r.MachineBefore.ManagedHeapBytes) + " KB -> " + FormatKb(r.MachineAfter.ManagedHeapBytes) + " KB`");
            sb.AppendLine("- GC 计数变化 (Gen0/1/2): `+" +
                          (r.MachineAfter.GcGen0Count - r.MachineBefore.GcGen0Count) + " / +" +
                          (r.MachineAfter.GcGen1Count - r.MachineBefore.GcGen1Count) + " / +" +
                          (r.MachineAfter.GcGen2Count - r.MachineBefore.GcGen2Count) + "`");
            sb.AppendLine("- 用例通过率: **`" + r.PassedCases + " / " + r.TotalCases + "`**");
            sb.AppendLine();
            sb.AppendLine("| 编号 | 验证项 | 结果 | TTFT (ms) | 总耗时 (ms) | 关键结论 |");
            sb.AppendLine("| :--- | :--- | :--- | ---: | ---: | :--- |");
            foreach (var c in r.Cases)
            {
                sb.Append("| ").Append(c.Id)
                  .Append(" | ").Append(c.Title)
                  .Append(" | ").Append(c.Passed ? "PASS" : "FAIL")
                  .Append(" | ").Append(c.TtftMs > 0 ? c.TtftMs.ToString("F2", CultureInfo.InvariantCulture) : "-")
                  .Append(" | ").Append(c.TotalMs.ToString("F2", CultureInfo.InvariantCulture))
                  .Append(" | ").Append(c.Summary.Replace("|", "\\|"))
                  .AppendLine(" |");
            }
            sb.AppendLine();
            sb.AppendLine("> 说明：上述自检针对本机回环 OpenAI Chat Completions 兼容 SSE 模拟服务端执行，证明 C# 协议解析、跨包 UTF-8、内联情绪/动作标签剥离、取消传播与异常分级逻辑成立；真实外部本地模型服务（如 Ollama / LM Studio 断网实测）与真实云端 API 需在具备对应模型/本机密钥后通过验证台或 CLI 实测补录。");
            return sb.ToString();
        }

        private static string FormatMb(long bytes)
        {
            return (bytes / (1024.0 * 1024.0)).ToString("F2", CultureInfo.InvariantCulture);
        }

        private static string FormatKb(long bytes)
        {
            return (bytes / 1024.0).ToString("F1", CultureInfo.InvariantCulture);
        }
    }
}
