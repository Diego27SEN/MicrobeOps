#nullable enable

using System;
using System.Collections.Generic;

namespace Armada.Core
{
    /// <summary>
    /// Fleet placement: validation and generation. Design doc sections 4.3 and 4.7.
    /// <para>
    /// <see cref="GenerateRandom"/> is deliberately the ONE implementation behind the player's
    /// "Random" button, the AI's own deployment and the server's auto-deploy on timeout.
    /// One implementation, one set of tests.
    /// </para>
    /// <para>
    /// <b>Fleet order is part of the contract.</b> <see cref="ShipPlacement"/> carries a class but
    /// no length, and on <c>admiral</c> two ships share the Battleship class with different lengths
    /// (5 and 4). So placement <c>i</c> always corresponds to <c>cfg.Fleet[i]</c>, and its length
    /// comes from there. Submitting the fleet out of order is a validation error, not a
    /// reinterpretation.
    /// </para>
    /// </summary>
    public static class PlacementRules
    {
        /// <summary>A ship at least this long counts as "large" for the human-like heuristic.</summary>
        private const int LargeShipLength = 4;

        /// <summary>Overlap, in cells, at which two parallel adjacent large ships are rejected.</summary>
        private const int ParallelOverlapThreshold = 3;

        private const int MaxLayoutAttempts = 64;
        private const int MaxAttemptsPerShip = 128;

        public static PlacementValidation Validate(BoardConfig cfg, RuleFlags flags, IReadOnlyList<ShipPlacement> placements)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            if (flags == null) throw new ArgumentNullException(nameof(flags));
            if (placements == null) throw new ArgumentNullException(nameof(placements));

            if (placements.Count != cfg.Fleet.Count)
            {
                return PlacementValidation.Invalid(
                    ErrorCodes.Format(ErrorCodes.FleetMismatch, cfg.Fleet.Count, placements.Count));
            }

            BitBoard occupied = BitBoard.Empty;

            for (int i = 0; i < placements.Count; i++)
            {
                ShipPlacement placement = placements[i];
                ShipSpec spec = cfg.Fleet[i];

                if (placement.Class != spec.Class)
                {
                    return PlacementValidation.Invalid(
                        ErrorCodes.Format(ErrorCodes.FleetMismatch, spec.Class, placement.Class));
                }

                // Bounds are computed in int, not byte, so a bow near the edge of the byte range
                // cannot wrap around into a false "fits".
                int lastX = placement.Orientation == Orientation.Horizontal ? placement.Bow.X + spec.Length - 1 : placement.Bow.X;
                int lastY = placement.Orientation == Orientation.Vertical ? placement.Bow.Y + spec.Length - 1 : placement.Bow.Y;

                if (placement.Bow.X >= cfg.Width || placement.Bow.Y >= cfg.Height || lastX >= cfg.Width || lastY >= cfg.Height)
                {
                    return PlacementValidation.Invalid(
                        ErrorCodes.Format(ErrorCodes.InvalidPlacement, ErrorCodes.PlacementReason.OutOfBounds));
                }

                BitBoard cells = CellsBitBoard(cfg, placement, spec.Length);

                if (occupied.Intersects(cells))
                {
                    return PlacementValidation.Invalid(
                        ErrorCodes.Format(ErrorCodes.InvalidPlacement, ErrorCodes.PlacementReason.Overlap));
                }

                if (!flags.AllowAdjacentShips && occupied.Intersects(Halo(cfg, cells)))
                {
                    return PlacementValidation.Invalid(
                        ErrorCodes.Format(ErrorCodes.InvalidPlacement, ErrorCodes.PlacementReason.Adjacency));
                }

                occupied = occupied.Or(cells);
            }

            return PlacementValidation.Valid();
        }

