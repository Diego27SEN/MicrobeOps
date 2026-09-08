#nullable enable

using System;

namespace Armada.Core
{
    /// <summary>One ship as tracked by the server: where it sits and how battered it is.</summary>
    public struct ShipRecord
    {
        public ShipClass Class;
        public byte Length;
        public Coord Bow;
        public Orientation Orientation;
        public byte HitCount;

        /// <summary>Index of the shot that sank it, or -1 while it is still afloat.</summary>
        public int SunkOnShotIndex;

        public bool IsSunk
        {
            get { return HitCount >= Length; }
        }

        public Coord CellAt(int offset)
        {
            return Orientation == Orientation.Horizontal
                ? new Coord((byte)(Bow.X + offset), Bow.Y)
                : new Coord(Bow.X, (byte)(Bow.Y + offset));
        }

        public ShipPlacement ToPlacement()
        {
            return new ShipPlacement(Class, Bow, Orientation);
        }
    }

    /// <summary>
    /// One player's half of the match, as held on the server.
    /// <para>
    /// <see cref="Occupancy"/> is THE secret of this game. It must never reach the opponent's
    /// projected view in any form, not even as a derived count. See
    /// <see cref="MatchProjection"/> and design doc section 11.1.
    /// </para>
    /// </summary>
    public struct PlayerBoard
    {
        /// <summary>Where this player's ships are. SECRET until the match is finished.</summary>
        public BitBoard Occupancy;

        /// <summary>Own cells that have been hit.</summary>
        public BitBoard Hits;

        /// <summary>
        /// The subset of <see cref="Hits"/> the shooter has actually been told about.
        /// <para>
        /// Under normal rules this equals <see cref="Hits"/>: you fire, you are told hit or miss.
        /// Under Salvo with <c>salvoAggregateReport</c> the shooter only learns "two hits, three
        /// misses" without which was which, so a hit stays undisclosed until the ship sinks and
        /// <c>revealSunkCells</c> uncovers its cells. Projection reads THIS, never
        /// <see cref="Hits"/>: using the wrong one would hand Salvo players information the rules
        /// deny them.
        /// </para>
        /// </summary>
        public BitBoard DisclosedHits;

        /// <summary>Every cell the opponent has fired at, hit or miss. The shooter always knows this.</summary>
        public BitBoard IncomingShots;

        public ShipRecord[] Fleet;

        /// <summary>True once a valid placement has been submitted for this player.</summary>
        public bool HasPlacement;

        /// <summary>Shots this player has fired. Used by the anti-farm gate.</summary>
        public int ShotsFired;

        public static PlayerBoard CreateEmpty()
        {
            return new PlayerBoard
            {
                Occupancy = BitBoard.Empty,
                Hits = BitBoard.Empty,
                DisclosedHits = BitBoard.Empty,
                IncomingShots = BitBoard.Empty,
                Fleet = Array.Empty<ShipRecord>(),
                HasPlacement = false,
                ShotsFired = 0
            };
        }

        public int ShipsAfloat
        {
            get
            {
                int afloat = 0;
                for (int i = 0; i < Fleet.Length; i++)
                {
                    if (!Fleet[i].IsSunk) afloat++;
                }
                return afloat;
            }
        }
    }
}
