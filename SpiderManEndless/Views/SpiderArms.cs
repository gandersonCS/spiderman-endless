using System.Collections.Generic;
using Microsoft.Xna.Framework;
using SpiderManEndless.UI;

namespace SpiderManEndless.Views;

/// <summary>
/// Where each attack frame's extras are on a costume's attack overlay sheet (Iron Spider's spider
/// arms: Textures/spiderman_ironspider_arms.png). The sheet has one <see cref="CellWidth"/> x
/// <see cref="CellHeight"/> cell per attack frame - row 0 = jab, row 1 = upward kick, row 2 =
/// uppercut (including its landing), row 3 = air kick - with the frame centered in the cell, so the
/// arms can reach outside the tight frame rectangles. Frames are matched by their spot on the sprite
/// sheet, so the same overlay works on the menu and in game. Must match
/// Textures/Originals/ironspider_arms.py.
/// </summary>
public static class SpiderArms
{
    public const int CellWidth = 96;
    public const int CellHeight = 112;

    private static readonly Dictionary<Rectangle, Point> Cells = BuildCells();

    private static Dictionary<Rectangle, Point> BuildCells()
    {
        var rows = new[]
        {
            MenuSpiderFrames.JabPunch, MenuSpiderFrames.UpwardKick, MenuSpiderFrames.Uppercut, SpiderFrames.AirKick,
        };
        var cells = new Dictionary<Rectangle, Point>();
        for (int row = 0; row < rows.Length; row++)
        {
            for (int column = 0; column < rows[row].Length; column++)
                cells.TryAdd(rows[row][column].Source, new Point(column, row));
        }
        return cells;
    }

    /// <summary>
    /// The overlay cell for an attack frame, and the origin that lines it up with the frame when
    /// drawn at the same position
    /// </summary>
    /// <returns>False if the frame isn't an attack frame</returns>
    public static bool TryGetCell(SpiderFrames.Frame frame, out Rectangle source, out Vector2 origin)
    {
        if (!Cells.TryGetValue(frame.Source, out var cell))
        {
            source = default;
            origin = default;
            return false;
        }

        source = new Rectangle(cell.X * CellWidth, cell.Y * CellHeight, CellWidth, CellHeight);
        var offset = new Vector2((CellWidth - frame.Source.Width) / 2, (CellHeight - frame.Source.Height) / 2);
        origin = frame.Origin + offset;
        return true;
    }
}
