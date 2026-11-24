using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Xiaosu.CompanionAI.LLM
{
    /// <summary>
    /// Resolves API credentials strictly at runtime from memory, local environment variables,
    /// or OS-level secure storage (macOS Keychain via /usr/bin/security).
    /// Never writes credentials to repository files or plain-text logs.
    /// </summary>
    public static class LocalSecretStore
    {
        public static string ResolveApiKey(
            string ephemeralApiKey,
            string envVarName,
            string keychainService,
            string keychainAccount,
            out string sourceDescription)
        {
            if (!string.IsNullOrWhiteSpace(ephemeralApiKey))
            {
                sourceDescription = "ephemeral-memory";
                return ephemeralApiKey.Trim();
            }

            if (!string.IsNullOrWhiteSpace(envVarName))
            {
                string envVal = Environment.GetEnvironmentVariable(envVarName.Trim());
                if (!string.IsNullOrWhiteSpace(envVal))
                {
                    sourceDescription = "env:" + envVarName.Trim();
                    return envVal.Trim();
                }
            }

            if (!string.IsNullOrWhiteSpace(keychainService) &&
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                string secret = ReadMacOsKeychainPassword(keychainService.Trim(), keychainAccount);
                if (!string.IsNullOrWhiteSpace(secret))
                {
                    sourceDescription = "macos-keychain:" + keychainService.Trim();
                    return secret;
                }
            }

            sourceDescription = "none";
            return null;
        }

        private static string ReadMacOsKeychainPassword(string service, string account)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "/usr/bin/security",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                string args = "find-generic-password -s " + QuoteArg(service);
                if (!string.IsNullOrWhiteSpace(account))
                {
                    args += " -a " + QuoteArg(account.Trim());
                }
                args += " -w";
                psi.Arguments = args;

                using (var proc = Process.Start(psi))
                {
                    if (proc == null) return null;
                    string stdout = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(3000);
                    if (proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
                    {
                        return stdout.Trim();
                    }
                }
            }
            catch
            {
                // Ignore Keychain lookup errors and treat as unconfigured.
            }
            return null;
        }

        private static string QuoteArg(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }
}