        /// <summary>Cells covered by a placement, bow to stern. Throws when the ship runs off-board.</summary>
        public static IReadOnlyList<Coord> CellsOf(BoardConfig cfg, ShipPlacement placement, int length)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));

            Coord[] cells = new Coord[length];
            for (int i = 0; i < length; i++)
            {
                int x = placement.Orientation == Orientation.Horizontal ? placement.Bow.X + i : placement.Bow.X;
                int y = placement.Orientation == Orientation.Vertical ? placement.Bow.Y + i : placement.Bow.Y;

                if (x >= cfg.Width || y >= cfg.Height)
                {
                    throw new ArgumentOutOfRangeException(nameof(placement), "Placement runs off the board.");
                }

                cells[i] = new Coord((byte)x, (byte)y);
            }

            return cells;
        }

        /// <summary>Length of the <paramref name="index"/>-th ship of the fleet.</summary>
        public static int LengthOf(BoardConfig cfg, int index)
        {
            return cfg.Fleet[index].Length;
        }

        /// <summary>Occupancy bitboard for a whole fleet. Assumes the placement is already valid.</summary>
        public static BitBoard ToOccupancy(BoardConfig cfg, IReadOnlyList<ShipPlacement> placements)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            if (placements == null) throw new ArgumentNullException(nameof(placements));

            BitBoard occupancy = BitBoard.Empty;
            for (int i = 0; i < placements.Count; i++)
            {
                occupancy = occupancy.Or(CellsBitBoard(cfg, placements[i], cfg.Fleet[i].Length));
            }
            return occupancy;
        }

        /// <summary>
        /// A random valid fleet layout, deterministic for a given <paramref name="rng"/> seed.
        /// Ships are placed in fleet order, which is longest first, because the long ones are the
        /// hardest to fit and failing early costs less.
        /// <para>
        /// Termination is guaranteed: after <see cref="MaxLayoutAttempts"/> randomized attempts it
        /// falls back to a deterministic scan that always succeeds on the configured densities.
        /// An unbounded retry loop inside server code would be a denial-of-service waiting to
        /// happen.
        /// </para>
        /// </summary>
        public static IReadOnlyList<ShipPlacement> GenerateRandom(BoardConfig cfg, RuleFlags flags, IRandom rng)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            if (flags == null) throw new ArgumentNullException(nameof(flags));
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            for (int attempt = 0; attempt < MaxLayoutAttempts; attempt++)
            {
                IReadOnlyList<ShipPlacement>? layout = TryGenerateRandomLayout(cfg, flags, rng);
                if (layout != null) return layout;
            }

            return GenerateDeterministicLayout(cfg, flags);
        }

        /// <summary>
        /// Heuristic from design doc section 4.6: rejects layouts with more than
        /// <c>MaxBorderCellRatio</c> of ship cells on the border, or with two large ships lying
        /// parallel and adjacent. Those are the patterns human players learn to exploit, so the
        /// AI must not fall into them.
        /// </summary>
        public static bool IsHumanLike(BoardConfig cfg, AiParams aiParams, IReadOnlyList<ShipPlacement> placements)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            if (aiParams == null) throw new ArgumentNullException(nameof(aiParams));
            if (placements == null) throw new ArgumentNullException(nameof(placements));

            int borderCells = 0;
            int totalCells = 0;

            for (int i = 0; i < placements.Count; i++)
            {
                int length = cfg.Fleet[i].Length;
                IReadOnlyList<Coord> cells = CellsOf(cfg, placements[i], length);

                for (int c = 0; c < cells.Count; c++)
                {
                    totalCells++;
                    Coord cell = cells[c];
                    bool onBorder = cell.X == 0 || cell.Y == 0 || cell.X == cfg.Width - 1 || cell.Y == cfg.Height - 1;
                    if (onBorder) borderCells++;
                }
            }

            if (totalCells > 0 && (double)borderCells / totalCells > aiParams.MaxBorderCellRatio) return false;

            for (int i = 0; i < placements.Count; i++)
            {
                if (cfg.Fleet[i].Length < LargeShipLength) continue;

                for (int j = i + 1; j < placements.Count; j++)
                {
                    if (cfg.Fleet[j].Length < LargeShipLength) continue;
                    if (AreParallelAndAdjacent(placements[i], cfg.Fleet[i].Length, placements[j], cfg.Fleet[j].Length)) return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Random layout that also satisfies <see cref="IsHumanLike"/>, retrying up to
        /// <c>MaxPlacementRetries</c> times and then accepting the last attempt, exactly as the
        /// design doc specifies. Used for AI deployment; plain <see cref="GenerateRandom"/> is used
        /// for the player's Random button and for auto-deploy on timeout.
        /// </summary>
        public static IReadOnlyList<ShipPlacement> GenerateHumanLike(BoardConfig cfg, RuleFlags flags, AiParams aiParams, IRandom rng)
        {
            if (aiParams == null) throw new ArgumentNullException(nameof(aiParams));

            IReadOnlyList<ShipPlacement> layout = GenerateRandom(cfg, flags, rng);

            for (int retry = 0; retry < aiParams.MaxPlacementRetries; retry++)
            {
                if (IsHumanLike(cfg, aiParams, layout)) return layout;
                layout = GenerateRandom(cfg, flags, rng);
            }

            return layout;
        }

        private static IReadOnlyList<ShipPlacement>? TryGenerateRandomLayout(BoardConfig cfg, RuleFlags flags, IRandom rng)
        {
            List<ShipPlacement> layout = new List<ShipPlacement>(cfg.Fleet.Count);
            BitBoard occupied = BitBoard.Empty;

            for (int i = 0; i < cfg.Fleet.Count; i++)
            {
                int length = cfg.Fleet[i].Length;
                bool placed = false;

                for (int attempt = 0; attempt < MaxAttemptsPerShip && !placed; attempt++)
                {
                    Orientation orientation = rng.Next(2) == 0 ? Orientation.Horizontal : Orientation.Vertical;

                    int spanX = orientation == Orientation.Horizontal ? length : 1;
                    int spanY = orientation == Orientation.Vertical ? length : 1;
                    if (spanX > cfg.Width || spanY > cfg.Height) continue;

                    int x = rng.Next(cfg.Width - spanX + 1);
                    int y = rng.Next(cfg.Height - spanY + 1);

                    ShipPlacement candidate = new ShipPlacement(cfg.Fleet[i].Class, new Coord((byte)x, (byte)y), orientation);
                    BitBoard cells = CellsBitBoard(cfg, candidate, length);

                    if (occupied.Intersects(cells)) continue;
                    if (!flags.AllowAdjacentShips && occupied.Intersects(Halo(cfg, cells))) continue;

                    layout.Add(candidate);
                    occupied = occupied.Or(cells);
                    placed = true;
                }

                if (!placed) return null;
            }

            return layout;
        }

        private static IReadOnlyList<ShipPlacement> GenerateDeterministicLayout(BoardConfig cfg, RuleFlags flags)
        {
            List<ShipPlacement> layout = new List<ShipPlacement>(cfg.Fleet.Count);
            BitBoard occupied = BitBoard.Empty;

            for (int i = 0; i < cfg.Fleet.Count; i++)
            {
                int length = cfg.Fleet[i].Length;
                bool placed = false;

                for (int y = 0; y < cfg.Height && !placed; y++)
                {
                    for (int x = 0; x < cfg.Width && !placed; x++)
                    {
                        for (int o = 0; o < 2 && !placed; o++)
                        {
                            Orientation orientation = o == 0 ? Orientation.Horizontal : Orientation.Vertical;

                            int lastX = orientation == Orientation.Horizontal ? x + length - 1 : x;
                            int lastY = orientation == Orientation.Vertical ? y + length - 1 : y;
                            if (lastX >= cfg.Width || lastY >= cfg.Height) continue;

                            ShipPlacement candidate = new ShipPlacement(cfg.Fleet[i].Class, new Coord((byte)x, (byte)y), orientation);
                            BitBoard cells = CellsBitBoard(cfg, candidate, length);

                            if (occupied.Intersects(cells)) continue;
                            if (!flags.AllowAdjacentShips && occupied.Intersects(Halo(cfg, cells))) continue;

                            layout.Add(candidate);
                            occupied = occupied.Or(cells);
                            placed = true;
                        }
                    }
                }

                if (!placed)
                {
                    throw new InvalidOperationException(
                        "Board '" + cfg.Id + "' cannot fit its own fleet. That is a configuration error, not a runtime one.");
                }
            }

            return layout;
        }

        private static BitBoard CellsBitBoard(BoardConfig cfg, ShipPlacement placement, int length)
        {
            BitBoard cells = BitBoard.Empty;
            for (int i = 0; i < length; i++)
            {
                int x = placement.Orientation == Orientation.Horizontal ? placement.Bow.X + i : placement.Bow.X;
                int y = placement.Orientation == Orientation.Vertical ? placement.Bow.Y + i : placement.Bow.Y;
                cells = cells.Set((y * cfg.Width) + x);
            }
            return cells;
        }

        /// <summary>The eight-neighbourhood of a set of cells, used for the no-touching variant.</summary>
        private static BitBoard Halo(BoardConfig cfg, BitBoard cells)
        {
            BitBoard halo = BitBoard.Empty;
            IReadOnlyList<int> indices = cells.ToIndices();

            for (int i = 0; i < indices.Count; i++)
            {
                int x = indices[i] % cfg.Width;
                int y = indices[i] / cfg.Width;

                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx;
                        int ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= cfg.Width || ny >= cfg.Height) continue;
                        halo = halo.Set((ny * cfg.Width) + nx);
                    }
                }
            }

            return halo;
        }

        /// <summary>
        /// Two ships lie parallel and adjacent when they share an orientation, sit exactly one row
        /// or column apart, and overlap along their axis by at least
        /// <see cref="ParallelOverlapThreshold"/> cells. A one-cell overlap is a corner brush, not
        /// the "wall of ships" pattern players learn to sweep.
        /// </summary>
        private static bool AreParallelAndAdjacent(ShipPlacement a, int lengthA, ShipPlacement b, int lengthB)
        {
            if (a.Orientation != b.Orientation) return false;

            if (a.Orientation == Orientation.Horizontal)
            {
                if (Math.Abs(a.Bow.Y - b.Bow.Y) != 1) return false;
                return AxisOverlap(a.Bow.X, lengthA, b.Bow.X, lengthB) >= ParallelOverlapThreshold;
            }

            if (Math.Abs(a.Bow.X - b.Bow.X) != 1) return false;
            return AxisOverlap(a.Bow.Y, lengthA, b.Bow.Y, lengthB) >= ParallelOverlapThreshold;
        }

        private static int AxisOverlap(int startA, int lengthA, int startB, int lengthB)
        {
            int from = Math.Max(startA, startB);
            int to = Math.Min(startA + lengthA - 1, startB + lengthB - 1);
            return to < from ? 0 : to - from + 1;
        }
    }
}
