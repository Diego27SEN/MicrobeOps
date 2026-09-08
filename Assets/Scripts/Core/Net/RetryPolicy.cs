#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Armada.Core
{
    /// <summary>
    /// Decides whether a failed call is worth trying again, and how long to wait.
    /// <para>
    /// This lives in Core rather than in the gateway because it is the part that can be wrong: a
    /// policy that retries a business error hammers the server with a call that will never
    /// succeed, and one that gives up on a dropped packet loses a turn the player already took.
    /// Here it is testable without a network.
    /// </para>
    /// <para>
    /// The line is simple. A transport failure is worth retrying. An <c>ERR_*</c> answer is the
    /// server having decided something, and deciding it twice will not change it - with one
    /// exception, <c>ERR_RATE_LIMITED</c>, which is the server saying "later" rather than "no".
    /// </para>
    /// </summary>
    public sealed class RetryPolicy
    {
        /// <summary>Design doc section 9.2: three attempts, 250 ms then 1 s then 4 s.</summary>
        public static readonly IReadOnlyList<int> DefaultDelaysMs = new[] { 250, 1000, 4000 };

        private readonly IReadOnlyList<int> _delaysMs;

        public RetryPolicy(IReadOnlyList<int>? delaysMs = null)
        {
            _delaysMs = delaysMs ?? DefaultDelaysMs;
            if (_delaysMs.Count == 0) throw new ArgumentException("A retry policy needs at least one delay.", nameof(delaysMs));
        }

        /// <summary>Total attempts, the first one included.</summary>
        public int MaxAttempts
        {
            get { return _delaysMs.Count + 1; }
        }

        /// <summary>
        /// Whether to try again after a transport failure - a timeout, a dropped connection, a 5xx.
        /// </summary>
        public bool ShouldRetryTransport(int attemptsMade)
        {
            return attemptsMade < MaxAttempts;
        }

        /// <summary>
        /// Whether to try again after the server answered with an <c>ERR_*</c> code.
        /// <para>
        /// Almost always no: the server decided. The exception is rate limiting, where it asked to
        /// be called later rather than refusing outright.
        /// </para>
        /// </summary>
        public bool ShouldRetryError(string? formattedError, int attemptsMade)
        {
            if (string.IsNullOrEmpty(formattedError)) return false;
            if (attemptsMade >= MaxAttempts) return false;

            return string.Equals(ErrorCodes.CodeOf(formattedError!), ErrorCodes.RateLimited, StringComparison.Ordinal);
        }

        /// <summary>
        /// How long to wait before attempt number <paramref name="attemptsMade"/>.
        /// <para>
        /// When the server said <c>ERR_RATE_LIMITED|seconds</c>, that wins: it knows when it will
        /// answer and the backoff schedule is only a guess.
        /// </para>
        /// </summary>
        public int DelayMsBefore(int attemptsMade, string? formattedError = null)
        {
            int serverAsked = RetryAfterMsFrom(formattedError);
            if (serverAsked > 0) return serverAsked;

            if (attemptsMade < 1) return 0;

            int index = attemptsMade - 1;
            return index < _delaysMs.Count ? _delaysMs[index] : _delaysMs[_delaysMs.Count - 1];
        }

        /// <summary>Parses the seconds out of <c>ERR_RATE_LIMITED|30</c>, or 0 when there is none.</summary>
        public static int RetryAfterMsFrom(string? formattedError)
        {
            if (string.IsNullOrEmpty(formattedError)) return 0;
            if (!string.Equals(ErrorCodes.CodeOf(formattedError!), ErrorCodes.RateLimited, StringComparison.Ordinal)) return 0;

            string[] parts = formattedError!.Split('|');
            if (parts.Length < 2) return 0;

            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)) return 0;
            if (seconds <= 0) return 0;

            return (int)Math.Round(seconds * 1000.0);
        }

        /// <summary>
        /// True for the errors that mean "this build cannot talk to this server". The client shows
        /// a forced-update or maintenance screen instead of retrying or degrading.
        /// </summary>
        public static bool IsFatalForSession(string? formattedError)
        {
            if (string.IsNullOrEmpty(formattedError)) return false;

            string code = ErrorCodes.CodeOf(formattedError!);
            return code == ErrorCodes.ProtocolMismatch
                || code == ErrorCodes.MinVersion
                || code == ErrorCodes.FeatureDisabled;
        }

        /// <summary>
        /// True when a stale sequence means the client should quietly resync rather than surface an
        /// error. This is the double-click and flaky-retry case, and the player should never see it.
        /// </summary>
        public static bool RequiresSilentResync(string? formattedError)
        {
            return !string.IsNullOrEmpty(formattedError)
                && ErrorCodes.CodeOf(formattedError!) == ErrorCodes.StaleAction;
        }
    }
}
