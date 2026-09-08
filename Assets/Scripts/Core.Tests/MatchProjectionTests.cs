#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using Armada.Core;
using NUnit.Framework;

namespace Armada.Core.Tests
{
    /// <summary>
    /// The five projection invariants of design doc section 6.3, plus the supporting cases.
    /// <para>
    /// If any test in this fixture goes red, the opponent's board is leaking and the online game is
    /// broken. Do not "fix" a failure here by adjusting the assertion.
    /// </para>
    /// </summary>
    [TestFixture]
    public sealed class MatchProjectionTests
    {
        private GameConfig _config = null!;
        private BattleEngine _engine = null!;
        private RuleFlags _rules = null!;

        [SetUp]
        public void SetUp()
        {
            _config = TestGame.Config();
            _engine = TestGame.Engine(config: _config);
            _rules = _engine.Flags;
        }

        // --- invariant 1 ----------------------------------------------------------------------

        [Test]
        public void Projection_ForOpponent_NeverLeaksUnhitShipCells()
        {
            MatchState state = TestGame.StartedMatch(_engine);
            MatchStateView view = MatchProjection.Project(in state, TestGame.PlayerA, _engine.Board, _rules, TestGame.Epoch);

            BitBoard secret = state.BoardB.Occupancy;
            BitBoard known = BitBoard.FromBase64(view.Opponent.HitsBase64);

            // Everything the view says about the enemy board must be a cell that was actually shot.
            BitBoard shotAt = BitBoard.FromBase64(view.Opponent.ShotsBase64);
            Assert.That(shotAt.Contains(known), Is.True);

            // And at the start of a match nothing has been shot, so nothing may be known.
            Assert.That(known.IsEmpty, Is.True);
            Assert.That(secret.PopCount, Is.EqualTo(17), "fixture sanity: B does have a fleet");
        }

        [Test]
        public void Projection_MidMatch_ExposesOnlyCellsTheShooterActuallyFiredAt()
        {
            MatchState state = TestGame.StartedMatch(_engine);
            PlayCoupleOfTurns(ref state);

            MatchStateView view = MatchProjection.Project(in state, TestGame.PlayerA, _engine.Board, _rules, TestGame.Epoch);

            BitBoard shots = BitBoard.FromBase64(view.Opponent.ShotsBase64);
            BitBoard hits = BitBoard.FromBase64(view.Opponent.HitsBase64);

            Assert.That(shots, Is.EqualTo(state.BoardB.IncomingShots));
            Assert.That(shots.Contains(hits), Is.True, "a known hit must always be a cell that was shot");
            Assert.That(state.BoardB.Occupancy.Contains(hits), Is.True, "a known hit must be a real ship cell");

            // The decisive part: unhit ship cells stay unknown.
            BitBoard unhitShipCells = state.BoardB.Occupancy.AndNot(state.BoardB.Hits);
            Assert.That(unhitShipCells.Intersects(shots), Is.False);
        }

        // --- invariant 2 ----------------------------------------------------------------------

        [Test]
        public void Projection_ForOpponent_HasNoOccupancyDerivedData()
        {
            // Walks the whole view object graph and asserts that no field anywhere encodes a secret
            // cell of B, in any representation: base64 bitboards, coordinates, A1 strings or counts.
            MatchState state = TestGame.StartedMatch(_engine);
            PlayCoupleOfTurns(ref state);

            MatchStateView view = MatchProjection.Project(in state, TestGame.PlayerA, _engine.Board, _rules, TestGame.Epoch);

            BitBoard secretCells = state.BoardB.Occupancy.AndNot(state.BoardB.DisclosedHits);
            Assert.That(secretCells.IsEmpty, Is.False, "fixture sanity: B still has hidden cells");

            // `You` is skipped: both boards share one index space, so A's own ships routinely sit on
            // indices that are also B's secrets, and scanning them would only produce noise. A's own
            // half is verified exactly, against A's own board, right below.
            List<string> leaks = new List<string>();
            CollectLeaks(view, "view", secretCells, _engine.Board, leaks, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);

            Assert.That(leaks, Is.Empty, "Secret data reachable from the view:\n" + string.Join("\n", leaks));

            Assert.That(BitBoard.FromBase64(view.You.OccupancyBase64), Is.EqualTo(state.BoardA.Occupancy));
            Assert.That(BitBoard.FromBase64(view.You.HitsBase64), Is.EqualTo(state.BoardA.Hits));
            Assert.That(BitBoard.FromBase64(view.You.IncomingShotsBase64), Is.EqualTo(state.BoardA.IncomingShots));
        }

