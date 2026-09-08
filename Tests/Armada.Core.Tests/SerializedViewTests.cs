#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;
using Armada.Core;
using NUnit.Framework;

namespace Armada.Core.Tests
{
    /// <summary>
    /// The fifth and most important projection test of design doc section 6.3: it asserts on the
    /// SERIALIZED JSON, not on the DTO.
    /// <para>
    /// Everything else in <c>MatchProjectionTests</c> checks the object graph, which is exactly
    /// what a well-meaning "just a debug field" change keeps satisfying. This one checks the bytes
    /// that actually leave the server, so a leaked field has nowhere to hide.
    /// </para>
    /// <para>
    /// It lives in the headless test project rather than under <c>Assets/</c> because it uses
    /// <c>System.Text.Json</c>, which is what the Cloud Code module serializes with and which is
    /// not part of the netstandard2.1 surface Core is limited to. CI runs it on every pull request,
    /// which is where it matters.
    /// </para>
    /// </summary>
    [TestFixture]
    public sealed class SerializedViewTests
    {
        /// <summary>
        /// Fields are included on purpose. <see cref="Coord"/> exposes X and Y as public fields, so
        /// without this the serializer would silently emit empty objects - and this test would pass
        /// by writing nothing rather than by hiding nothing. The module must use these same options.
        /// </summary>
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            IncludeFields = true,
            WriteIndented = false
        };

        private GameConfig _config = null!;
        private BattleEngine _engine = null!;

        [SetUp]
        public void SetUp()
        {
            _config = DefaultGameConfig.Create();
            _engine = new BattleEngine(
                _config.Board(BoardConfig.ClassicId),
                _config.RuleSet(RuleFlags.ClassicId),
                _config.Timers.RealTime,
                _config.Ai,
                new SeededRandom(1234));
        }

        [Test]
        public void SerializedView_ContainsNoSecretCoordinates()
        {
            MatchState state = StartedMatch();
            PlaySomeShots(ref state);

            MatchStateView view = MatchProjection.Project(in state, "alice", _engine.Board, _engine.Flags, 1_700_000_000_000L);
            string json = JsonSerializer.Serialize(view, Options);

            BitBoard secret = state.BoardB.Occupancy.AndNot(state.BoardB.DisclosedHits);
            Assert.That(secret.IsEmpty, Is.False, "fixture sanity: B must still have hidden cells");

            List<string> leaks = FindLeaks(json, secret);
            Assert.That(leaks, Is.Empty, "Secret coordinates found in the serialized view:\n" + string.Join("\n", leaks) + "\n\nJSON:\n" + json);
        }

        [Test]
        public void SerializedView_DuringPlacement_ContainsNoSecretCoordinates()
        {
            MatchState state = _engine.CreateMatch("m1", "alice", "bob", MatchMode.Casual, null, 1_700_000_000_000L);
            _engine.ApplyPlacement(ref state, "bob", Layout(3), 1_700_000_000_000L);

            MatchStateView view = MatchProjection.Project(in state, "alice", _engine.Board, _engine.Flags, 1_700_000_000_000L);
            string json = JsonSerializer.Serialize(view, Options);

            List<string> leaks = FindLeaks(json, state.BoardB.Occupancy);
            Assert.That(leaks, Is.Empty, string.Join("\n", leaks) + "\n\nJSON:\n" + json);
        }

        [Test]
        public void SerializedView_OnlyAfterTheMatchEnds_CarriesTheOpponentFleet()
        {
            MatchState state = StartedMatch();
            string winner = state.CurrentTurn == PlayerSlot.A ? "alice" : "bob";
            PlayToVictory(ref state, winner);

            MatchStateView view = MatchProjection.Project(in state, "alice", _engine.Board, _engine.Flags, 1_700_000_000_000L);
            string json = JsonSerializer.Serialize(view, Options);

            Assert.That(view.Phase, Is.EqualTo(MatchPhase.Finished));
            Assert.That(json, Does.Contain("\"Reveal\""));

            // Now the reveal is legitimate, so the same scan must find the fleet. If it did not,
            // the scan itself would be broken and every other assertion here would be worthless.
            List<string> found = FindLeaks(json, state.BoardB.Occupancy);
            Assert.That(found, Is.Not.Empty, "the scanner must be able to see coordinates when they are legitimately present");
        }

