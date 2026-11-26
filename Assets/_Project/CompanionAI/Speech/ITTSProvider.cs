using System;
using System.Threading;
using System.Threading.Tasks;

namespace Xiaosu.CompanionAI.Speech
{
    public sealed class PcmAudio
    {
        public PcmAudio(float[] samples, int sampleRate, int channels)
        {
            Samples = samples ?? throw new ArgumentNullException(nameof(samples));
            SampleRate = sampleRate;
            Channels = channels;
        }

        public float[] Samples { get; }
        public int SampleRate { get; }
        public int Channels { get; }
    }

    public interface ITTSProvider : IDisposable
    {
        Task<PcmAudio> SynthesizeAsync(string text, CancellationToken cancellationToken);
    }
}
