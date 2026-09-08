#nullable enable

using System;
using System.Collections.Generic;

namespace Armada.Core
{
    /// <summary>An opponent ship the AI has already sunk, as far as the rules let it know.</summary>
    public readonly struct SunkShipKnowledge
    {
        public readonly ShipClass? Class;
        public readonly int Length;

        public SunkShipKnowledge(ShipClass? shipClass, int length)
        {
            Class = shipClass;
            Length = length;
        }
    }

    /// <summary>
    /// Everything the AI is allowed to know. Deliberately built from the same projected
    /// information a human player would have: the AI never sees the player's board. "Hard" is hard
    /// because it counts possible placements, not because it cheats (design doc section 4.6).
    /// </summary>
    public sealed class AiMemory
    {
        /// <summary>Cells the AI has fired at.</summary>
        public BitBoard Shots { get; set; }

        /// <summary>Of those, the ones the rules have told it were hits.</summary>
        public BitBoard Hits { get; set; }

        /// <summary>Cells belonging to ships already sunk, when the rules reveal them.</summary>
        public BitBoard SunkCells { get; set; }

        public IReadOnlyList<SunkShipKnowledge> SunkShips { get; set; } = Array.Empty<SunkShipKnowledge>();

        /// <summary>Lengths of the enemy ships still afloat.</summary>
        public IReadOnlyList<int> RemainingShipLengths { get; set; } = Array.Empty<int>();

        /// <summary>
        /// Adaptive only: shooting accuracy over the last evaluation window, for the AI and for the
        /// human. Null when the caller has no window yet. Everything else here is board knowledge;
        /// these two are the feedback signal that keeps an Adaptive match close.
        /// </summary>
        public double? OwnAccuracyLastWindow { get; set; }

        public double? OpponentAccuracyLastWindow { get; set; }

        /// <summary>Hits that do not yet belong to a ship known to be sunk. The follow-up targets.</summary>
        public BitBoard UnresolvedHits
        {
            get { return Hits.AndNot(SunkCells); }
        }

        /// <summary>Cells a surviving ship cannot occupy: known misses and cells of sunk ships.</summary>
        public BitBoard Blocked
        {
            get { return Shots.AndNot(Hits).Or(SunkCells); }
        }

        public int UntriedCellCount(BoardConfig cfg)
        {
            int untried = 0;
            for (int i = 0; i < cfg.CellCount; i++)
            {
                if (!Shots.Get(i)) untried++;
            }
            return untried;
        }

        /// <summary>
        /// Builds the AI's knowledge from a target board using ONLY what the shooter has been told:
        /// where it fired, which of those the rules disclosed as hits, and which ships sank.
        /// <para>
        /// This is the seam that makes "the AI does not cheat" auditable rather than a promise. It
        /// reads <see cref="PlayerBoard.DisclosedHits"/>, never <see cref="PlayerBoard.Occupancy"/>
        /// or <see cref="PlayerBoard.Hits"/>, and a test asserts that two boards differing only in
        /// hidden ship positions produce identical memories.
        /// </para>
        /// </summary>
        public static AiMemory FromDisclosedKnowledge(in PlayerBoard target, BoardConfig cfg, RuleFlags flags)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            if (flags == null) throw new ArgumentNullException(nameof(flags));

            BitBoard sunkCells = BitBoard.Empty;
            List<SunkShipKnowledge> sunkShips = new List<SunkShipKnowledge>();
            List<int> remaining = new List<int>();

            for (int i = 0; i < target.Fleet.Length; i++)
            {
                ShipRecord ship = target.Fleet[i];

                if (!ship.IsSunk)
                {
                    remaining.Add(ship.Length);
                    continue;
                }

                sunkShips.Add(new SunkShipKnowledge(flags.AnnounceSunkShipType ? ship.Class : (ShipClass?)null, ship.Length));

                if (!flags.RevealSunkCells) continue;

                for (int c = 0; c < ship.Length; c++)
                {
                    sunkCells = sunkCells.Set(ship.CellAt(c).ToIndex(cfg.Width));
                }
            }

            return new AiMemory
            {
                Shots = target.IncomingShots,
                Hits = target.DisclosedHits,
                SunkCells = sunkCells,
                SunkShips = sunkShips,
                RemainingShipLengths = remaining
            };
        }
    }

    /// <summary>
    /// A shooting strategy. Deterministic: with the same memory, config and RNG seed it must
    /// produce the same sequence of shots. That is a tested invariant, not an aspiration.
    /// </summary>
    public interface IAiStrategy
    {
        AiDifficulty Difficulty { get; }

        Coord NextShot(in AiMemory memory, BoardConfig cfg, IRandom rng);

        void Observe(Coord cell, ShotOutcome outcome, ShipClass? sunk);
    }

    public static class AiFactory
    {
        public static IAiStrategy Create(AiDifficulty difficulty, AiParams aiParams)
        {
            if (aiParams == null) throw new ArgumentNullException(nameof(aiParams));

            AiDifficultyParams parameters = aiParams.For(difficulty);

            switch (difficulty)
            {
                case AiDifficulty.Easy:
                    return new EasyAiStrategy(parameters);
                case AiDifficulty.Medium:
                    return new MediumAiStrategy();
                case AiDifficulty.Hard:
                    return new DensityAiStrategy(AiDifficulty.Hard, 0, null);
                case AiDifficulty.Adaptive:
                    return new DensityAiStrategy(AiDifficulty.Adaptive, aiParams.Adaptive.NoiseCeiling / 2, aiParams.Adaptive);
                default:
                    throw new ArgumentOutOfRangeException(nameof(difficulty));
            }
        }
    }
}
