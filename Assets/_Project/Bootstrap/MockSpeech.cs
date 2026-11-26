using System.Threading;
using System.Threading.Tasks;
using Xiaosu.CompanionAI.Speech;

namespace Xiaosu.Bootstrap
{
    public sealed class MockSpeech : ITTSProvider
    {
        public Task<PcmAudio> SynthesizeAsync(string text, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new PcmAudio(new float[1600], 16000, 1));
        }

        public void Dispose() { }
    }
}