        [Test]
        public void SerializedView_IsSmallEnoughForASingleCloudSaveDocument()
        {
            MatchState state = StartedMatch();
            PlaySomeShots(ref state);

            string json = JsonSerializer.Serialize(MatchProjection.Project(in state, "alice", _engine.Board, _engine.Flags, 0), Options);

            // Design doc section 6.3 budgets under 1.5 KB for the full state; the projected view is
            // strictly smaller. This is a guard against someone attaching history to it.
            Assert.That(json.Length, Is.LessThan(4096), "the projected view is getting fat: " + json.Length + " bytes");
        }

        // --- helpers ---------------------------------------------------------------------------

        private IReadOnlyList<ShipPlacement> Layout(ulong seed)
        {
            return PlacementRules.GenerateRandom(_engine.Board, _engine.Flags, new SeededRandom(seed));
        }

        private MatchState StartedMatch()
        {
            MatchState state = _engine.CreateMatch("m1", "alice", "bob", MatchMode.Casual, null, 1_700_000_000_000L);
            _engine.ApplyPlacement(ref state, "alice", Layout(11), 1_700_000_000_000L);
            _engine.ApplyPlacement(ref state, "bob", Layout(12), 1_700_000_000_000L);
            return state;
        }

        private void PlaySomeShots(ref MatchState state)
        {
            long now = 1_700_000_000_000L;

            for (int turn = 0; turn < 6 && state.Phase == MatchPhase.InProgress; turn++)
            {
                now += 1000;
                PlayerSlot slot = state.CurrentTurn;
                string shooter = slot == PlayerSlot.A ? "alice" : "bob";
                PlayerBoard target = slot == PlayerSlot.A ? state.BoardB : state.BoardA;

                Coord cell = FirstUntried(in target, turn % 2 == 0);
                _engine.ApplyShots(ref state, shooter, new[] { cell }, state.Sequence, now);
            }
        }

        private Coord FirstUntried(in PlayerBoard board, bool wantShipCell)
        {
            for (int i = 0; i < _engine.Board.CellCount; i++)
            {
                if (board.IncomingShots.Get(i)) continue;
                if (board.Occupancy.Get(i) != wantShipCell) continue;
                return _engine.Board.FromIndex(i);
            }

            for (int i = 0; i < _engine.Board.CellCount; i++)
            {
                if (!board.IncomingShots.Get(i)) return _engine.Board.FromIndex(i);
            }

            throw new InvalidOperationException("Board exhausted.");
        }

        private void PlayToVictory(ref MatchState state, string winner)
        {
            string loser = winner == "alice" ? "bob" : "alice";
            PlayerSlot loserSlot = state.SlotOf(loser)!.Value;
            PlayerBoard loserBoard = loserSlot == PlayerSlot.A ? state.BoardA : state.BoardB;

            List<Coord> targets = new List<Coord>();
            IReadOnlyList<int> indices = loserBoard.Occupancy.ToIndices();
            for (int i = 0; i < indices.Count; i++) targets.Add(_engine.Board.FromIndex(indices[i]));

            int next = 0;
            long now = 1_700_000_000_000L;
            int guard = 0;

            while (state.Phase != MatchPhase.Finished && guard++ < 500)
            {
                now += 1000;
                bool winnerShoots = state.CurrentTurn == state.SlotOf(winner)!.Value;

                if (winnerShoots)
                {
                    _engine.ApplyShots(ref state, winner, new[] { targets[next++] }, state.Sequence, now);
                }
                else
                {
                    PlayerBoard winnerBoard = state.SlotOf(winner)!.Value == PlayerSlot.A ? state.BoardA : state.BoardB;
                    _engine.ApplyShots(ref state, loser, new[] { FirstUntried(in winnerBoard, false) }, state.Sequence, now);
                }
            }
        }

