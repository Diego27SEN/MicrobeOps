#nullable enable

using System;
using Armada.Core;
using UnityEngine.UIElements;

namespace Armada.Game
{
    /// <summary>What a single cell is currently showing.</summary>
    public enum CellVisual
    {
        Unknown = 0,
        Water = 1,
        Ship = 2,
        Miss = 3,
        Hit = 4,
        Sunk = 5,
        Preview = 6,
        PreviewInvalid = 7
    }

    /// <summary>
    /// The grid, built in code because its size comes from the board config.
    /// <para>
    /// Every state carries a shape as well as a colour - a hollow ring for a miss, a solid diamond
    /// for a hit, a hull bar for a sunk ship. Water, hit and sunk are never distinguishable by
    /// colour alone. That is an accessibility requirement for launch, and building it into the only
    /// class that draws cells is what keeps it from being forgotten one screen at a time.
    /// </para>
    /// <para>
    /// Cells are <see cref="Button"/>s so they are keyboard and mouse reachable for free, which is
    /// what lets the same UI serve touch and desktop without per-platform code.
    /// </para>
    /// </summary>
    public sealed class BoardGridView
    {
        private readonly VisualElement _root;
        private readonly Button[] _cells;
        private readonly VisualElement[] _marks;

        public BoardGridView(
            VisualElement root,
            int width,
            int height,
            Action<Coord>? onCellClicked,
            Action<Coord>? onCellHovered = null,
            Action? onPointerLeft = null,
            Action<Coord>? onCellDoubleClicked = null,
            Action? onSecondaryAction = null)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));

            Width = width;
            Height = height;
            _cells = new Button[width * height];
            _marks = new VisualElement[width * height];

            Build(onCellClicked, onCellHovered, onCellDoubleClicked);

            if (onPointerLeft != null)
            {
                _root.RegisterCallback<PointerLeaveEvent>(_ => onPointerLeft());
            }

            // The secondary action is rotation during deployment. Right click and the wheel are
            // what players of this genre reach for, and both are additive: the button and the R key
            // stay, so touch and keyboard lose nothing.
            if (onSecondaryAction != null)
            {
                _root.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 1) return;

                    onSecondaryAction();
                    evt.StopPropagation();
                });

                _root.RegisterCallback<WheelEvent>(evt =>
                {
                    onSecondaryAction();
                    evt.StopPropagation();
                });
            }
        }

        public int Width { get; }

        public int Height { get; }

        /// <summary>Rebuilds from scratch. The grid is cheap, and cheap beats clever cache invalidation.</summary>
        private void Build(Action<Coord>? onCellClicked, Action<Coord>? onCellHovered, Action<Coord>? onCellDoubleClicked)
        {
            _root.Clear();
            _root.AddToClassList("grid");

            // Column header: a spacer, then A..L.
            VisualElement header = new VisualElement();
            header.AddToClassList("grid__row");
            header.AddToClassList("grid__row--header");
            header.Add(Corner());

            for (int x = 0; x < Width; x++)
            {
                Label label = new Label(((char)('A' + x)).ToString());
                label.AddToClassList("grid__label");

                // Column headers carry their own modifier because they have to match the cell
                // footprint exactly, margins included. Sized like the row gutter instead, the
                // letters drift further from their column with every step across the board.
                label.AddToClassList("grid__label--column");
                header.Add(label);
            }

            _root.Add(header);

            for (int y = 0; y < Height; y++)
            {
                VisualElement row = new VisualElement();
                row.AddToClassList("grid__row");

                Label rowLabel = new Label((y + 1).ToString());
                rowLabel.AddToClassList("grid__label");
                rowLabel.AddToClassList("grid__label--row");
                row.Add(rowLabel);

                for (int x = 0; x < Width; x++)
                {
                    Coord coord = new Coord((byte)x, (byte)y);
                    int index = (y * Width) + x;

                    Button cell = new Button();
                    cell.AddToClassList("cell");
                    cell.name = "cell-" + coord.ToA1();

                    // Screen readers and keyboard users get the coordinate; sighted users get the
                    // headers. Neither depends on the other.
                    cell.tooltip = coord.ToA1();

                    VisualElement mark = new VisualElement();
                    mark.AddToClassList("cell__mark");
                    mark.pickingMode = PickingMode.Ignore;
                    cell.Add(mark);

                    if (onCellClicked != null)
                    {
                        cell.clickable = new Clickable(() => onCellClicked(coord));
                    }

                    // Hover is an addition for pointer devices, not a branch: touch simply never
                    // raises it, and the tap-to-place path works exactly the same either way.
                    if (onCellHovered != null)
                    {
                        cell.RegisterCallback<PointerEnterEvent>(_ => onCellHovered(coord));
                    }

                    // A second click on the same cell is a shortcut, never the only way to act: the
                    // Clickable above still handles single clicks and keyboard activation, so the
                    // cell stays reachable with Tab and Enter.
                    if (onCellDoubleClicked != null)
                    {
                        cell.RegisterCallback<ClickEvent>(evt =>
                        {
                            if (evt.clickCount >= 2) onCellDoubleClicked(coord);
                        });
                    }

                    _cells[index] = cell;
                    _marks[index] = mark;
                    row.Add(cell);
                }

                _root.Add(row);
            }
        }

        public void SetCell(Coord coord, CellVisual visual)
        {
            int index = coord.ToIndex(Width);
            if (index < 0 || index >= _cells.Length) return;

            Button cell = _cells[index];
            VisualElement mark = _marks[index];

            cell.RemoveFromClassList("cell--water");
            cell.RemoveFromClassList("cell--ship");
            cell.RemoveFromClassList("cell--miss");
            cell.RemoveFromClassList("cell--hit");
            cell.RemoveFromClassList("cell--sunk");
            cell.RemoveFromClassList("cell--preview");
            cell.RemoveFromClassList("cell--preview-invalid");

            mark.RemoveFromClassList("cell__mark--dot");
            mark.RemoveFromClassList("cell__mark--diamond");
            mark.RemoveFromClassList("cell__mark--hull");

            switch (visual)
            {
                case CellVisual.Water:
                case CellVisual.Unknown:
                    cell.AddToClassList("cell--water");
                    break;

                case CellVisual.Ship:
                    cell.AddToClassList("cell--ship");
                    break;

                case CellVisual.Miss:
                    cell.AddToClassList("cell--miss");
                    mark.AddToClassList("cell__mark--dot");
                    break;

                case CellVisual.Hit:
                    cell.AddToClassList("cell--hit");
                    mark.AddToClassList("cell__mark--diamond");
                    break;

                case CellVisual.Sunk:
                    cell.AddToClassList("cell--sunk");
                    mark.AddToClassList("cell__mark--hull");
                    break;

                case CellVisual.Preview:
                    cell.AddToClassList("cell--preview");
                    break;

                case CellVisual.PreviewInvalid:
                    cell.AddToClassList("cell--preview-invalid");
                    break;
            }
        }

        public void SetSelected(Coord coord, bool selected)
        {
            int index = coord.ToIndex(Width);
            if (index < 0 || index >= _cells.Length) return;

            if (selected) _cells[index].AddToClassList("cell--selected");
            else _cells[index].RemoveFromClassList("cell--selected");
        }

        public void ClearSelection()
        {
            for (int i = 0; i < _cells.Length; i++) _cells[i].RemoveFromClassList("cell--selected");
        }

        public void SetInteractable(bool interactable)
        {
            for (int i = 0; i < _cells.Length; i++) _cells[i].SetEnabled(interactable);
        }

        public void Fill(CellVisual visual)
        {
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++) SetCell(new Coord((byte)x, (byte)y), visual);
            }
        }

        private static VisualElement Corner()
        {
            VisualElement corner = new VisualElement();
            corner.AddToClassList("grid__label");
            corner.AddToClassList("grid__label--corner");
            return corner;
        }
    }
}
