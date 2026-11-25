using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Xiaosu.Core;

namespace Xiaosu.Bootstrap
{
    // Session-only demo store with snapshot semantics. It never writes to disk.
    public sealed class InMemoryStore : IMemoryStore
    {
        private string preferences = JsonUtility.ToJson(new CompanionPreferences());
        private string profile;
        private readonly Dictionary<string, string> habits = new Dictionary<string, string>();

        public Task<CompanionPreferences> LoadPreferencesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(JsonUtility.FromJson<CompanionPreferences>(preferences));
        }

        public Task SavePreferencesAsync(CompanionPreferences value, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            preferences = JsonUtility.ToJson(value ?? throw new ArgumentNullException(nameof(value)));
            return Task.CompletedTask;
        }

        public Task<HatchedProfile> LoadProfileAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(profile == null ? null : JsonUtility.FromJson<HatchedProfile>(profile));
        }

        public Task SaveProfileAsync(HatchedProfile value, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            profile = JsonUtility.ToJson(value ?? throw new ArgumentNullException(nameof(value)));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<LearnedHabit>> LoadHabitsAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<LearnedHabit>>(
                habits.Values.Select(value => JsonUtility.FromJson<LearnedHabit>(value)).ToArray());
        }

        public Task SaveHabitAsync(LearnedHabit habit, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (habit == null || string.IsNullOrWhiteSpace(habit.Id))
                throw new ArgumentException("A habit ID is required.", nameof(habit));

            habits[habit.Id] = JsonUtility.ToJson(habit);
            return Task.CompletedTask;
        }

        public Task RemoveHabitAsync(string id, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            habits.Remove(id);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            habits.Clear();
            profile = null;
        }
    }
}
