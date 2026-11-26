using System;
using System.Threading;
using System.Threading.Tasks;
using Xiaosu.CompanionAI.LLM;

namespace Xiaosu.Bootstrap
{
    public sealed class MockLlmProvider : ILLMProvider
    {
        private readonly int delayMs;

        public MockLlmProvider(int delayMs = 450)
        {
            this.delayMs = delayMs;
        }

        public LlmEndpointConfig Config { get; } = new LlmEndpointConfig { Name = "mock", Model = "mock" };

        public async Task<LlmStreamResult> StreamChatAsync(
            LlmChatRequest request, Action<LlmStreamEvent> onEvent, CancellationToken cancellationToken)
        {
            try
            {
                var text = new System.Text.StringBuilder();
                foreach (var chunk in new[] { "I'm here. ", "Take your time. ", "We can keep each other company." })
                {
                    await Task.Delay(delayMs, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    text.Append(chunk);
                    onEvent?.Invoke(new LlmStreamEvent { EventType = LlmEventType.TextDelta, Text = chunk });
                }

                cancellationToken.ThrowIfCancellationRequested();
                onEvent?.Invoke(new LlmStreamEvent { EventType = LlmEventType.EmotionTag, TagValue = "calm" });
                cancellationToken.ThrowIfCancellationRequested();
                onEvent?.Invoke(new LlmStreamEvent { EventType = LlmEventType.Completed });
                return new LlmStreamResult { IsSuccess = true, FullCleanText = text.ToString() };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                onEvent?.Invoke(new LlmStreamEvent { EventType = LlmEventType.Cancelled });
                return new LlmStreamResult { IsCancelled = true, ErrorKind = LlmErrorKind.Cancelled };
            }
        }

        public void Dispose() { }
    }
}
