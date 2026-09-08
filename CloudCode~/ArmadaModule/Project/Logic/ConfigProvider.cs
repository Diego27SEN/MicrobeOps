#nullable enable

using System;
using Armada.Core;

namespace Armada.CloudCode
{
    /// <summary>
    /// The single read point for game configuration.
    /// <para>
    /// Remote Config is read here and nowhere else, and the client never reads it at all - it asks
    /// <c>GetGameConfig</c>. That is what keeps one source of truth for every balance number
    /// instead of a client copy that drifts.
    /// </para>
    /// <para>
    /// Two defences, both inherited from a service that went down at the wrong moment before.
    /// A short TTL cache keeps a burst of matches from turning into a burst of Remote Config
    /// reads, and an embedded seed means a failed read degrades to the design doc's own numbers
    /// rather than to an exception. A config service being unavailable must not stop people
    /// playing.
    /// </para>
    /// </summary>
    public sealed class ConfigProvider
    {
        /// <summary>Design doc section 10.1: two minutes, chosen so a LiveOps change lands quickly enough to matter.</summary>
        public static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(2);

        private GameConfig? _cached;
        private long _cachedAtUnixMs;

        /// <summary>True when the last read came from the embedded seed instead of Remote Config.</summary>
        public bool UsingEmbeddedFallback { get; private set; }

        /// <summary>
        /// Returns the configuration, from cache when it is still fresh.
        /// <para>
        /// The Remote Config call itself is not wired yet - that lands with the service account in
        /// M3 - so today every read is the embedded seed. The seam is here so the endpoints and
        /// their tests are written against the final shape rather than being rewritten later.
        /// </para>
        /// </summary>
        public GameConfig Get(long nowUnixMs)
        {
            if (_cached != null && nowUnixMs - _cachedAtUnixMs < CacheTtl.TotalMilliseconds)
            {
                return _cached;
            }

            _cached = LoadFromSeed();
            _cachedAtUnixMs = nowUnixMs;
            return _cached;
        }

        /// <summary>Drops the cache. Used by tests and by a future LiveOps invalidation hook.</summary>
        public void Invalidate()
        {
            _cached = null;
            _cachedAtUnixMs = 0;
        }

        private GameConfig LoadFromSeed()
        {
            UsingEmbeddedFallback = true;
            return DefaultGameConfig.Create();
        }
    }
}