        [Test]
        public void Projection_ExposesNoAggregateCountOfTheOpponentFleet()
        {
            // A "ships remaining" counter looks harmless and is not: under Salvo, where sinkings are
            // not announced by class, it narrows the search. If a screen ever needs it, it goes
            // through backend-security first.
            MatchState state = TestGame.StartedMatch(_engine);
            MatchStateView view = MatchProjection.Project(in state, TestGame.PlayerA, _engine.Board, _rules, TestGame.Epoch);

            PropertyInfo[] properties = typeof(TrackingBoardView).GetProperties();
            foreach (PropertyInfo property in properties)
            {
                Assert.That(
                    property.PropertyType == typeof(int) || property.PropertyType == typeof(long),
                    Is.False,
                    "TrackingBoardView." + property.Name + " is a bare number; aggregate counts about the opponent are forbidden.");
            }

            Assert.That(view.Opponent.SunkShips, Is.Empty);
        }

        // --- invariant 3 ----------------------------------------------------------------------

        [Test]
        public void Projection_RevealsBothFleets_OnlyWhenFinished()
        {
            MatchState state = TestGame.StartedMatch(_engine);

            Assert.That(MatchProjection.Project(in state, TestGame.PlayerA, _engine.Board, _rules, TestGame.Epoch).Reveal, Is.Null);

            PlayCoupleOfTurns(ref state);
            Assert.That(MatchProjection.Project(in state, TestGame.PlayerA, _engine.Board, _rules, TestGame.Epoch).Reveal, Is.Null);

            string winner = state.CurrentTurn == PlayerSlot.A ? TestGame.PlayerA : TestGame.PlayerB;
            TestGame.PlayToVictory(_engine, ref state, winner);

            MatchStateView finished = MatchProjection.Project(in state, TestGame.PlayerA, _engine.Board, _rules, TestGame.Epoch);

            Assert.That(finished.Phase, Is.EqualTo(MatchPhase.Finished));
            Assert.That(finished.Reveal, Is.Not.Null);
            Assert.That(finished.Reveal!.Yours.Length, Is.EqualTo(5));
            Assert.That(finished.Reveal.Opponent.Length, Is.EqualTo(5));
        }

        // --- invariant 4 ----------------------------------------------------------------------

