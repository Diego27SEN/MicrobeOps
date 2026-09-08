#nullable enable

using System;
using System.Collections.Generic;

namespace Armada.Core
{
    /// <summary>Shot resolution against a single board. Design doc section 4.5.</summary>
    public static class ShotRules
    {
        /// <summary>
        /// Applies one shot to <paramref name="target"/> and reports what happened. The caller is
        /// responsible for having validated bounds, turn ownership and the "cell not already
        /// targeted" rule before getting here.
        /// <para>
        /// A hit is recorded in <c>Hits</c> always, but only mirrored into <c>DisclosedHits</c>
        /// when the rules tell the shooter about it. Under Salvo's aggregate report it is not,
        /// until the ship sinks and its cells are revealed.
        /// </para>
        /// </summary>
        public static ShotResolution Resolve(ref PlayerBoard target, BoardConfig cfg, RuleFlags flags, Coord cell, int shotIndex)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            if (flags == null) throw new ArgumentNullException(nameof(flags));

            int index = cell.ToIndex(cfg.Width);
            target.IncomingShots = target.IncomingShots.Set(index);

            if (!target.Occupancy.Get(index)) return ShotResolution.Miss();

            target.Hits = target.Hits.Set(index);
            if (!flags.SalvoAggregateReport) target.DisclosedHits = target.DisclosedHits.Set(index);

            int shipIndex = FindShipAt(in target, cfg, cell);
            if (shipIndex < 0)
            {
                throw new InvalidOperationException(
                    "Occupancy and fleet records disagree at " + cell.ToA1() + ". The board was built inconsistently.");
            }

            ShipRecord ship = target.Fleet[shipIndex];
            ship.HitCount = (byte)(ship.HitCount + 1);

            if (!ship.IsSunk)
            {
                target.Fleet[shipIndex] = ship;
                return new ShotResolution(ShotOutcome.Hit, null, -1);
            }

            ship.SunkOnShotIndex = shotIndex;
            target.Fleet[shipIndex] = ship;

            // Sinking is announced even under the aggregate report, so the cells of a sunk ship
            // become known at that moment when the rules reveal them.
            if (flags.RevealSunkCells)
            {
                IReadOnlyList<Coord> cells = CellsOf(in ship);
                for (int i = 0; i < cells.Count; i++)
                {
                    target.DisclosedHits = target.DisclosedHits.Set(cells[i].ToIndex(cfg.Width));
                }
            }

            ShotOutcome outcome = IsFleetDestroyed(in target) ? ShotOutcome.Win : ShotOutcome.Sunk;
            return new ShotResolution(outcome, ship.Class, shipIndex);
        }

        /// <summary>True when every ship on the board is sunk.</summary>
        public static bool IsFleetDestroyed(in PlayerBoard board)
        {
            if (board.Fleet.Length == 0) return false;

            for (int i = 0; i < board.Fleet.Length; i++)
            {
                if (!board.Fleet[i].IsSunk) return false;
            }
            return true;
        }

        /// <summary>
        /// How many shots the shooter must declare this turn. Salvo scales with the shooter's own
        /// surviving fleet, which is why losing ships hurts twice in that variant.
        /// </summary>
        public static int ShotsForTurn(in PlayerBoard shooterBoard, RuleFlags flags)
        {
            if (flags == null) throw new ArgumentNullException(nameof(flags));

            if (flags.ShotsPerTurn == ShotsPerTurnMode.OneShotPerAfloatShip)
            {
                int afloat = shooterBoard.ShipsAfloat;
                return afloat > 0 ? afloat : 1;
            }

            return flags.FixedShotsPerTurn > 0 ? flags.FixedShotsPerTurn : 1;
        }

        /// <summary>
        /// The required shot count, capped by how much water is left to aim at.
        /// <para>
        /// Late in a Salvo game the target board can hold fewer untried cells than the shooter has
        /// ships afloat. Demanding the full volley there would be a rule nobody can satisfy: every
        /// submission would fail validation and the match would sit deadlocked until the abandon
        /// timer put it down. This is the count the engine and the projection both use.
        /// </para>
        /// </summary>
        public static int ShotsForTurn(in PlayerBoard shooterBoard, in PlayerBoard targetBoard, BoardConfig cfg, RuleFlags flags)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));

            int required = ShotsForTurn(in shooterBoard, flags);
            int available = UntriedCellCount(in targetBoard, cfg);

            if (available <= 0) return 1;
            return required <= available ? required : available;
        }

        /// <summary>Cells on the target board that have never been fired at.</summary>
        public static int UntriedCellCount(in PlayerBoard targetBoard, BoardConfig cfg)
        {
            int untried = 0;
            for (int i = 0; i < cfg.CellCount; i++)
            {
                if (!targetBoard.IncomingShots.Get(i)) untried++;
            }
            return untried;
        }

        /// <summary>
        /// Turns a raw resolution into what the shooter is allowed to see, redacting
        /// <c>sunkClass</c> and <c>sunkCells</c> according to the rule flags. Redaction happens
        /// here, once, rather than at each call site.
        /// </summary>
        public static ShotResult ToShooterResult(in PlayerBoard target, BoardConfig cfg, RuleFlags flags, Coord cell, ShotResolution resolution)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            if (flags == null) throw new ArgumentNullException(nameof(flags));

            ShotResult result = new ShotResult
            {
                Cell = cell,
                Outcome = resolution.Outcome
            };

            bool sank = resolution.Outcome == ShotOutcome.Sunk || resolution.Outcome == ShotOutcome.Win;
            if (!sank || resolution.SunkShipIndex < 0) return result;

            if (flags.AnnounceSunkShipType) result.SunkClass = resolution.SunkClass;

            if (flags.RevealSunkCells)
            {
                IReadOnlyList<Coord> cells = CellsOf(in target.Fleet[resolution.SunkShipIndex]);
                Coord[] copy = new Coord[cells.Count];
                for (int i = 0; i < cells.Count; i++) copy[i] = cells[i];
                result.SunkCells = copy;
            }

            return result;
        }

        /// <summary>The cells a ship record occupies, bow to stern.</summary>
        public static IReadOnlyList<Coord> CellsOf(in ShipRecord ship)
        {
            Coord[] cells = new Coord[ship.Length];
            for (int i = 0; i < ship.Length; i++)
            {
                cells[i] = ship.CellAt(i);
            }
            return cells;
        }

        private static int FindShipAt(in PlayerBoard board, BoardConfig cfg, Coord cell)
        {
            for (int i = 0; i < board.Fleet.Length; i++)
            {
                ShipRecord ship = board.Fleet[i];

                if (ship.Orientation == Orientation.Horizontal)
                {
                    if (ship.Bow.Y != cell.Y) continue;
                    if (cell.X < ship.Bow.X || cell.X >= ship.Bow.X + ship.Length) continue;
                    return i;
                }

                if (ship.Bow.X != cell.X) continue;
                if (cell.Y < ship.Bow.Y || cell.Y >= ship.Bow.Y + ship.Length) continue;
                return i;
            }

            return -1;
        }
    }
}
