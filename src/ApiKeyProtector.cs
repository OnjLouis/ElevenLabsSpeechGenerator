using System;
using System.Security.Cryptography;
using System.Text;

namespace ElevenLabsSpeechGenerator
{
    internal static class ApiKeyProtector
    {
        // Keep the existing music app's protected-file format interchangeable.
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ElevenLabsMusicGenerator.ApiKey.v1");

        public static string Protect(string key)
        {
            var data = Encoding.UTF8.GetBytes(key);
            try { return Convert.ToBase64String(ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser)); }
            finally { Array.Clear(data, 0, data.Length); }
        }

        public static string Unprotect(string protectedKey)
        {
            var data = ProtectedData.Unprotect(Convert.FromBase64String(protectedKey), Entropy, DataProtectionScope.CurrentUser);
            try { return Encoding.UTF8.GetString(data); }
            finally { Array.Clear(data, 0, data.Length); }
        }
    }
}
