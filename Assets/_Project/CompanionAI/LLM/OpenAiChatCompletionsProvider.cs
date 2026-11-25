using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Xiaosu.CompanionAI.LLM
{
    /// <summary>
    /// Minimal, Unity-compatible (.NET Standard 2.1) OpenAI Chat Completions SSE streaming provider.
    /// Supports both local OpenAI-compatible runtimes (Ollama, LM Studio, llama.cpp) and optional
    /// cloud OpenAI-compatible endpoints with deterministic cancellation, timeout isolation,
    /// multi-byte UTF-8 packet reassembly, inline emotion/action tag extraction, and latency/GC metrics.
    /// </summary>
    public sealed class OpenAiChatCompletionsProvider : ILLMProvider
    {
        private readonly LlmEndpointConfig _config;
        private readonly Func<string> _apiKeyAccessor;
        private readonly HttpClient _httpClient;
        private readonly bool _ownsHttpClient;
        private bool _disposed;

        public OpenAiChatCompletionsProvider(
            LlmEndpointConfig config,
            Func<string> apiKeyAccessor = null,
            HttpClient sharedHttpClient = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _apiKeyAccessor = apiKeyAccessor;
            if (sharedHttpClient != null)
            {
                _httpClient = sharedHttpClient;
                _ownsHttpClient = false;
            }
            else
            {
                _httpClient = new HttpClient
                {
                    Timeout = Timeout.InfiniteTimeSpan
                };
                _ownsHttpClient = true;
            }
        }

        public LlmEndpointConfig Config
        {
            get { return _config; }
        }

        public async Task<LlmStreamResult> StreamChatAsync(
            LlmChatRequest request,
            Action<LlmStreamEvent> onEvent,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (request == null) throw new ArgumentNullException(nameof(request));

            var result = new LlmStreamResult();
            var metrics = result.Metrics;
            var beforeSnap = ResourceSampler.Capture();
            metrics.ManagedMemoryBeforeBytes = beforeSnap.ManagedHeapBytes;

            long startTicks = Stopwatch.GetTimestamp();
            long cancelRequestedTicks = 0;

            string apiKey = _apiKeyAccessor != null ? _apiKeyAccessor() : null;
            if (_config.ProviderKind == LlmProviderKind.CloudOpenAiCompatible && string.IsNullOrWhiteSpace(apiKey))
            {
                result.IsSuccess = false;
                result.ErrorKind = LlmErrorKind.AuthenticationError;
                result.ErrorMessage = "Cloud endpoint requires a local runtime API key (Keychain / env / session memory); none was provided.";
                FinalizeMetrics(metrics, beforeSnap, startTicks, 0, null);
                Emit(onEvent, new LlmStreamEvent
                {
                    EventType = LlmEventType.Faulted,
                    ElapsedMs = ElapsedMsSince(startTicks),
                    ErrorKind = result.ErrorKind,
                    Text = result.ErrorMessage
                });
                return result;
            }

            string endpointUrl = BuildChatCompletionsUrl(_config.BaseUrl);
            string model = !string.IsNullOrWhiteSpace(request.ModelOverride) ? request.ModelOverride : _config.Model;
            string requestJson = LlmJson.BuildChatCompletionRequestJson(
                model,
                request.Messages,
                request.Temperature,
                request.MaxTokens,
                _config.IncludeStreamUsage);

            HttpResponseMessage response = null;
            Stream responseStream = null;
            var interChunkDeltasMs = new List<double>(64);
            double lastContentChunkMs = -1.0;
            bool firstContentSeen = false;
            bool sawDoneSentinel = false;
            string protocolFaultMessage = null;
            var reasoningBuilder = new StringBuilder();
            var tagParser = new CompanionStreamTagParser();

            int ttftTimeoutMs = _config.FirstTokenTimeoutMs > 0 ? _config.FirstTokenTimeoutMs : 15000;
            int readTimeoutMs = _config.StreamReadTimeoutMs > 0 ? _config.StreamReadTimeoutMs : 30000;

            using (var activePhaseCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            using (cancellationToken.Register(() =>
            {
                Interlocked.CompareExchange(ref cancelRequestedTicks, Stopwatch.GetTimestamp(), 0);
                DisposeQuietly(responseStream);
                DisposeQuietly(response);
            }))
            {
                try
                {
                    using (var httpReq = new HttpRequestMessage(HttpMethod.Post, endpointUrl))
                    {
                        httpReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
                        httpReq.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
                        if (!string.IsNullOrWhiteSpace(apiKey))
                        {
                            httpReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
                        }
                        if (request.ExtraHeaders != null)
                        {
                            foreach (var kv in request.ExtraHeaders)
                            {
                                if (!string.IsNullOrWhiteSpace(kv.Key) && kv.Value != null)
                                {
                                    httpReq.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                                }
                            }
                        }
                        httpReq.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

                        activePhaseCts.CancelAfter(ttftTimeoutMs);
                        response = await _httpClient.SendAsync(
                            httpReq,
                            HttpCompletionOption.ResponseHeadersRead,
                            activePhaseCts.Token).ConfigureAwait(false);
                    }

                    result.HttpStatusCode = (int)response.StatusCode;
                    metrics.HeadersLatencyMs = ElapsedMsSince(startTicks);
                    Emit(onEvent, new LlmStreamEvent
                    {
                        EventType = LlmEventType.HeadersReceived,
                        ElapsedMs = metrics.HeadersLatencyMs,
                        HttpStatusCode = result.HttpStatusCode
                    });

                    if (!response.IsSuccessStatusCode)
                    {
                        string errBody = string.Empty;
                        if (response.Content != null)
                        {
                            errBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        }
                        result.IsSuccess = false;
                        result.ErrorKind = ClassifyHttpStatus(result.HttpStatusCode);
                        string extracted = LlmJson.ExtractErrorMessage(errBody);
                        result.ErrorMessage = SecretRedactor.Redact(
                            "HTTP " + result.HttpStatusCode + (string.IsNullOrEmpty(extracted) ? string.Empty : " - " + extracted),
                            apiKey);
                        FinalizeMetrics(metrics, beforeSnap, startTicks, cancelRequestedTicks, interChunkDeltasMs);
                        Emit(onEvent, new LlmStreamEvent
                        {
                            EventType = LlmEventType.Faulted,
                            ElapsedMs = metrics.TotalLatencyMs,
                            HttpStatusCode = result.HttpStatusCode,
                            ErrorKind = result.ErrorKind,
                            Text = result.ErrorMessage
                        });
                        return result;
                    }

                    responseStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                    var sseDecoder = new OpenAiSseFrameDecoder();
                    byte[] readBuffer = new byte[2048];

                    Action<string> handleDataFrame = dataFrame =>
                    {
                        if (cancellationToken.IsCancellationRequested || protocolFaultMessage != null)
                        {
                            return;
                        }

                        metrics.RawSseFrames++;
                        double frameMs = ElapsedMsSince(startTicks);
                        Emit(onEvent, new LlmStreamEvent
                        {
                            EventType = LlmEventType.RawSseFrame,
                            ElapsedMs = frameMs,
                            ChunkIndex = metrics.RawSseFrames,
                            RawFrame = SecretRedactor.Redact(dataFrame, apiKey)
                        });

                        if (string.Equals(dataFrame.Trim(), "[DONE]", StringComparison.Ordinal))
                        {
                            sawDoneSentinel = true;
                            return;
                        }

                        OpenAiStreamChunkPayload chunk;
                        try
                        {
                            chunk = LlmJson.ParseStreamChunk(dataFrame);
                        }
                        catch (Exception ex)
                        {
                            protocolFaultMessage = "Malformed SSE JSON chunk at frame #" + metrics.RawSseFrames + ": " + ex.Message;
                            return;
                        }

                        if (!string.IsNullOrEmpty(chunk.ErrorMessage))
                        {
                            protocolFaultMessage = "Stream error payload: " + chunk.ErrorMessage;
                            return;
                        }

                        if (!string.IsNullOrEmpty(chunk.FinishReason))
                        {
                            result.FinishReason = chunk.FinishReason;
                        }

                        if (chunk.PromptTokens.HasValue || chunk.CompletionTokens.HasValue)
                        {
                            if (chunk.PromptTokens.HasValue) metrics.PromptTokens = chunk.PromptTokens.Value;
                            if (chunk.CompletionTokens.HasValue) metrics.CompletionTokens = chunk.CompletionTokens.Value;
                            Emit(onEvent, new LlmStreamEvent
                            {
                                EventType = LlmEventType.UsageReported,
                                ElapsedMs = frameMs,
                                ChunkIndex = metrics.RawSseFrames
                            });
                        }

                        if (!string.IsNullOrEmpty(chunk.ReasoningDelta))
                        {
                            if (!firstContentSeen)
                            {
                                firstContentSeen = true;
                                metrics.TtftMs = frameMs;
                            }
                            reasoningBuilder.Append(chunk.ReasoningDelta);
                            Emit(onEvent, new LlmStreamEvent
                            {
                                EventType = LlmEventType.ReasoningDelta,
                                ElapsedMs = frameMs,
                                ChunkIndex = metrics.RawSseFrames,
                                Text = chunk.ReasoningDelta
                            });
                        }

                        if (!string.IsNullOrEmpty(chunk.ContentDelta))
                        {
                            if (!firstContentSeen)
                            {
                                firstContentSeen = true;
                                metrics.TtftMs = frameMs;
                            }
                            else if (lastContentChunkMs >= 0)
                            {
                                interChunkDeltasMs.Add(Math.Max(0.0, frameMs - lastContentChunkMs));
                            }
                            lastContentChunkMs = frameMs;
                            metrics.ContentChunks++;

                            tagParser.PushContentDelta(
                                chunk.ContentDelta,
                                cleanDelta =>
                                {
                                    metrics.TotalChars += cleanDelta.Length;
                                    Emit(onEvent, new LlmStreamEvent
                                    {
                                        EventType = LlmEventType.TextDelta,
                                        ElapsedMs = ElapsedMsSince(startTicks),
                                        ChunkIndex = metrics.ContentChunks,
                                        Text = cleanDelta
                                    });
                                },
                                (tagKind, tagVal) =>
                                {
                                    double nowMs = ElapsedMsSince(startTicks);
                                    result.Tags.Add(new LlmTagRecord
                                    {
                                        Kind = tagKind,
                                        Value = tagVal,
                                        ElapsedMs = nowMs
                                    });
                                    if (tagKind == "emotion")
                                    {
                                        metrics.EmotionTagCount++;
                                        Emit(onEvent, new LlmStreamEvent
                                        {
                                            EventType = LlmEventType.EmotionTag,
                                            ElapsedMs = nowMs,
                                            ChunkIndex = metrics.ContentChunks,
                                            TagValue = tagVal
                                        });
                                    }
                                    else if (tagKind == "action")
                                    {
                                        metrics.ActionTagCount++;
                                        Emit(onEvent, new LlmStreamEvent
                                        {
                                            EventType = LlmEventType.ActionTag,
                                            ElapsedMs = nowMs,
                                            ChunkIndex = metrics.ContentChunks,
                                            TagValue = tagVal
                                        });
                                    }
                                },
                                sentence =>
                                {
                                    metrics.SentenceCount++;
                                    result.Sentences.Add(sentence);
                                    Emit(onEvent, new LlmStreamEvent
                                    {
                                        EventType = LlmEventType.SentenceReady,
                                        ElapsedMs = ElapsedMsSince(startTicks),
                                        ChunkIndex = metrics.ContentChunks,
                                        Text = sentence
                                    });
                                });
                        }
                    };

                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        int phaseTimeout = firstContentSeen ? readTimeoutMs : ttftTimeoutMs;
                        activePhaseCts.CancelAfter(phaseTimeout);

                        int bytesRead;
                        using (activePhaseCts.Token.Register(() => DisposeQuietly(responseStream)))
                        {
                            bytesRead = await responseStream.ReadAsync(
                                readBuffer,
                                0,
                                readBuffer.Length,
                                activePhaseCts.Token).ConfigureAwait(false);
                        }

                        if (bytesRead == 0)
                        {
                            sseDecoder.PushBytes(readBuffer, 0, 0, true, handleDataFrame);
                            break;
                        }

                        sseDecoder.PushBytes(readBuffer, 0, bytesRead, false, handleDataFrame);

                        if (protocolFaultMessage != null || sawDoneSentinel)
                        {
                            break;
                        }
                    }

                    if (protocolFaultMessage != null)
                    {
                        result.IsSuccess = false;
                        result.ErrorKind = LlmErrorKind.ProtocolError;
                        result.ErrorMessage = SecretRedactor.Redact(protocolFaultMessage, apiKey);
                        result.FullCleanText = tagParser.FullCleanText;
                        result.FullReasoningText = reasoningBuilder.ToString();
                        FinalizeMetrics(metrics, beforeSnap, startTicks, cancelRequestedTicks, interChunkDeltasMs);
                        Emit(onEvent, new LlmStreamEvent
                        {
                            EventType = LlmEventType.Faulted,
                            ElapsedMs = metrics.TotalLatencyMs,
                            HttpStatusCode = result.HttpStatusCode,
                            ErrorKind = result.ErrorKind,
                            Text = result.ErrorMessage
                        });
                        return result;
                    }

                    if (!sawDoneSentinel && string.IsNullOrEmpty(result.FinishReason))
                    {
                        result.IsSuccess = false;
                        result.ErrorKind = LlmErrorKind.ProtocolError;
                        result.ErrorMessage = "Stream ended prematurely before [DONE] or finish_reason.";
                        result.FullCleanText = tagParser.FullCleanText;
                        result.FullReasoningText = reasoningBuilder.ToString();
                        FinalizeMetrics(metrics, beforeSnap, startTicks, cancelRequestedTicks, interChunkDeltasMs);
                        Emit(onEvent, new LlmStreamEvent
                        {
                            EventType = LlmEventType.Faulted,
                            ElapsedMs = metrics.TotalLatencyMs,
                            HttpStatusCode = result.HttpStatusCode,
                            ErrorKind = result.ErrorKind,
                            Text = result.ErrorMessage
                        });
                        return result;
                    }

                    tagParser.Complete(
                        trailingDelta =>
                        {
                            metrics.TotalChars += trailingDelta.Length;
                            Emit(onEvent, new LlmStreamEvent
                            {
                                EventType = LlmEventType.TextDelta,
                                ElapsedMs = ElapsedMsSince(startTicks),
                                ChunkIndex = metrics.ContentChunks,
                                Text = trailingDelta
                            });
                        },
                        trailingSentence =>
                        {
                            metrics.SentenceCount++;
                            result.Sentences.Add(trailingSentence);
                            Emit(onEvent, new LlmStreamEvent
                            {
                                EventType = LlmEventType.SentenceReady,
                                ElapsedMs = ElapsedMsSince(startTicks),
                                ChunkIndex = metrics.ContentChunks,
                                Text = trailingSentence
                            });
                        });

                    result.IsSuccess = true;
                    result.ErrorKind = LlmErrorKind.None;
                    result.FullCleanText = tagParser.FullCleanText;
                    result.FullReasoningText = reasoningBuilder.ToString();
                    if (string.IsNullOrEmpty(result.FinishReason))
                    {
                        result.FinishReason = "stop";
                    }

                    FinalizeMetrics(metrics, beforeSnap, startTicks, cancelRequestedTicks, interChunkDeltasMs);
                    Emit(onEvent, new LlmStreamEvent
                    {
                        EventType = LlmEventType.Completed,
                        ElapsedMs = metrics.TotalLatencyMs,
                        HttpStatusCode = result.HttpStatusCode,
                        Text = result.FullCleanText
                    });
                    return result;
                }
                catch (Exception ex)
                {
                    result.FullCleanText = tagParser.FullCleanText;
                    result.FullReasoningText = reasoningBuilder.ToString();

                    if (cancellationToken.IsCancellationRequested)
                    {
                        result.IsSuccess = false;
                        result.IsCancelled = true;
                        result.ErrorKind = LlmErrorKind.Cancelled;
                        result.ErrorMessage = "Cancelled by caller CancellationToken.";
                        FinalizeMetrics(metrics, beforeSnap, startTicks, cancelRequestedTicks, interChunkDeltasMs);
                        Emit(onEvent, new LlmStreamEvent
                        {
                            EventType = LlmEventType.Cancelled,
                            ElapsedMs = metrics.TotalLatencyMs,
                            HttpStatusCode = result.HttpStatusCode,
                            ErrorKind = LlmErrorKind.Cancelled,
                            Text = result.ErrorMessage
                        });
                        return result;
                    }

                    if (activePhaseCts.IsCancellationRequested)
                    {
                        result.IsSuccess = false;
                        result.ErrorKind = LlmErrorKind.Timeout;
                        result.ErrorMessage = firstContentSeen
                            ? "Stream read idle timeout exceeded (" + readTimeoutMs + " ms)."
                            : "First token timeout (TTFT) exceeded (" + ttftTimeoutMs + " ms).";
                        FinalizeMetrics(metrics, beforeSnap, startTicks, cancelRequestedTicks, interChunkDeltasMs);
                        Emit(onEvent, new LlmStreamEvent
                        {
                            EventType = LlmEventType.Faulted,
                            ElapsedMs = metrics.TotalLatencyMs,
                            HttpStatusCode = result.HttpStatusCode,
                            ErrorKind = LlmErrorKind.Timeout,
                            Text = result.ErrorMessage
                        });
                        return result;
                    }

                    result.IsSuccess = false;
                    result.ErrorKind = ClassifyException(ex);
                    result.ErrorMessage = SecretRedactor.Redact(ex.GetType().Name + ": " + ex.Message, apiKey);
                    FinalizeMetrics(metrics, beforeSnap, startTicks, cancelRequestedTicks, interChunkDeltasMs);
                    Emit(onEvent, new LlmStreamEvent
                    {
                        EventType = LlmEventType.Faulted,
                        ElapsedMs = metrics.TotalLatencyMs,
                        HttpStatusCode = result.HttpStatusCode,
                        ErrorKind = result.ErrorKind,
                        Text = result.ErrorMessage
                    });
                    return result;
                }
                finally
                {
                    DisposeQuietly(responseStream);
                    DisposeQuietly(response);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_ownsHttpClient)
            {
                _httpClient.Dispose();
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(OpenAiChatCompletionsProvider));
            }
        }

        private static string BuildChatCompletionsUrl(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                throw new ArgumentException("BaseUrl must not be empty.");
            }
            string trimmed = baseUrl.Trim().TrimEnd('/');
            if (trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }
            return trimmed + "/chat/completions";
        }

        private static LlmErrorKind ClassifyHttpStatus(int statusCode)
        {
            if (statusCode == 401 || statusCode == 403) return LlmErrorKind.AuthenticationError;
            if (statusCode == 429) return LlmErrorKind.RateLimitError;
            if (statusCode >= 500) return LlmErrorKind.ServerError;
            return LlmErrorKind.BadRequestError;
        }

        private static LlmErrorKind ClassifyException(Exception ex)
        {
            if (ex is HttpRequestException || ex is SocketException || ex.InnerException is SocketException)
            {
                return LlmErrorKind.NetworkUnreachable;
            }
            if (ex is FormatException)
            {
                return LlmErrorKind.ProtocolError;
            }
            return LlmErrorKind.NetworkUnreachable;
        }

        private static void FinalizeMetrics(
            LlmStreamMetrics metrics,
            MachineResourceSnapshot beforeSnap,
            long startTicks,
            long cancelRequestedTicks,
            List<double> interChunkDeltasMs)
        {
            long endTicks = Stopwatch.GetTimestamp();
            metrics.TotalLatencyMs = TicksToMs(endTicks - startTicks);
            long cancelReq = Interlocked.Read(ref cancelRequestedTicks);
            if (cancelReq > 0 && endTicks >= cancelReq)
            {
                metrics.CancelPropagationMs = TicksToMs(endTicks - cancelReq);
            }

            if (interChunkDeltasMs != null && interChunkDeltasMs.Count > 0)
            {
                interChunkDeltasMs.Sort();
                double sum = 0.0;
                for (int i = 0; i < interChunkDeltasMs.Count; i++)
                {
                    sum += interChunkDeltasMs[i];
                }
                metrics.InterChunkMeanMs = sum / interChunkDeltasMs.Count;
                int p95Idx = (int)Math.Ceiling(interChunkDeltasMs.Count * 0.95) - 1;
                if (p95Idx < 0) p95Idx = 0;
                if (p95Idx >= interChunkDeltasMs.Count) p95Idx = interChunkDeltasMs.Count - 1;
                metrics.InterChunkP95Ms = interChunkDeltasMs[p95Idx];
                metrics.InterChunkMaxMs = interChunkDeltasMs[interChunkDeltasMs.Count - 1];
            }

            var afterSnap = ResourceSampler.Capture();
            metrics.ManagedMemoryAfterBytes = afterSnap.ManagedHeapBytes;
            metrics.ManagedMemoryDeltaBytes = afterSnap.ManagedHeapBytes - beforeSnap.ManagedHeapBytes;
            metrics.ProcessWorkingSetBytes = afterSnap.WorkingSetBytes;
            metrics.GcGen0Delta = Math.Max(0, afterSnap.GcGen0Count - beforeSnap.GcGen0Count);
            metrics.GcGen1Delta = Math.Max(0, afterSnap.GcGen1Count - beforeSnap.GcGen1Count);
            metrics.GcGen2Delta = Math.Max(0, afterSnap.GcGen2Count - beforeSnap.GcGen2Count);
        }

        private static double ElapsedMsSince(long startTicks)
        {
            return TicksToMs(Stopwatch.GetTimestamp() - startTicks);
        }

        private static double TicksToMs(long ticks)
        {
            return (double)ticks * 1000.0 / Stopwatch.Frequency;
        }

        private static void Emit(Action<LlmStreamEvent> onEvent, LlmStreamEvent evt)
        {
            if (onEvent != null)
            {
                try
                {
                    onEvent(evt);
                }
                catch
                {
                    // Never allow observer UI callbacks to corrupt transport state.
                }
            }
        }

        private static void DisposeQuietly(IDisposable disposable)
        {
            if (disposable != null)
            {
                try { disposable.Dispose(); } catch { }
            }
        }
    }
}
