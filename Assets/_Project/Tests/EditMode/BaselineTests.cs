using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Xiaosu.Bootstrap;
using Xiaosu.CompanionAI.LLM;
using Xiaosu.Core;

namespace Xiaosu.Tests
{
    public sealed class BaselineTests
    {
        [Test]
        public async Task MockEmitsTextEmotionAndCompletion()
        {
            using var provider = new MockLlmProvider(0);
            var events = new List<LlmEventType>();
            var result = await provider.StreamChatAsync(new LlmChatRequest(),
                item => events.Add(item.EventType), CancellationToken.None);
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(events, Does.Contain(LlmEventType.TextDelta));
            Assert.That(events[events.Count - 2], Is.EqualTo(LlmEventType.EmotionTag));
            Assert.That(events[events.Count - 1], Is.EqualTo(LlmEventType.Completed));
        }

        [Test]
        public async Task CancelStopsFurtherTextAndEmotionEvents()
        {
            using var provider = new MockLlmProvider(0);
            using var cancellation = new CancellationTokenSource();
            var events = new List<LlmEventType>();
            var result = await provider.StreamChatAsync(new LlmChatRequest(), item =>
            {
                events.Add(item.EventType);
                if (item.EventType == LlmEventType.TextDelta) cancellation.Cancel();
            }, cancellation.Token);

            Assert.That(result.IsCancelled, Is.True);
            Assert.That(events, Is.EqualTo(new[] { LlmEventType.TextDelta, LlmEventType.Cancelled }));
        }

        [Test]
        public async Task RemovingHabitDoesNotChangeHatchedPersonality()
        {
            using var store = new InMemoryStore();
            var token = CancellationToken.None;
            await store.SaveProfileAsync(new HatchedProfile { PersonalityId = "curious" }, token);
            await store.SaveHabitAsync(new LearnedHabit { Id = "focus", Behavior = "wait" }, token);
            Assert.That(await store.LoadHabitsAsync(token), Has.Count.EqualTo(1));
            await store.RemoveHabitAsync("focus", token);
            Assert.That(await store.LoadHabitsAsync(token), Is.Empty);
            Assert.That((await store.LoadProfileAsync(token)).PersonalityId, Is.EqualTo("curious"));
        }
    }
}
