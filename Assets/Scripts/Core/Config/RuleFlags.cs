#nullable enable

namespace Armada.Core
{
    public enum ShotsPerTurnMode : byte
    {
        /// <summary>A fixed number of shots per turn, given by <see cref="RuleFlags.FixedShotsPerTurn"/>.</summary>
        Fixed = 0,

        /// <summary>Salvo: as many shots as the shooter still has ships afloat.</summary>
        OneShotPerAfloatShip = 1
    }

    /// <summary>
    /// Configurable rule set. Values from design doc section 4.3, mirrored in Remote Config under
    /// <c>RULE_SETS</c>.
    /// <para>
    /// Invariants that are NOT configurable: ships occupy consecutive cells horizontally or
    /// vertically, never overlapping and never off-board; both players get the exact same fleet
    /// composition; the same cell can never be shot twice.
    /// </para>
    /// </summary>
    public sealed class RuleFlags
    {
        public const string ClassicId = "classic";
        public const string BlitzId = "blitz";
        public const string SalvoId = "salvo";

        public string Id { get; set; } = string.Empty;

        /// <summary>When false, ships may not touch, not even diagonally (the Russian variant).</summary>
        public bool AllowAdjacentShips { get; set; } = true;

        /// <summary>When true, sinking a ship announces which class went down.</summary>
        public bool AnnounceSunkShipType { get; set; } = true;

        /// <summary>When true, the cells of a sunk ship are marked on the tracking grid.</summary>
        public bool RevealSunkCells { get; set; } = true;

        /// <summary>When true, a hit grants another shot. Speeds blitz up considerably.</summary>
        public bool ExtraTurnOnHit { get; set; }

        public ShotsPerTurnMode ShotsPerTurn { get; set; } = ShotsPerTurnMode.Fixed;

        public int FixedShotsPerTurn { get; set; } = 1;

        /// <summary>Salvo only: report "2 hits, 3 misses" without saying which shot was which.</summary>
        public bool SalvoAggregateReport { get; set; }

        /// <summary>Always false. Never configurable to true; kept explicit so the answer is written down.</summary>
        public bool DiagonalPlacement { get; set; }
    }
}
