using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace SpiderManEndless.Views;

/// <summary>
/// The sprite sheets for the hazards, and where each frame is on them.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>Textures/task_force_soldier</c>: 64x64 cells, 8 per row, feet on the bottom of the cell.
/// Cut out of the Open Gunner sheet (left-facing frames only, background removed).
/// Row 0 = run cycle; row 1 = stand, duck, jump, hurt, aim up, aim up (steep), aim down, aim down (steep).</item>
/// <item><c>Textures/task_force_pylon</c>: 32x32 frames, 2 across and 3 down, the light pulsing</item>
/// <item><c>Textures/pumpkin_bomb</c>: one 16x16 frame</item>
/// <item><c>Textures/spider_sense</c>: 32x32 frames, 2x2, the warning flashing</item>
/// </list>
/// </remarks>
public sealed class HazardSprites
{
    public HazardSprites(ContentManager content)
    {
        Soldier = content.Load<Texture2D>("Textures/task_force_soldier");
        Pylon = content.Load<Texture2D>("Textures/task_force_pylon");
        Pumpkin = content.Load<Texture2D>("Textures/pumpkin_bomb");
        SpiderSense = content.Load<Texture2D>("Textures/spider_sense");
    }

    public Texture2D Soldier { get; }
    public Texture2D Pylon { get; }
    public Texture2D Pumpkin { get; }
    public Texture2D SpiderSense { get; }

    // ---- soldier ----

    public const int SoldierCell = 64;

    /// <summary>Soldiers are drawn at 1.5x so they're about Spider-Man's size</summary>
    public const float SoldierScale = 1.5f;

    /// <summary>Bottom center of the figure in a cell (his feet)</summary>
    public static readonly Vector2 SoldierFeet = new(31, 64);

    /// <summary>Middle of the figure in a cell, for spinning him when he's knocked away</summary>
    public static readonly Vector2 SoldierMiddle = new(31, 42);

    public const int RunFrames = 8;
    public const int StandFrame = 8;
    public const int DuckFrame = 9;
    public const int JumpFrame = 10;
    public const int HurtFrame = 11;
    public const int AimUpFrame = 12;
    public const int AimDownFrame = 14;

    public static Rectangle SoldierFrame(int index)
        => new(index % 8 * SoldierCell, index / 8 * SoldierCell, SoldierCell, SoldierCell);

    // ---- pylon ----

    public const int PylonFrameSize = 32;
    public const int PylonFrames = 6;
    public const float PylonScale = 2f;
    public static readonly Vector2 PylonBottom = new(16, 32);

    public static Rectangle PylonFrame(int index)
        => new(index % 2 * PylonFrameSize, index / 2 * PylonFrameSize, PylonFrameSize, PylonFrameSize);

    // ---- pumpkin bomb ----

    public const float PumpkinScale = 2f;
    public static readonly Vector2 PumpkinCenter = new(7.5f, 7.5f);

    /// <summary>Where the fuse is, relative to the center (sprite pixels)</summary>
    public static readonly Vector2 PumpkinFuse = new(0, -6);

    // ---- spider sense ----

    public const int SenseFrameSize = 32;
    public const int SenseFrames = 4;

    /// <summary>Middle of the drawing in a frame</summary>
    public static readonly Vector2 SenseCenter = new(16, 19);

    public static Rectangle SenseFrame(int index)
        => new(index % 2 * SenseFrameSize, index / 2 * SenseFrameSize, SenseFrameSize, SenseFrameSize);
}
