using System;
using System.Text.RegularExpressions;

namespace Xiaosu.CompanionAI.LLM
{
    public static class SecretRedactor
    {
        private static readonly Regex BearerRegex = new Regex(
            @"Bearer\s+[A-Za-z0-9\-\._~\+\/]+=*",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex SkTokenRegex = new Regex(
            @"\bsk-[A-Za-z0-9_\-]{6,}\b",
            RegexOptions.Compiled);

        public static string Redact(string input, string explicitSecret = null)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            string output = input;
            if (!string.IsNullOrEmpty(explicitSecret) && explicitSecret.Length >= 4)
            {
                output = output.Replace(explicitSecret, "[REDACTED_KEY]");
            }

            output = BearerRegex.Replace(output, "Bearer [REDACTED]");
            output = SkTokenRegex.Replace(output, "sk-[REDACTED]");
            return output;
        }
    }
}
