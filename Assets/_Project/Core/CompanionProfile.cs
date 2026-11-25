using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Xiaosu.Core
{
    public enum SafetyResponse
    {
        EverydaySupport,
        ProfessionalReferral,
        ClarifyImmediateSafety,
        EmergencyHelp,
        HarmfulRequestBoundary
    }

    [Serializable]
    public sealed class CompanionPreferences
    {
        public bool ProactiveConversationEnabled;
        public bool Muted;
        public string TonePreference = string.Empty;
    }

    [Serializable]
    public sealed class HatchedProfile
    {
        public string CharacterId = string.Empty;
        public string PersonalityId = string.Empty;
        public string VoiceStyle = string.Empty;
        public string AppearanceId = string.Empty;
        public string[] BehavioralTendencies = Array.Empty<string>();
    }

    [Serializable]
    public sealed class LearnedHabit
    {
        public string Id = string.Empty;
        public string Trigger = string.Empty;
        public string Behavior = string.Empty;
    }

    public interface IMemoryStore : IDisposable
    {
        Task<CompanionPreferences> LoadPreferencesAsync(CancellationToken cancellationToken);
        Task SavePreferencesAsync(CompanionPreferences preferences, CancellationToken cancellationToken);
        Task<HatchedProfile> LoadProfileAsync(CancellationToken cancellationToken);
        Task SaveProfileAsync(HatchedProfile profile, CancellationToken cancellationToken);
        Task<IReadOnlyList<LearnedHabit>> LoadHabitsAsync(CancellationToken cancellationToken);
        Task SaveHabitAsync(LearnedHabit habit, CancellationToken cancellationToken);
        Task RemoveHabitAsync(string id, CancellationToken cancellationToken);
    }
}
