#nullable enable

using System;
using System.Collections.Generic;
using Armada.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Armada.Game
{
    /// <summary>
    /// The match itself: your fleet on one grid, enemy waters on the other.
    /// <para>
    /// Everything drawn here comes from <see cref="MatchStateView"/> - the same projected DTO the
    /// server will send once online modes land. The screen never reads <c>MatchState</c>, even
    /// though offline the whole thing is sitting in the same process. Painting only from the
    /// projection is what guarantees no screen can accidentally start depending on data the server
    /// would never have sent.
    /// </para>
    /// </summary>
    public sealed class MatchScreen : ScreenController
    {
        private readonly Func<LocalMatchDriver?> _driver;
        private readonly Func<string> _viewerId;
        private readonly Action _onMatchFinished;
        private readonly Action<string> _showToast;

        /// <summary>Reports what the player did, for the onboarding. Ignored when it is not running.</summary>
        private readonly Action<TutorialTrigger> _report;

        private readonly List<Coord> _pendingVolley = new List<Coord>();

        private VisualElement? _root;
        private BoardGridView? _ownGrid;
        private BoardGridView? _enemyGrid;

        public MatchScreen(
            Func<LocalMatchDriver?> driver,
            Func<string> viewerId,
            Action onMatchFinished,
            Action<string> showToast,
            Action<TutorialTrigger> report)
        {
            _driver = driver;
            _viewerId = viewerId;
            _onMatchFinished = onMatchFinished;
            _showToast = showToast;
            _report = report;
        }

        public override string RootName
        {
            get { return "screen-match"; }
        }

        protected override void OnBind(VisualElement root)
        {
            _root = root;

            SetText(root, "match-own-caption", "match.your_fleet");
            SetText(root, "match-enemy-caption", "match.enemy_waters");

            WireButton(root, "match-fire", "match.fire", OnFire);
            WireButton(root, "match-forfeit", "match.forfeit", OnForfeit);

            BuildGrids(root);
            Refresh();
        }

        public override void OnShow()
        {
            _pendingVolley.Clear();
            if (_root != null) BuildGrids(_root);
            Refresh();

            // The first mover is decided by a coin flip in the engine, so half the matches open on
            // the AI's turn. Without this the board sits there inert: the player cannot fire
            // because it is not their turn, and nothing else was ever going to move the AI.
            LocalMatchDriver? driver = _driver();
            if (driver != null && driver.IsAiTurn) PlayAiTurnsIfNeeded(driver);
        }

        private void BuildGrids(VisualElement root)
        {
            LocalMatchDriver? driver = _driver();
            if (driver == null) return;

            VisualElement? ownHost = root.Q<VisualElement>("match-own-grid");
            VisualElement? enemyHost = root.Q<VisualElement>("match-enemy-grid");
            if (ownHost == null || enemyHost == null) return;

            _ownGrid = new BoardGridView(ownHost, driver.Board.Width, driver.Board.Height, null);
            _enemyGrid = new BoardGridView(
                enemyHost,
                driver.Board.Width,
                driver.Board.Height,
                OnEnemyCellClicked,
                onCellDoubleClicked: OnEnemyCellDoubleClicked);
        }

        /// <summary>Repaints from the projection. Cheap enough to call after every action.</summary>
        public void Refresh()
        {
            LocalMatchDriver? driver = _driver();
            if (driver == null || _root == null || _ownGrid == null || _enemyGrid == null) return;

            MatchStateView view = driver.ViewFor(_viewerId(), LocalClock.NowUnixMs());

            DrawOwnBoard(view, driver.Board);
            DrawEnemyBoard(view, driver.Board);

            Label? turn = _root.Q<Label>("match-turn");
            if (turn != null) turn.text = UiText.Get(view.IsYourTurn ? "match.your_turn" : "match.opponent_turn");

            int required = driver.ShotsRequiredThisTurn();
            Label? prompt = _root.Q<Label>("match-prompt");
            if (prompt != null)
            {
                // A shortcut nobody is told about is not a shortcut. It goes where the shot count
                // already lives, and only while the player is actually on the clock.
                if (!view.IsYourTurn) prompt.text = string.Empty;
                else if (required > 1) prompt.text = UiText.Get("match.shots_required").Replace("{0}", required.ToString());
                else prompt.text = UiText.Get("match.double_tap_to_fire");
            }

            Button? fire = _root.Q<Button>("match-fire");
            if (fire != null) fire.SetEnabled(view.IsYourTurn && _pendingVolley.Count == required);

            _enemyGrid.SetInteractable(view.IsYourTurn);

            DrawOwnFleet(view);
            DrawEnemyFleet(view, driver.Board);
        }

        /// <summary>Your own roster. Nothing to redact: it is your fleet.</summary>
        private void DrawOwnFleet(MatchStateView view)
        {
            VisualElement? host = _root!.Q<VisualElement>("match-own-fleet");
            if (host == null) return;

            host.Clear();

            for (int i = 0; i < view.You.Fleet.Length; i++)
            {
                ShipStatusView ship = view.You.Fleet[i];
                host.Add(ShipChip(UiText.Get("ship." + ship.Class) + " " + ship.Length, ship.IsSunk));
            }
        }

        /// <summary>
        /// What you know about the enemy fleet.
        /// <para>
        /// Built entirely from the projected view, which is the point: the fleet composition is
        /// public - both sides get the same ships from the board config - and which of them sank
        /// was already announced to you shot by shot. Nothing here needed a new field, and needing
        /// none is the evidence that it leaks nothing.
        /// </para>
        /// <para>
        /// Under Salvo the projection withholds the class of a sunk ship, so the roster cannot say
        /// which one went down and does not pretend to: the chips stay unmarked and only the count
        /// moves. That count is not new information either - the volley report already told you how
        /// many sank.
        /// </para>
        /// </summary>
        private void DrawEnemyFleet(MatchStateView view, BoardConfig board)
        {
            VisualElement? host = _root!.Q<VisualElement>("match-enemy-fleet");
            if (host == null) return;

            host.Clear();

            // Class alone does not identify a ship: admiral carries two battleships of different
            // lengths. When the rules reveal the sunk cells the length comes with them, so the
            // exact ship is attributed first and only the leftovers fall back to class alone.
            // Marking the five-cell battleship when the four-cell one went down would be telling
            // the player something they were never told.
            List<(ShipClass Class, int Length)> sunkShips = new List<(ShipClass, int)>();

            for (int i = 0; i < view.Opponent.SunkShips.Length; i++)
            {
                SunkShipView sunk = view.Opponent.SunkShips[i];
                if (!sunk.Class.HasValue) continue;

                sunkShips.Add((sunk.Class.Value, sunk.Cells?.Length ?? 0));
            }

            bool[] chipSunk = new bool[board.Fleet.Count];

            for (int pass = 0; pass < 2; pass++)
            {
                bool exact = pass == 0;

                for (int s = sunkShips.Count - 1; s >= 0; s--)
                {
                    (ShipClass sunkClass, int sunkLength) = sunkShips[s];
                    if (exact && sunkLength <= 0) continue;

                    for (int i = 0; i < board.Fleet.Count; i++)
                    {
                        if (chipSunk[i]) continue;
                        if (board.Fleet[i].Class != sunkClass) continue;
                        if (exact && board.Fleet[i].Length != sunkLength) continue;

                        chipSunk[i] = true;
                        sunkShips.RemoveAt(s);
                        break;
                    }
                }
            }

            for (int i = 0; i < board.Fleet.Count; i++)
            {
                ShipSpec spec = board.Fleet[i];
                host.Add(ShipChip(UiText.Get("ship." + spec.Class) + " " + spec.Length, chipSunk[i]));
            }

            Label count = new Label(
                UiText.Get("match.fleet_sunk")
                    .Replace("{0}", view.Opponent.SunkShips.Length.ToString())
                    .Replace("{1}", board.Fleet.Count.ToString()));

            count.AddToClassList("fleet__count");
            host.Add(count);
        }

        private static Label ShipChip(string text, bool sunk)
        {
            Label chip = new Label(text);
            chip.AddToClassList("fleet__ship");
            if (sunk) chip.AddToClassList("fleet__ship--sunk");
            return chip;
        }

        private void DrawOwnBoard(MatchStateView view, BoardConfig board)
        {
            BitBoard occupancy = BitBoard.FromBase64(view.You.OccupancyBase64);
            BitBoard hits = BitBoard.FromBase64(view.You.HitsBase64);
            BitBoard incoming = BitBoard.FromBase64(view.You.IncomingShotsBase64);

            BitBoard sunkCells = BitBoard.Empty;
            for (int i = 0; i < view.You.Fleet.Length; i++)
            {
                ShipStatusView ship = view.You.Fleet[i];
                if (!ship.IsSunk) continue;

                for (int c = 0; c < ship.Length; c++)
                {
                    int x = ship.Orientation == Orientation.Horizontal ? ship.Bow.X + c : ship.Bow.X;
                    int y = ship.Orientation == Orientation.Vertical ? ship.Bow.Y + c : ship.Bow.Y;
                    sunkCells = sunkCells.Set((y * board.Width) + x);
                }
            }

            for (int index = 0; index < board.CellCount; index++)
            {
                Coord cell = board.FromIndex(index);

                CellVisual visual;
                if (sunkCells.Get(index)) visual = CellVisual.Sunk;
                else if (hits.Get(index)) visual = CellVisual.Hit;
                else if (incoming.Get(index)) visual = CellVisual.Miss;
                else if (occupancy.Get(index)) visual = CellVisual.Ship;
                else visual = CellVisual.Water;

                _ownGrid!.SetCell(cell, visual);
            }
        }

        private void DrawEnemyBoard(MatchStateView view, BoardConfig board)
        {
            BitBoard shots = BitBoard.FromBase64(view.Opponent.ShotsBase64);
            BitBoard hits = BitBoard.FromBase64(view.Opponent.HitsBase64);

            BitBoard sunkCells = BitBoard.Empty;
            for (int i = 0; i < view.Opponent.SunkShips.Length; i++)
            {
                Coord[]? cells = view.Opponent.SunkShips[i].Cells;
                if (cells == null) continue;

                for (int c = 0; c < cells.Length; c++) sunkCells = sunkCells.Set(cells[c].ToIndex(board.Width));
            }

            for (int index = 0; index < board.CellCount; index++)
            {
                Coord cell = board.FromIndex(index);

                CellVisual visual;
                if (sunkCells.Get(index)) visual = CellVisual.Sunk;
                else if (hits.Get(index)) visual = CellVisual.Hit;
                else if (shots.Get(index)) visual = CellVisual.Miss;
                else visual = CellVisual.Unknown;

                _enemyGrid!.SetCell(cell, visual);
            }

            _enemyGrid!.ClearSelection();
            for (int i = 0; i < _pendingVolley.Count; i++) _enemyGrid.SetSelected(_pendingVolley[i], true);
        }

        private void OnEnemyCellClicked(Coord coord)
        {
            LocalMatchDriver? driver = _driver();
            if (driver == null || !driver.ViewFor(_viewerId(), LocalClock.NowUnixMs()).IsYourTurn) return;

            if (_pendingVolley.Contains(coord))
            {
                _pendingVolley.Remove(coord);
                Refresh();
                return;
            }

            int required = driver.ShotsRequiredThisTurn();

            // With a single shot per turn, tapping a cell replaces the selection instead of being
            // ignored: correcting an aim should never need a deselect first.
            if (required == 1) _pendingVolley.Clear();
            else if (_pendingVolley.Count >= required) return;

            _pendingVolley.Add(coord);
            Refresh();
        }

        /// <summary>
        /// Second click on a cell: aim there and fire, without the trip to the button.
        /// <para>
        /// The volley is set explicitly here rather than built on whatever the single clicks left
        /// behind. Both handlers run for a double click - the first click selects, the second
        /// toggles the same cell back off - so reading that state would fire at nothing. Salvo
        /// still needs its full volley, so there the shortcut only fires once the last cell is in.
        /// </para>
        /// </summary>
        private void OnEnemyCellDoubleClicked(Coord coord)
        {
            LocalMatchDriver? driver = _driver();
            if (driver == null || !driver.ViewFor(_viewerId(), LocalClock.NowUnixMs()).IsYourTurn) return;

            int required = driver.ShotsRequiredThisTurn();

            if (required == 1)
            {
                _pendingVolley.Clear();
                _pendingVolley.Add(coord);
            }
            else if (!_pendingVolley.Contains(coord))
            {
                if (_pendingVolley.Count >= required) return;
                _pendingVolley.Add(coord);
            }

            if (_pendingVolley.Count != required)
            {
                Refresh();
                return;
            }

            OnFire();
        }

        private void OnFire()
        {
            LocalMatchDriver? driver = _driver();
            if (driver == null || _pendingVolley.Count == 0) return;

            ApplyResult result = driver.Fire(_viewerId(), _pendingVolley.ToArray(), LocalClock.NowUnixMs());

            if (!result.Success)
            {
                _showToast(UiText.Error(result.Error!));
                Refresh();
                return;
            }

            _pendingVolley.Clear();
            ReportShots(result);
            Refresh();

            if (driver.IsFinished)
            {
                _onMatchFinished();
                return;
            }

            PlayAiTurnsIfNeeded(driver);
        }

        /// <summary>
        /// Lets the AI take its turn (or turns, when a hit grants another under blitz rules).
        /// <para>
        /// Resolved synchronously for now. M2's goal is a complete offline match; pacing the shots
        /// with animation is a polish task, and doing it here would mean holding UI state across
        /// awaits before there is anything to look at.
        /// </para>
        /// </summary>
        private void PlayAiTurnsIfNeeded(LocalMatchDriver driver)
        {
            int guard = 0;

            while (driver.IsAiTurn && !driver.IsFinished && guard++ < 64)
            {
                ApplyResult? result = driver.StepAi(LocalClock.NowUnixMs());
                if (result == null || !result.Success)
                {
                    Debug.LogError("[Match] The AI could not move: " + result?.Error);
                    break;
                }
            }

            Refresh();
            if (driver.IsFinished) _onMatchFinished();
        }

        /// <summary>
        /// Tells the onboarding what the volley did. Salvo reports through the aggregate summary,
        /// because there the per-cell results deliberately do not exist.
        /// </summary>
        private void ReportShots(ApplyResult result)
        {
            _report(TutorialTrigger.ShotFired);

            if (result.Volley != null)
            {
                if (result.Volley.Hits > 0) _report(TutorialTrigger.ShotHit);
                if (result.Volley.Sunk.Length > 0) _report(TutorialTrigger.ShipSunk);
                return;
            }

            for (int i = 0; i < result.Shots.Length; i++)
            {
                ShotOutcome outcome = result.Shots[i].Outcome;

                if (outcome != ShotOutcome.Miss) _report(TutorialTrigger.ShotHit);
                if (outcome == ShotOutcome.Sunk || outcome == ShotOutcome.Win) _report(TutorialTrigger.ShipSunk);
            }
        }

        private void OnForfeit()
        {
            LocalMatchDriver? driver = _driver();
            if (driver == null) return;

            ApplyResult result = driver.Forfeit(_viewerId(), LocalClock.NowUnixMs());
            if (!result.Success)
            {
                _showToast(UiText.Error(result.Error!));
                return;
            }

            _onMatchFinished();
        }
    }
}