        /// <summary>
        /// Scans raw JSON for any encoding of a secret cell: base64 bitboards, A1 strings, and
        /// {"X":n,"Y":m} pairs. Deliberately format-agnostic, because the point is to catch a field
        /// nobody thought about.
        /// </summary>
        private List<string> FindLeaks(string json, BitBoard secret)
        {
            List<string> leaks = new List<string>();

            using JsonDocument document = JsonDocument.Parse(json);
            Walk(document.RootElement, "$", secret, leaks);

            // Belt and braces: a straight substring hunt for A1 notation, in case a future field
            // buries a coordinate inside a larger string that the structural walk would not split.
            IReadOnlyList<int> secretIndices = secret.ToIndices();
            for (int i = 0; i < secretIndices.Count; i++)
            {
                string a1 = _engine.Board.FromIndex(secretIndices[i]).ToA1();
                if (json.Contains("\"" + a1 + "\"", StringComparison.Ordinal))
                {
                    leaks.Add("raw JSON contains the A1 string \"" + a1 + "\"");
                }
            }

            return leaks;
        }

        private void Walk(JsonElement element, string path, BitBoard secret, List<string> leaks)
        {
            // The viewer's own board shares the index space with the opponent's, so scanning it
            // would report their own ships as leaks. It is verified against the source board in
            // MatchProjectionTests instead.
            if (path.StartsWith("$.You", StringComparison.Ordinal)) return;
            if (path.StartsWith("$.Reveal.Yours", StringComparison.Ordinal)) return;

            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    if (TryReadCoord(element, out Coord coord))
                    {
                        if (_engine.Board.Contains(coord) && secret.Get(coord.ToIndex(_engine.Board.Width)))
                        {
                            leaks.Add(path + " = " + coord.ToA1() + " (secret cell as an X/Y pair)");
                        }
                    }

                    foreach (JsonProperty property in element.EnumerateObject())
                    {
                        Walk(property.Value, path + "." + property.Name, secret, leaks);
                    }
                    return;

                case JsonValueKind.Array:
                    int index = 0;
                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        Walk(item, path + "[" + index++ + "]", secret, leaks);
                    }
                    return;

                case JsonValueKind.String:
                    CheckString(element.GetString(), path, secret, leaks);
                    return;
            }
        }

        private static bool TryReadCoord(JsonElement element, out Coord coord)
        {
            coord = default;

            if (!element.TryGetProperty("X", out JsonElement x) || !element.TryGetProperty("Y", out JsonElement y)) return false;
            if (x.ValueKind != JsonValueKind.Number || y.ValueKind != JsonValueKind.Number) return false;
            if (!x.TryGetInt32(out int xv) || !y.TryGetInt32(out int yv)) return false;
            if (xv < 0 || yv < 0 || xv > 255 || yv > 255) return false;

            coord = new Coord((byte)xv, (byte)yv);
            return true;
        }

        private void CheckString(string? text, string path, BitBoard secret, List<string> leaks)
        {
            if (string.IsNullOrEmpty(text)) return;

            if (Coord.TryParse(text, out Coord asCoord)
                && _engine.Board.Contains(asCoord)
                && secret.Get(asCoord.ToIndex(_engine.Board.Width)))
            {
                leaks.Add(path + " = \"" + text + "\" (secret cell in A1 notation)");
                return;
            }

            try
            {
                if (BitBoard.FromBase64(text).Intersects(secret))
                {
                    leaks.Add(path + " = base64 bitboard intersecting secret cells");
                }
            }
            catch (FormatException)
            {
            }
            catch (ArgumentException)
            {
            }
        }
    }
}
