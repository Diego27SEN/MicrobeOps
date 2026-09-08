#nullable enable

using System;

namespace Armada.CloudCode
{
    /// <summary>
    /// Every endpoint returns one of these. Failure carries an <c>ERR_CODE|p1|p2</c> string and
    /// never prose: the client resolves it against Localization, logs stay greppable, and tests
    /// assert on a stable token instead of on a sentence somebody might reword.
    /// </summary>
    public sealed class Result<T>
    {
        public bool Success { get; set; }
        public T? Data { get; set; }
        public string? Error { get; set; }

        public static Result<T> Ok(T data)
        {
            return new Result<T> { Success = true, Data = data };
        }

        public static Result<T> Fail(string error)
        {
            if (string.IsNullOrEmpty(error)) throw new ArgumentException("Error code is required.", nameof(error));
            return new Result<T> { Success = false, Error = error };
        }
    }

    /// <summary>
    /// Boot handshake payload. The client compares protocol versions before doing anything else,
    /// so an incompatible build is turned away at the door rather than halfway through a match.
    /// </summary>
    public sealed class ServerInfo
    {
        public int ProtocolVersion { get; set; }
        public long ServerTimeUnixMs { get; set; }
        public int MinClientProtocolVersion { get; set; }
        public KillSwitchesDto KillSwitches { get; set; } = new KillSwitchesDto();
        public string Environment { get; set; } = string.Empty;
    }

    /// <summary>
    /// The kill switches, flattened for the wire. Kept as its own DTO rather than reusing Core's
    /// type so that adding a server-only flag never silently changes what Core compiles.
    /// </summary>
    public sealed class KillSwitchesDto
    {
        public bool OnlineEnabled { get; set; } = true;
        public bool RankedEnabled { get; set; } = true;
        public bool AsyncEnabled { get; set; } = true;
        public bool IapEnabled { get; set; } = true;
        public bool AdsEnabled { get; set; } = true;
        public bool BotFillEnabled { get; set; } = true;
        public bool PushEnabled { get; set; } = true;
        public bool RematchEnabled { get; set; } = true;
        public bool EmotesEnabled { get; set; } = true;
        public string MaintenanceMessageKey { get; set; } = string.Empty;
    }
}
