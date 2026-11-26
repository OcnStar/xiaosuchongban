using System;
using System.Text;

namespace Xiaosu.CompanionAI.LLM
{
    /// <summary>
    /// Stateful streaming parser that extracts inline control tags such as
    /// [emotion:Happy] and [action:Nod] across arbitrary token boundaries while
    /// emitting clean display deltas and TTS-ready sentence slices.
    /// Unknown or malformed bracket expressions degrade gracefully to plain text.
    /// </summary>
    public sealed class CompanionStreamTagParser
    {
        private const int MaxTagLength = 64;

        private readonly StringBuilder _tagCandidate;
        private readonly StringBuilder _sentenceBuffer;
        private readonly StringBuilder _fullCleanText;
        private bool _insideBracket;

        public CompanionStreamTagParser()
        {
            _tagCandidate = new StringBuilder(64);
            _sentenceBuffer = new StringBuilder(128);
            _fullCleanText = new StringBuilder(512);
            _insideBracket = false;
        }

        public string FullCleanText
        {
            get { return _fullCleanText.ToString(); }
        }

        public void PushContentDelta(
            string delta,
            Action<string> onCleanDelta,
            Action<string, string> onControlTag,
            Action<string> onSentenceReady)
        {
            if (string.IsNullOrEmpty(delta))
            {
                return;
            }

            var cleanChunk = new StringBuilder(delta.Length);

            for (int i = 0; i < delta.Length; i++)
            {
                char c = delta[i];
                if (_insideBracket)
                {
                    if (c == ']')
                    {
                        string candidate = _tagCandidate.ToString();
                        _tagCandidate.Length = 0;
                        _insideBracket = false;

                        string tagKind;
                        string tagVal;
                        if (TryParseControlTag(candidate, out tagKind, out tagVal))
                        {
                            FlushCleanChunk(cleanChunk, onCleanDelta);
                            if (onControlTag != null)
                            {
                                onControlTag(tagKind, tagVal);
                            }
                        }
                        else
                        {
                            AppendPlainText("[" + candidate + "]", cleanChunk, onSentenceReady);
                        }
                    }
                    else if (c == '[' || c == '\r' || c == '\n' || _tagCandidate.Length >= MaxTagLength)
                    {
                        // Not a valid control tag: flush buffered '[' + candidate literally.
                        string literalPrefix = "[" + _tagCandidate.ToString();
                        _tagCandidate.Length = 0;
                        if (c == '[')
                        {
                            AppendPlainText(literalPrefix, cleanChunk, onSentenceReady);
                            _insideBracket = true;
                        }
                        else
                        {
                            _insideBracket = false;
                            AppendPlainText(literalPrefix, cleanChunk, onSentenceReady);
                            AppendPlainChar(c, cleanChunk, onSentenceReady);
                        }
                    }
                    else
                    {
                        _tagCandidate.Append(c);
                    }
                }
                else
                {
                    if (c == '[')
                    {
                        _insideBracket = true;
                        _tagCandidate.Length = 0;
                    }
                    else
                    {
                        AppendPlainChar(c, cleanChunk, onSentenceReady);
                    }
                }
            }

            FlushCleanChunk(cleanChunk, onCleanDelta);
        }

        public void Complete(Action<string> onCleanDelta, Action<string> onSentenceReady)
        {
            if (_insideBracket)
            {
                var trailing = new StringBuilder();
                AppendPlainText("[" + _tagCandidate.ToString(), trailing, onSentenceReady);
                _tagCandidate.Length = 0;
                _insideBracket = false;
                FlushCleanChunk(trailing, onCleanDelta);
            }

            string remainingSentence = _sentenceBuffer.ToString().Trim();
            _sentenceBuffer.Length = 0;
            if (remainingSentence.Length > 0 && onSentenceReady != null)
            {
                onSentenceReady(remainingSentence);
            }
        }

        private void AppendPlainText(string text, StringBuilder cleanChunk, Action<string> onSentenceReady)
        {
            for (int i = 0; i < text.Length; i++)
            {
                AppendPlainChar(text[i], cleanChunk, onSentenceReady);
            }
        }

        private void AppendPlainChar(char c, StringBuilder cleanChunk, Action<string> onSentenceReady)
        {
            cleanChunk.Append(c);
            _fullCleanText.Append(c);
            _sentenceBuffer.Append(c);

            if (IsSentenceTerminator(c))
            {
                string sentence = _sentenceBuffer.ToString().Trim();
                _sentenceBuffer.Length = 0;
                if (sentence.Length > 0 && onSentenceReady != null)
                {
                    onSentenceReady(sentence);
                }
            }
        }

        private static void FlushCleanChunk(StringBuilder cleanChunk, Action<string> onCleanDelta)
        {
            if (cleanChunk.Length > 0)
            {
                string s = cleanChunk.ToString();
                cleanChunk.Length = 0;
                if (onCleanDelta != null)
                {
                    onCleanDelta(s);
                }
            }
        }

        private static bool IsSentenceTerminator(char c)
        {
            return c == '。' || c == '！' || c == '？' || c == '!' || c == '?' || c == '；' || c == ';' || c == '\n';
        }

        private static bool TryParseControlTag(string inner, out string kind, out string value)
        {
            kind = null;
            value = null;
            int colon = inner.IndexOf(':');
            if (colon <= 0 || colon >= inner.Length - 1)
            {
                return false;
            }

            string rawKind = inner.Substring(0, colon).Trim().ToLowerInvariant();
            if (rawKind != "emotion" && rawKind != "action")
            {
                return false;
            }

            string rawVal = inner.Substring(colon + 1).Trim();
            if (rawVal.Length == 0 || rawVal.Length > 32)
            {
                return false;
            }

            for (int i = 0; i < rawVal.Length; i++)
            {
                char ch = rawVal[i];
                bool valid = (ch >= 'a' && ch <= 'z')
                    || (ch >= 'A' && ch <= 'Z')
                    || (ch >= '0' && ch <= '9')
                    || ch == '_' || ch == '-';
                if (!valid)
                {
                    return false;
                }
            }

            kind = rawKind;
            value = rawVal;
            return true;
        }
    }
}
