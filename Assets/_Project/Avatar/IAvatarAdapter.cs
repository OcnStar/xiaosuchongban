using System;
using System.Threading;
using System.Threading.Tasks;

namespace Xiaosu.Avatar
{
    public interface IAvatarAdapter : IDisposable
    {
        Task LoadAsync(string assetId, CancellationToken cancellationToken);
        void SetExpression(string expressionId);
        void PlayAction(string actionId);
        void SetMouthOpen(float normalizedAmount);
    }
}
