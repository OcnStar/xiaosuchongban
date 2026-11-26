using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Xiaosu.Avatar;

namespace Xiaosu.Bootstrap
{
    public sealed class MockAvatar : IAvatarAdapter
    {
        public string AssetId { get; private set; }
        public string Expression { get; private set; } = "neutral";
        public string Action { get; private set; } = "idle";
        public float MouthOpen { get; private set; }

        public Task LoadAsync(string assetId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AssetId = assetId;
            return Task.CompletedTask;
        }

        public void SetExpression(string expressionId) => Expression = expressionId;
        public void PlayAction(string actionId) => Action = actionId;
        public void SetMouthOpen(float normalizedAmount) => MouthOpen = Mathf.Clamp01(normalizedAmount);
        public void Dispose() { }
    }
}