        [Test]
        public void Projection_DuringPlacement_ExposesOnlyReadyFlag()
        {
            MatchState state = _engine.CreateMatch("m1", TestGame.PlayerA, TestGame.PlayerB, MatchMode.Casual, null, TestGame.Epoch);

            IReadOnlyList<ShipPlacement> layoutB = PlacementRules.GenerateRandom(_engine.Board, _rules, new SeededRandom(3));
            _engine.ApplyPlacement(ref state, TestGame.PlayerB, layoutB, TestGame.Epoch);

            MatchStateView view = MatchProjection.Project(in state, TestGame.PlayerA, _engine.Board, _rules, TestGame.Epoch);

            Assert.That(view.Phase, Is.EqualTo(MatchPhase.Placement));
            Assert.That(view.OpponentReady, Is.True, "the ready flag is the one thing that does cross");

            Assert.That(BitBoard.FromBase64(view.Opponent.ShotsBase64).IsEmpty, Is.True);
            Assert.That(BitBoard.FromBase64(view.Opponent.HitsBase64).IsEmpty, Is.True);
            Assert.That(view.Opponent.SunkShips, Is.Empty);
            Assert.That(view.Reveal, Is.Null);

            List<string> leaks = new List<string>();
            CollectLeaks(view, "view", state.BoardB.Occupancy, _engine.Board, leaks, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
            Assert.That(leaks, Is.Empty, string.Join("\n", leaks));
        }

        [Test]
        public void Projection_DuringPlacement_DoesNotRevealTheOpponentIsStillDeploying_BeyondTheFlag()
        {
            MatchState state = _engine.CreateMatch("m1", TestGame.PlayerA, TestGame.PlayerB, MatchMode.Casual, null, TestGame.Epoch);

            MatchStateView view = MatchProjection.Project(in state, TestGame.PlayerA, _engine.Board, _rules, TestGame.Epoch);

            Assert.That(view.OpponentReady, Is.False);
        }

        // --- redaction by rule flags -----------------------------------------------------------

        [Test]
        public void Projection_WhenAnnounceSunkShipTypeIsOff_OmitsTheSunkClass()
        {
            BattleEngine salvoEngine = TestGame.Engine(BoardConfig.ClassicId, RuleFlags.SalvoId, config: _config);
            MatchState state = SinkOneOpponentShip(salvoEngine, out string shooter);

            MatchStateView view = MatchProjection.Project(in state, shooter, salvoEngine.Board, salvoEngine.Flags, TestGame.Epoch);

            Assert.That(view.Opponent.SunkShips.Length, Is.GreaterThanOrEqualTo(1));
            foreach (SunkShipView sunk in view.Opponent.SunkShips)
            {
                Assert.That(sunk.Class, Is.Null, "salvo does not announce which class went down");
                Assert.That(sunk.Cells, Is.Not.Null, "but salvo does reveal the cells");
            }
        }

        [Test]
        public void Projection_UnderSalvo_ReportsOnlyDisclosedHits()
        {
            // Under the aggregate report a hit stays unknown until its ship sinks. If projection
            // read PlayerBoard.Hits instead of DisclosedHits, this is the test that would catch it.
            BattleEngine salvoEngine = TestGame.Engine(BoardConfig.ClassicId, RuleFlags.SalvoId, config: _config);
            MatchState state = TestGame.StartedMatch(salvoEngine);

            PlayerSlot shooterSlot = state.CurrentTurn;
            string shooter = shooterSlot == PlayerSlot.A ? TestGame.PlayerA : TestGame.PlayerB;
            PlayerBoard target = shooterSlot == PlayerSlot.A ? state.BoardB : state.BoardA;

            // Hit the bow of the five-cell carrier four times: hits land, nothing sinks.
            List<Coord> carrierCells = new List<Coord>();
            for (int i = 0; i < 4; i++) carrierCells.Add(target.Fleet[0].CellAt(i));

            Coord[] volley = { carrierCells[0], carrierCells[1], carrierCells[2], carrierCells[3], TestGame.EmptyCell(in target, salvoEngine.Board) };
            ApplyResult result = salvoEngine.ApplyShots(ref state, shooter, volley, state.Sequence, TestGame.Epoch + 1000);

            Assert.That(result.Success, Is.True, result.Error);
            Assert.That(result.Volley!.Hits, Is.EqualTo(4));

            MatchStateView view = MatchProjection.Project(in state, shooter, salvoEngine.Board, salvoEngine.Flags, TestGame.Epoch + 1000);
            BitBoard disclosed = BitBoard.FromBase64(view.Opponent.HitsBase64);

            Assert.That(disclosed.IsEmpty, Is.True, "four hits landed, but Salvo does not say which shots they were");
            Assert.That(BitBoard.FromBase64(view.Opponent.ShotsBase64).PopCount, Is.EqualTo(5));
        }

        // --- own board -------------------------------------------------------------------------

        [Test]
        public void Projection_ForYourself_ShowsYourWholeFleet()
        {
            MatchState state = TestGame.StartedMatch(_engine);
            MatchStateView view = MatchProjection.Project(in state, TestGame.PlayerA, _engine.Board, _rules, TestGame.Epoch);

            Assert.That(BitBoard.FromBase64(view.You.OccupancyBase64), Is.EqualTo(state.BoardA.Occupancy));
            Assert.That(view.You.Fleet.Length, Is.EqualTo(5));
            Assert.That(view.You.Fleet[0].Class, Is.EqualTo(ShipClass.Carrier));
            Assert.That(view.You.Fleet[0].Length, Is.EqualTo(5));
        }

        [Test]
        public void Projection_IsSymmetric_EachPlayerSeesTheirOwnSide()
        {
            MatchState state = TestGame.StartedMatch(_engine);

            MatchStateView forA = MatchProjection.Project(in state, TestGame.PlayerA, _engine.Board, _rules, TestGame.Epoch);
            MatchStateView forB = MatchProjection.Project(in state, TestGame.PlayerB, _engine.Board, _rules, TestGame.Epoch);

            Assert.That(BitBoard.FromBase64(forA.You.OccupancyBase64), Is.EqualTo(state.BoardA.Occupancy));
            Assert.That(BitBoard.FromBase64(forB.You.OccupancyBase64), Is.EqualTo(state.BoardB.Occupancy));
            Assert.That(forA.IsYourTurn, Is.Not.EqualTo(forB.IsYourTurn));
            Assert.That(forA.YouAreFirstMover, Is.Not.EqualTo(forB.YouAreFirstMover));
        }

        [Test]
        public void Projection_ForAStranger_Throws()
        {
            MatchState state = TestGame.StartedMatch(_engine);

            Assert.That(
                () => MatchProjection.Project(in state, "mallory", _engine.Board, _rules, TestGame.Epoch),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void Projection_CarriesTheSequenceForIdempotentFollowUps()
        {
            MatchState state = TestGame.StartedMatch(_engine);
            MatchStateView view = MatchProjection.Project(in state, TestGame.PlayerA, _engine.Board, _rules, TestGame.Epoch);

            Assert.That(view.Sequence, Is.EqualTo(state.Sequence));
            Assert.That(view.ProtocolVersion, Is.EqualTo(GameProtocol.Version));
        }

        // --- helpers ---------------------------------------------------------------------------

        private void PlayCoupleOfTurns(ref MatchState state)
        {
            long now = TestGame.Epoch;

            for (int turn = 0; turn < 6; turn++)
            {
                if (state.Phase != MatchPhase.InProgress) break;

                now += 1000;
                PlayerSlot slot = state.CurrentTurn;
                string shooter = slot == PlayerSlot.A ? TestGame.PlayerA : TestGame.PlayerB;
                PlayerBoard target = slot == PlayerSlot.A ? state.BoardB : state.BoardA;

                // Alternate a known ship cell and a known miss so both kinds of knowledge exist.
                Coord cell = turn % 2 == 0
                    ? FirstUntried(TestGame.OccupiedCells(in target, _engine.Board), in target, _engine.Board)
                    : TestGame.EmptyCell(in target, _engine.Board);

                _engine.ApplyShots(ref state, shooter, new[] { cell }, state.Sequence, now);
            }
        }

        private static Coord FirstUntried(List<Coord> candidates, in PlayerBoard board, BoardConfig cfg)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                if (!board.IncomingShots.Get(candidates[i].ToIndex(cfg.Width))) return candidates[i];
            }
            throw new InvalidOperationException("No untried candidate left.");
        }

        private MatchState SinkOneOpponentShip(BattleEngine engine, out string shooter)
        {
            MatchState state = TestGame.StartedMatch(engine);
            PlayerSlot slot = state.CurrentTurn;
            shooter = slot == PlayerSlot.A ? TestGame.PlayerA : TestGame.PlayerB;

            PlayerBoard target = slot == PlayerSlot.A ? state.BoardB : state.BoardA;
            ShipRecord destroyer = target.Fleet[4];

            // Salvo fires one shot per afloat ship; the destroyer is two cells, so pad with water.
            List<Coord> volley = new List<Coord> { destroyer.CellAt(0), destroyer.CellAt(1) };
            BitBoard used = BitBoard.Empty.Set(volley[0].ToIndex(engine.Board.Width)).Set(volley[1].ToIndex(engine.Board.Width));

            for (int i = 0; volley.Count < ShotRules.ShotsForTurn(in target, engine.Flags); i++)
            {
                if (target.Occupancy.Get(i) || used.Get(i)) continue;
                volley.Add(engine.Board.FromIndex(i));
                used = used.Set(i);
            }

            ApplyResult result = engine.ApplyShots(ref state, shooter, volley, state.Sequence, TestGame.Epoch + 1000);
            Assert.That(result.Success, Is.True, result.Error);

            return state;
        }

        /// <summary>
        /// Reflective walk over the view graph looking for anything that encodes a secret cell.
        /// Kept deliberately paranoid: it inspects every field and property, follows collections,
        /// and checks strings both as base64 bitboards and as A1 notation.
        /// </summary>
        private static void CollectLeaks(
            object? node,
            string path,
            BitBoard secretCells,
            BoardConfig cfg,
            List<string> leaks,
            HashSet<object> visited,
            int depth)
        {
            if (node == null || depth > 8) return;

            // The viewer's own half of the board is theirs to see, and it lives in the same index
            // space as the opponent's, so it is verified separately rather than scanned here.
            if (path.StartsWith("view.You", StringComparison.Ordinal)) return;
            if (path.StartsWith("view.Reveal.Yours", StringComparison.Ordinal)) return;

            switch (node)
            {
                case string text:
                    CheckString(text, path, secretCells, cfg, leaks);
                    return;

                case Coord coord:
                    if (secretCells.Get(coord.ToIndex(cfg.Width))) leaks.Add(path + " = " + coord.ToA1());
                    return;

                case BitBoard board:
                    if (board.Intersects(secretCells)) leaks.Add(path + " = bitboard intersecting secret cells");
                    return;
            }

            Type type = node.GetType();
            if (type.IsPrimitive || type.IsEnum || node is decimal) return;

            if (!type.IsValueType && !visited.Add(node)) return;

            if (node is System.Collections.IEnumerable sequence)
            {
                int index = 0;
                foreach (object? item in sequence)
                {
                    CollectLeaks(item, path + "[" + index++ + "]", secretCells, cfg, leaks, visited, depth + 1);
                }
                return;
            }

            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length > 0) continue;
                CollectLeaks(SafeRead(property, node), path + "." + property.Name, secretCells, cfg, leaks, visited, depth + 1);
            }

            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                CollectLeaks(field.GetValue(node), path + "." + field.Name, secretCells, cfg, leaks, visited, depth + 1);
            }
        }

        private static object? SafeRead(PropertyInfo property, object node)
        {
            try
            {
                return property.GetValue(node);
            }
            catch (TargetInvocationException)
            {
                return null;
            }
        }

        private static void CheckString(string text, string path, BitBoard secretCells, BoardConfig cfg, List<string> leaks)
        {
            if (text.Length == 0) return;

            if (Coord.TryParse(text, out Coord asCoord)
                && cfg.Contains(asCoord)
                && secretCells.Get(asCoord.ToIndex(cfg.Width)))
            {
                leaks.Add(path + " = \"" + text + "\" (secret cell in A1 notation)");
                return;
            }

            try
            {
                BitBoard decoded = BitBoard.FromBase64(text);
                if (decoded.Intersects(secretCells)) leaks.Add(path + " = base64 bitboard intersecting secret cells");
            }
            catch (FormatException)
            {
            }
            catch (ArgumentException)
            {
            }
        }
    }

    /// <summary>Reference identity comparer, so the graph walk does not loop on shared instances.</summary>
    internal sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();

        public new bool Equals(object? x, object? y)
        {
            return ReferenceEquals(x, y);
        }

        public int GetHashCode(object obj)
        {
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
