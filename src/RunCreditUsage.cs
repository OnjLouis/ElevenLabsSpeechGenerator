using System;
using System.Collections.Generic;
using System.Globalization;

namespace ElevenLabsSpeechGenerator
{
    internal static class RunCreditUsage
    {
        public static decimal? Parse(string header)
        {
            var text = (header ?? "").Trim();
            if (text.Length == 0 || text.Length > 24) return null;
            var point = text.IndexOf('.');
            if (point == 0 || point == text.Length - 1) return null;
            for (int i = 0; i < text.Length; i++)
                if (i != point && (text[i] < '0' || text[i] > '9')) return null;
            decimal value;
            return decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)
                ? (decimal?)value : null;
        }

        public static string CompletionLine(IEnumerable<decimal?> reported)
        {
            bool any = false;
            decimal total = 0;
            foreach (var amount in reported)
            {
                if (!amount.HasValue || amount.Value < 0 || amount.Value > decimal.MaxValue - total) return null;
                any = true;
                total += amount.Value;
            }
            return any ? "Credits used this run: " + total.ToString("#,0.#######################", CultureInfo.CurrentCulture) + "." : null;
        }
    }
}
