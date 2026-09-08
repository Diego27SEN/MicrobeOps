#nullable enable

namespace Armada.Core
{
    /// <summary>
    /// Wire contract version, mirrored byte-for-byte between the Unity client and the Cloud Code
    /// module because both compile this very file (see the shared &lt;Compile Include&gt; in the
    /// module .csproj).
    /// <para>
    /// Bump ONLY on breaking contract changes, and always in the same commit on both sides.
    /// Do not confuse this with <c>bundleVersion</c>, which is the player-visible version and
    /// rises with every change that reaches players.
    /// </para>
    /// </summary>
    public static class GameProtocol
    {
        /// <summary>Current protocol version. Frozen at M0 together with the API contract.</summary>
        public const int Version = 1;

        /// <summary>
        /// Oldest protocol version this build still accepts from a peer. Backwards compatibility
        /// is mandatory for at least two protocol versions (rollback requirement).
        /// </summary>
        public const int MinSupportedVersion = 1;

        public static bool IsCompatible(int otherVersion)
        {
            return otherVersion >= MinSupportedVersion && otherVersion <= Version;
        }
    }
}
