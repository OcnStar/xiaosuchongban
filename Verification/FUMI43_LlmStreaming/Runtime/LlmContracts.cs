using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Xiaosu.CompanionAI.LLM
{
    public enum LlmProviderKind
    {
        LocalOpenAiCompatible = 0,
        CloudOpenAiCompatible = 1
    }

    public enum LlmEventType
    {
        HeadersReceived = 0,
        RawSseFrame = 1,
        TextDelta = 2,
        ReasoningDelta = 3,
        EmotionTag = 4,
        ActionTag = 5,
        SentenceReady = 6,
        UsageReported = 7,
        Completed = 8,
        Cancelled = 9,
        Faulted = 10
    }

    public enum LlmErrorKind
    {
        None = 0,
        Cancelled = 1,
        Timeout = 2,
        NetworkUnreachable = 3,
        AuthenticationError = 4,
        RateLimitError = 5,
        BadRequestError = 6,
        ServerError = 7,
        ProtocolError = 8
    }

    public sealed class LlmChatMessage
    {
        public string Role { get; set; }
        public string Content { get; set; }

        public LlmChatMessage()
        {
            Role = "user";
            Content = string.Empty;
        }

        public LlmChatMessage(string role, string content)
        {
            Role = string.IsNullOrWhiteSpace(role) ? "user" : role;
            Content = content ?? string.Empty;
        }
    }

    public sealed class LlmEndpointConfig
    {
        public string Name { get; set; } = "local-openai-compatible";
        public LlmProviderKind ProviderKind { get; set; } = LlmProviderKind.LocalOpenAiCompatible;
        public string BaseUrl { get; set; } = "http://127.0.0.1:11434/v1";
        public string Model { get; set; } = "qwen2.5:7b";
        public int FirstTokenTimeoutMs { get; set; } = 15000;
        public int StreamReadTimeoutMs { get; set; } = 30000;
        public bool IncludeStreamUsage { get; set; } = false;
    }

    public sealed class LlmChatRequest
    {
        public List<LlmChatMessage> Messages { get; set; } = new List<LlmChatMessage>();
        public string ModelOverride { get; set; }
        public double? Temperature { get; set; } = 0.7;
        public int? MaxTokens { get; set; }
        public Dictionary<string, string> ExtraHeaders { get; set; }
    }

    public sealed class LlmStreamEvent
    {
        public LlmEventType EventType { get; set; }
        public double ElapsedMs { get; set; }
        public int ChunkIndex { get; set; }
        public string Text { get; set; }
        public string TagValue { get; set; }
        public string RawFrame { get; set; }
        public int HttpStatusCode { get; set; }
        public LlmErrorKind ErrorKind { get; set; }
    }

    public sealed class LlmTagRecord
    {
        public string Kind { get; set; }
        public string Value { get; set; }
        public double ElapsedMs { get; set; }
    }

    public sealed class LlmStreamMetrics
    {
        public double HeadersLatencyMs { get; set; }
        public double TtftMs { get; set; }
        public double TotalLatencyMs { get; set; }
        public double CancelPropagationMs { get; set; }
        public int RawSseFrames { get; set; }
        public int ContentChunks { get; set; }
        public int TotalChars { get; set; }
        public int EmotionTagCount { get; set; }
        public int ActionTagCount { get; set; }
        public int SentenceCount { get; set; }
        public double InterChunkMeanMs { get; set; }
        public double InterChunkP95Ms { get; set; }
        public double InterChunkMaxMs { get; set; }
        public int PromptTokens { get; set; }
        public int CompletionTokens { get; set; }
        public long ManagedMemoryBeforeBytes { get; set; }
        public long ManagedMemoryAfterBytes { get; set; }
        public long ManagedMemoryDeltaBytes { get; set; }
        public long ProcessWorkingSetBytes { get; set; }
        public int GcGen0Delta { get; set; }
        public int GcGen1Delta { get; set; }
        public int GcGen2Delta { get; set; }
    }

    public sealed class LlmStreamResult
    {
        public bool IsSuccess { get; set; }
        public bool IsCancelled { get; set; }
        public LlmErrorKind ErrorKind { get; set; }
        public int HttpStatusCode { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public string FullCleanText { get; set; } = string.Empty;
        public string FullReasoningText { get; set; } = string.Empty;
        public string FinishReason { get; set; } = string.Empty;
        public LlmStreamMetrics Metrics { get; set; } = new LlmStreamMetrics();
        public List<LlmTagRecord> Tags { get; set; } = new List<LlmTagRecord>();
        public List<string> Sentences { get; set; } = new List<string>();
    }

    public interface ILLMProvider : IDisposable
    {
        LlmEndpointConfig Config { get; }

        Task<LlmStreamResult> StreamChatAsync(
            LlmChatRequest request,
            Action<LlmStreamEvent> onEvent,
            CancellationToken cancellationToken);
    }
}
