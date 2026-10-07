using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Web.Script.Serialization;

namespace ElevenLabsSpeechGenerator
{
    internal sealed class SubscriptionBalance
    {
        public long Used { get; private set; }
        public long Limit { get; private set; }
        public long Remaining { get { return Math.Max(0, Limit - Used); } }
        public DateTimeOffset? NextReset { get; private set; }
        public bool UsageBasedBillingEnabled { get; private set; }
        public bool UnlimitedExtension { get; private set; }
        public decimal? CurrentOverage { get; private set; }
        public string OverageCurrency { get; private set; }

        public static SubscriptionBalance Parse(string json)
        {
            Dictionary<string, object> values;
            try { values = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json); }
            catch (Exception ex) { throw new InvalidDataException("ElevenLabs returned invalid subscription data.", ex); }
            if (values == null) throw new InvalidDataException("ElevenLabs returned no subscription data.");
            var used = RequiredCount(values, "character_count");
            var limit = RequiredCount(values, "character_limit");
            DateTimeOffset? reset = null;
            object rawReset;
            if (values.TryGetValue("next_character_count_reset_unix", out rawReset) && rawReset != null)
            {
                try
                {
                    var seconds = Convert.ToInt64(rawReset, CultureInfo.InvariantCulture);
                    if (seconds <= 0) throw new ArgumentOutOfRangeException("next_character_count_reset_unix");
                    reset = DateTimeOffset.FromUnixTimeSeconds(seconds);
                }
                catch (Exception ex) { throw new InvalidDataException("ElevenLabs returned an invalid credit reset date.", ex); }
            }
            object rawExtension;
            bool unlimited = values.TryGetValue("max_credit_limit_extension", out rawExtension) &&
                string.Equals(Convert.ToString(rawExtension, CultureInfo.InvariantCulture), "unlimited", StringComparison.OrdinalIgnoreCase);
            long cappedExtension;
            bool hasExtension = unlimited || (rawExtension != null &&
                long.TryParse(Convert.ToString(rawExtension, CultureInfo.InvariantCulture), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out cappedExtension) && cappedExtension > 0);
            object rawPermission;
            bool entitled = values.TryGetValue("can_extend_character_limit", out rawPermission) && rawPermission is bool && (bool)rawPermission;
            decimal? overage = null;
            string currency = null;
            object rawOverage;
            var overageData = values.TryGetValue("current_overage", out rawOverage)
                ? rawOverage as Dictionary<string, object> : null;
            if (overageData != null)
            {
                object rawAmount;
                decimal amount;
                if (overageData.TryGetValue("amount", out rawAmount) && rawAmount != null &&
                    decimal.TryParse(Convert.ToString(rawAmount, CultureInfo.InvariantCulture), NumberStyles.Number,
                        CultureInfo.InvariantCulture, out amount) && amount >= 0)
                    overage = amount;
                object rawCurrency;
                if (overageData.TryGetValue("currency", out rawCurrency) && rawCurrency is string)
                    currency = ((string)rawCurrency).ToUpperInvariant();
            }
            return new SubscriptionBalance { Used = used, Limit = limit, NextReset = reset,
                UsageBasedBillingEnabled = entitled && hasExtension, UnlimitedExtension = unlimited,
                CurrentOverage = overage, OverageCurrency = currency };
        }

        public string Format(DateTimeOffset now)
        {
            var lines = "Included allowance remaining: " + Remaining.ToString("N0", CultureInfo.CurrentCulture) +
                " of " + Limit.ToString("N0", CultureInfo.CurrentCulture) + " credits." + Environment.NewLine +
                "Used this period: " + Used.ToString("N0", CultureInfo.CurrentCulture) + " credits.";
            if (Used > Limit)
            {
                lines += Environment.NewLine + "Included allowance exceeded by " +
                    (Used - Limit).ToString("N0", CultureInfo.CurrentCulture) + " credits.";
                if (UsageBasedBillingEnabled)
                    lines += Environment.NewLine + "Usage-based billing is enabled" +
                        (UnlimitedExtension ? "; no overage credit cap is reported by the API." : ".");
                if (CurrentOverage.HasValue && !string.IsNullOrEmpty(OverageCurrency))
                    lines += Environment.NewLine + "Current overage charge: " + OverageCurrency + " " +
                        CurrentOverage.Value.ToString("N2", CultureInfo.CurrentCulture) + ".";
                lines += Environment.NewLine + "Your total spendable balance is not available from this check. Further generation may incur charges.";
            }
            if (!NextReset.HasValue) return lines + Environment.NewLine + "Next reset: unavailable.";
            var reset = NextReset.Value.ToLocalTime();
            lines += Environment.NewLine + "Next reset: " + reset.ToString("d MMM yyyy, HH:mm zzz", CultureInfo.CurrentCulture) + " (local time).";
            var remaining = NextReset.Value - now;
            if (remaining > TimeSpan.Zero)
            {
                var minutes = (long)Math.Ceiling(remaining.TotalMinutes);
                var days = minutes / 1440;
                var hours = minutes % 1440 / 60;
                var parts = new List<string>();
                if (days > 0) parts.Add(days + (days == 1 ? " day" : " days"));
                if (hours > 0) parts.Add(hours + (hours == 1 ? " hour" : " hours"));
                if (minutes % 60 > 0) parts.Add(minutes % 60 + (minutes % 60 == 1 ? " minute" : " minutes"));
                lines += " Approximately " + string.Join(", ", parts.ToArray()) + " remaining.";
            }
            return lines;
        }

        private static long RequiredCount(Dictionary<string, object> values, string name)
        {
            object value;
            if (!values.TryGetValue(name, out value) || value == null) throw new InvalidDataException("The subscription response did not include " + name + ".");
            long count;
            try { count = Convert.ToInt64(value, CultureInfo.InvariantCulture); }
            catch (Exception ex) { throw new InvalidDataException("The subscription response has an invalid " + name + ".", ex); }
            if (count < 0) throw new InvalidDataException("The subscription response has a negative " + name + ".");
            return count;
        }
    }
}
