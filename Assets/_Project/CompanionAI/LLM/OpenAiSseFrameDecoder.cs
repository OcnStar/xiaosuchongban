using System;
using System.Collections.Generic;
using System.Text;

namespace Xiaosu.CompanionAI.LLM
{
    /// <summary>
    /// Stateful byte-to-UTF8-to-SSE frame decoder.
    /// Safely reassembles multi-byte UTF-8 sequences (such as 3-byte CJK characters or 4-byte emojis)
    /// and SSE event lines split across arbitrary TCP ReadAsync packet boundaries.
    /// </summary>
    public sealed class OpenAiSseFrameDecoder
    {
        private readonly Decoder _utf8Decoder;
        private readonly char[] _charScratch;
        private readonly StringBuilder _lineBuffer;
        private readonly List<string> _eventDataLines;
        private bool _lastCharWasCr;

        public OpenAiSseFrameDecoder(int scratchChars = 4096)
        {
            _utf8Decoder = new UTF8Encoding(false, false).GetDecoder();
            _charScratch = new char[Math.Max(256, scratchChars)];
            _lineBuffer = new StringBuilder(512);
            _eventDataLines = new List<string>(4);
            _lastCharWasCr = false;
        }

        public void PushBytes(byte[] buffer, int offset, int count, bool isFinalBlock, Action<string> onDataFrame)
        {
            if (count < 0 || offset < 0 || (buffer != null && offset + count > buffer.Length))
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            int remainingBytes = count;
            int currentOffset = offset;

            while (remainingBytes > 0 || isFinalBlock)
            {
                int bytesUsed;
                int charsUsed;
                bool completed;
                _utf8Decoder.Convert(
                    buffer ?? Array.Empty<byte>(),
                    currentOffset,
                    remainingBytes,
                    _charScratch,
                    0,
                    _charScratch.Length,
                    isFinalBlock,
                    out bytesUsed,
                    out charsUsed,
                    out completed);

                currentOffset += bytesUsed;
                remainingBytes -= bytesUsed;

                if (charsUsed > 0)
                {
                    ProcessChars(_charScratch, charsUsed, onDataFrame);
                }

                if (remainingBytes == 0 && (!isFinalBlock || completed))
                {
                    break;
                }
            }

            if (isFinalBlock)
            {
                if (_lineBuffer.Length > 0)
                {
                    string trailingLine = _lineBuffer.ToString();
                    _lineBuffer.Length = 0;
                    ProcessLine(trailingLine, onDataFrame);
                }
                DispatchBufferedEvent(onDataFrame);
            }
        }

        private void ProcessChars(char[] chars, int length, Action<string> onDataFrame)
        {
            for (int i = 0; i < length; i++)
            {
                char c = chars[i];
                if (_lastCharWasCr)
                {
                    _lastCharWasCr = false;
                    if (c == '\n')
                    {
                        continue;
                    }
                }

                if (c == '\r')
                {
                    _lastCharWasCr = true;
                    string line = _lineBuffer.ToString();
                    _lineBuffer.Length = 0;
                    ProcessLine(line, onDataFrame);
                }
                else if (c == '\n')
                {
                    string line = _lineBuffer.ToString();
                    _lineBuffer.Length = 0;
                    ProcessLine(line, onDataFrame);
                }
                else
                {
                    _lineBuffer.Append(c);
                }
            }
        }

        private void ProcessLine(string line, Action<string> onDataFrame)
        {
            if (line.Length == 0)
            {
                DispatchBufferedEvent(onDataFrame);
                return;
            }

            // SSE comment / heartbeat line (e.g. ": keep-alive")
            if (line[0] == ':')
            {
                return;
            }

            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                string value = line.Length > 5 && line[5] == ' '
                    ? line.Substring(6)
                    : line.Substring(5);
                _eventDataLines.Add(value);
            }
        }

        private void DispatchBufferedEvent(Action<string> onDataFrame)
        {
            if (_eventDataLines.Count == 0)
            {
                return;
            }

            string payload = _eventDataLines.Count == 1
                ? _eventDataLines[0]
                : string.Join("\n", _eventDataLines.ToArray());
            _eventDataLines.Clear();

            if (onDataFrame != null)
            {
                onDataFrame(payload);
            }
        }
    }
}
