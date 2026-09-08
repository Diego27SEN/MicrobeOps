#nullable enable

using System;
using System.Collections.Generic;
using Armada.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Armada.Game
{
    /// <summary>
    /// Fleet deployment. Tap a ship, tap a cell, rotate if needed - which works identically with a
    /// finger, a mouse or the keyboard, so there is no per-platform branch anywhere in here.
    /// <para>
    /// Validation is not reimplemented: every candidate layout goes through
    /// <see cref="PlacementRules"/>, the same code the server runs. The screen's only job is to
    /// show what is legal and to say why when it is not.
    /// </para>
    /// </summary>
    public sealed class PlacementScreen : ScreenController
    {
        private readonly Func<LocalMatchDriver?> _driver;
        private readonly Func<string> _currentPlayerId;
        private readonly Action _onPlacementSubmitted;
        private readonly Action<string> _showToast;

        /// <summary>Reports what the player did, for the onboarding. Ignored when it is not running.</summary>
        private readonly Action<TutorialTrigger> _report;

        private readonly List<ShipPlacement> _placed = new List<ShipPlacement>();

        private BoardGridView? _grid;
        private VisualElement? _root;
        private int _selectedShip;
        private Orientation _orientation = Orientation.Horizontal;

        /// <summary>Cell the pointer is over, which anchors the placement preview.</summary>
        private Coord? _hovered;

        public PlacementScreen(
            Func<LocalMatchDriver?> driver,
            Func<string> currentPlayerId,
            Action onPlacementSubmitted,
            Action<string> showToast,
            Action<TutorialTrigger> report)
        {
            _driver = driver;
            _currentPlayerId = currentPlayerId;
            _onPlacementSubmitted = onPlacementSubmitted;
            _showToast = showToast;
            _report = report;
        }

        public override string RootName
        {
            get { return "screen-placement"; }
        }

        protected override void OnBind(VisualElement root)
        {
            _root = root;

            SetText(root, "placement-title", "placement.title");
            SetText(root, "placement-hint", "placement.hint");

            // Without this the player is trapped: the match has not started, so there is nothing to
            // forfeit, and there was no other way off this screen.
            WireButton(root, "placement-back", "common.back", () => Router.Show(ScreenId.ModeSelect));

            WireButton(root, "placement-random", "placement.random", OnRandom);
            WireButton(root, "placement-rotate", "placement.rotate", OnRotate);
            WireButton(root, "placement-confirm", "placement.confirm", OnConfirm);

            // Rotating is the one action with no obvious target, so it also gets a key. The button
            // stays, because a key alone is invisible on a phone.
            root.focusable = true;
            root.RegisterCallback<KeyDownEvent>(OnKeyDown);

            BuildGrid(root);
            Redraw();
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.R) return;

            OnRotate();
            evt.StopPropagation();
        }

        public override void OnShow()
        {
            _placed.Clear();
            _selectedShip = 0;
            _orientation = Orientation.Horizontal;
            _hovered = null;

            if (_root != null) BuildGrid(_root);
            Redraw();
        }

        private void BuildGrid(VisualElement root)
        {
            LocalMatchDriver? driver = _driver();
            VisualElement? host = root.Q<VisualElement>("placement-grid");
            if (driver == null || host == null) return;

            _grid = new BoardGridView(
                host,
                driver.Board.Width,
                driver.Board.Height,
                OnCellClicked,
                OnCellHovered,
                OnPointerLeftGrid,
                onSecondaryAction: OnRotate);
        }

        private void OnCellHovered(Coord coord)
        {
            if (_hovered.HasValue && _hovered.Value == coord) return;

            _hovered = coord;
            Redraw();
        }

        private void OnPointerLeftGrid()
        {
            if (!_hovered.HasValue) return;

            _hovered = null;
            Redraw();
        }

        private void OnRandom()
        {
            LocalMatchDriver? driver = _driver();
            if (driver == null) return;

            _placed.Clear();
            _placed.AddRange(driver.SuggestPlacement());
            _selectedShip = _placed.Count;
            _hovered = null;
            Redraw();

            // Using Random satisfies both placement steps at once, so the onboarding does not sit
            // there asking for a ship the player has already laid out.
            _report(TutorialTrigger.ShipPlaced);
            _report(TutorialTrigger.FleetComplete);
        }

        private void OnRotate()
        {
            _orientation = _orientation == Orientation.Horizontal ? Orientation.Vertical : Orientation.Horizontal;

            // Rotating with nothing on screen to show for it was the complaint: the preview and the
            // button label both have to move, or the button reads as a no-op.
            Redraw();
        }

        private void OnCellClicked(Coord coord)
        {
            LocalMatchDriver? driver = _driver();
            if (driver == null) return;

            // Tapping an already-placed ship picks it up again, which is the shortest correction
            // path and needs no separate undo button.
            for (int i = 0; i < _placed.Count; i++)
            {
                if (!Covers(driver.Board, _placed[i], i, coord)) continue;

                _placed.RemoveRange(i, _placed.Count - i);
                _selectedShip = i;
                Redraw();
                return;
            }

            if (_selectedShip >= driver.Board.Fleet.Count) return;

            // One gesture, two input models. A pointer has already previewed this cell on hover, so
            // the click lands on it and places straight away. A finger raises no hover at all, so
            // the first tap previews and the second confirms - which is how the footprint becomes
            // visible on a phone without a separate mode or a per-platform branch.
            if (!_hovered.HasValue || _hovered.Value != coord)
            {
                _hovered = coord;
                Redraw();
                return;
            }

            ShipPlacement candidate = new ShipPlacement(driver.Board.Fleet[_selectedShip].Class, coord, _orientation);

            List<ShipPlacement> attempt = new List<ShipPlacement>(_placed) { candidate };
            if (!IsPrefixValid(driver, attempt))
            {
                _showToast(UiText.Get("error.ERR_INVALID_PLACEMENT"));
                return;
            }

            _placed.Add(candidate);
            _selectedShip++;
            _hovered = null;
            Redraw();

            _report(TutorialTrigger.ShipPlaced);
            if (_placed.Count == driver.Board.Fleet.Count) _report(TutorialTrigger.FleetComplete);
        }

        private void OnConfirm()
        {
            LocalMatchDriver? driver = _driver();
            if (driver == null) return;

            PlacementValidation validation = driver.ValidatePlacement(_placed);
            if (!validation.IsValid)
            {
                _showToast(UiText.Error(validation.Error!));
                return;
            }

            ApplyResult result = driver.SubmitPlacement(_currentPlayerId(), _placed, NowUnixMs());
            if (!result.Success)
            {
                _showToast(UiText.Error(result.Error!));
                return;
            }

            _onPlacementSubmitted();
        }

        /// <summary>
        /// Validates a partial fleet by checking it against a board whose spec is truncated to the
        /// ships placed so far. Reusing the real validator on a prefix keeps overlap and adjacency
        /// rules identical to the final check, instead of a second, subtly different implementation
        /// living in the UI.
        /// </summary>
        private static bool IsPrefixValid(LocalMatchDriver driver, List<ShipPlacement> attempt)
        {
            List<ShipSpec> prefix = new List<ShipSpec>(attempt.Count);
            for (int i = 0; i < attempt.Count; i++) prefix.Add(driver.Board.Fleet[i]);

            BoardConfig partial = new BoardConfig
            {
                Id = driver.Board.Id,
                Width = driver.Board.Width,
                Height = driver.Board.Height,
                Fleet = prefix
            };

            return PlacementRules.Validate(partial, driver.Rules, attempt).IsValid;
        }

        private static bool Covers(BoardConfig board, ShipPlacement placement, int fleetIndex, Coord coord)
        {
            int length = board.Fleet[fleetIndex].Length;

            for (int i = 0; i < length; i++)
            {
                int x = placement.Orientation == Orientation.Horizontal ? placement.Bow.X + i : placement.Bow.X;
                int y = placement.Orientation == Orientation.Vertical ? placement.Bow.Y + i : placement.Bow.Y;
                if (x == coord.X && y == coord.Y) return true;
            }

            return false;
        }

        private void Redraw()
        {
            LocalMatchDriver? driver = _driver();
            if (_grid == null || driver == null || _root == null) return;

            _grid.Fill(CellVisual.Water);

            for (int i = 0; i < _placed.Count; i++)
            {
                IReadOnlyList<Coord> cells = PlacementRules.CellsOf(driver.Board, _placed[i], driver.Board.Fleet[i].Length);
                for (int c = 0; c < cells.Count; c++) _grid.SetCell(cells[c], CellVisual.Ship);
            }

            int remaining = driver.Board.Fleet.Count - _placed.Count;

            DrawPreview(driver, remaining);

            Label? counter = _root.Q<Label>("placement-remaining");
            if (counter != null) counter.text = UiText.Get("placement.remaining").Replace("{0}", remaining.ToString());

            Button? confirm = _root.Q<Button>("placement-confirm");
            if (confirm != null) confirm.SetEnabled(remaining == 0);

            Label? nextShip = _root.Q<Label>("placement-next-ship");
            if (nextShip != null)
            {
                // Naming the ship and its length is what makes the preview readable: "Portaaviones
                // (5)" tells you how far the footprint reaches before you go looking for it.
                nextShip.text = remaining > 0
                    ? UiText.Get("ship." + driver.Board.Fleet[_placed.Count].Class)
                        + " (" + driver.Board.Fleet[_placed.Count].Length + ")"
                    : string.Empty;
            }

            Button? rotate = _root.Q<Button>("placement-rotate");
            if (rotate != null)
            {
                string orientationKey = _orientation == Orientation.Horizontal
                    ? "placement.orientation.horizontal"
                    : "placement.orientation.vertical";

                rotate.text = UiText.Get("placement.rotate") + " · " + UiText.Get(orientationKey);
                rotate.SetEnabled(remaining > 0);
            }
        }

        /// <summary>
        /// Paints the footprint the next ship would occupy under the pointer, in valid or invalid
        /// colours. Cells that fall off the board are simply not painted, so a ship hanging over
        /// the edge shows only the part that would land - and shows it as invalid.
        /// </summary>
        private void DrawPreview(LocalMatchDriver driver, int remaining)
        {
            if (_grid == null || !_hovered.HasValue || remaining <= 0) return;

            ShipSpec spec = driver.Board.Fleet[_placed.Count];
            ShipPlacement candidate = new ShipPlacement(spec.Class, _hovered.Value, _orientation);

            List<ShipPlacement> attempt = new List<ShipPlacement>(_placed) { candidate };
            bool valid = IsPrefixValid(driver, attempt);
            CellVisual visual = valid ? CellVisual.Preview : CellVisual.PreviewInvalid;

            for (int i = 0; i < spec.Length; i++)
            {
                int x = _orientation == Orientation.Horizontal ? _hovered.Value.X + i : _hovered.Value.X;
                int y = _orientation == Orientation.Vertical ? _hovered.Value.Y + i : _hovered.Value.Y;

                if (x >= driver.Board.Width || y >= driver.Board.Height) continue;

                _grid.SetCell(new Coord((byte)x, (byte)y), visual);
            }
        }

        private static long NowUnixMs()
        {
            return LocalClock.NowUnixMs();
        }
    }

    /// <summary>
    /// The one place the client reads the wall clock.
    /// <para>
    /// Core is forbidden from touching the clock, and rightly so, but somebody has to supply
    /// <c>nowUnixMs</c>. Keeping that in a single named function means the day this has to become
    /// "server time, corrected by the boot handshake offset" - which is exactly what happens in M3 -
    /// there is one place to change.
    /// </para>
    /// </summary>
    public static class LocalClock
    {
        public static long NowUnixMs()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }
}
