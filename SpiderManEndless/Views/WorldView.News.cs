using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using CIS580.UI;
using CIS580.Views;
using SpiderManEndless.Simulation;

namespace SpiderManEndless.Views;

/// <summary>
/// The Daily Bugle boards: big news screens standing behind the rooftops that teach the game and
/// report on the task force. Each one has a "LIVE FOOTAGE" feed on the left (the actual rooftops
/// and Spider-Man, redrawn small) and a headline with a few lines on the right.
/// </summary>
/// <remarks>
/// <para>
/// The simulation decides when a board goes up (<see cref="NewsBoardRaised"/> and
/// <see cref="TaskForceAdapted"/>); this only decides where and how it's drawn.
/// </para>
/// <para>
/// Boards are far behind the rooftops, so they drift by very slowly (<see cref="BoardParallax"/>
/// of the roofs' speed): one takes about 100 m to cross the screen. Only one is up at a time; a
/// new one waits off the right edge until the last one is gone. Everything is measured in
/// distance run, not time, so boards stop when the game is paused.
/// </para>
/// </remarks>
public partial class WorldView
{
    // ---- the sprite (Textures/daily_bugle_board: 50x60 frames, 2 across, 3 frames of the lamp glinting) ----

    private const int BoardFrameWidth = 50;
    private const int BoardFrameHeight = 60;
    private const int BoardFrames = 3;

    /// <summary>The news screen inside a frame (the white part), in sheet pixels</summary>
    private static readonly Rectangle BoardScreen = new(1, 10, 48, 19);

    /// <summary>Drawn this many times its pixel size, so the text on it can be read (400 wide)</summary>
    private const float BoardScale = 8f;

    private const float BoardWidth = BoardFrameWidth * BoardScale;

    /// <summary>
    /// World y of the top of the sprite. Puts the top of the frame just under the pause button,
    /// and the screen above all but the tallest roofs
    /// </summary>
    private const float BoardTop = -24f;

    /// <summary>The controls board starts on screen here (its left edge), just ahead of him</summary>
    private const float FirstBoardX = 300f;

    /// <summary>
    /// Boards move this fraction of the rooftops' speed. At 0.27 one takes about 100 m to cross
    /// the screen (about 12 seconds running), which is how far apart the teaching boards are
    /// </summary>
    private const float BoardParallax = 0.27f;

    // ---- what's on the screen ----

    private static readonly Color BoardInk = new(6, 0, 74);            // same navy as the legs
    private static readonly Color BoardRed = new(214, 24, 24);
    private static readonly Color FeedFrame = new(20, 20, 28);
    private const float KickerScale = 0.38f;
    private const float HeadlineScale = 0.64f;
    private const float BodyScale = 0.47f;

    // the screen's layout (world units, from the screen's top left)
    private const float FeedLeft = 8, FeedTop = 24, FeedWidth = 128;
    private const float ColumnLeft = FeedLeft + FeedWidth + 12;

    /// <summary>How zoomed out the live feed is: 1 world unit in the feed = this many on the board</summary>
    private const float FeedZoom = 0.6f;

    private static readonly RasterizerState ScissorRaster = new() { ScissorTestEnable = true, CullMode = CullMode.None };

    /// <summary>
    /// A board that's up: what it says, where it starts (its left edge), and the distance (world
    /// units) it starts moving at. Until then it waits there
    /// </summary>
    private sealed record PostedBoard(string Kicker, string Headline, string Body, float StartX, double StartUnits,
        BoardPreview Preview = BoardPreview.Live, int Variant = 0)
    {
        /// <summary>The distance (world units) it's all the way off the left edge at</summary>
        public double GoneUnits => StartUnits + (StartX + BoardWidth) / BoardParallax;
    }

    private readonly List<PostedBoard> _boards = new();

    /// <summary>What a board's feed shows</summary>
    private enum BoardPreview
    {
        /// <summary>The real rooftops and Spider-Man, live</summary>
        Live,
        /// <summary>A replay: his spider sense goes off and he jumps a threat (a random one of three)</summary>
        SpiderSense,
        /// <summary>A replay: he takes down a soldier (on a roof or in the air, at random)</summary>
        Takedown,
    }

    /// <summary>Picks which threat or soldier a replay shows (just for looks, so not the simulation's random)</summary>
    private readonly Random _replayRandom = new();

    /// <summary>Distance travelled, in world units (doesn't jump around on a continue, unlike the scroll)</summary>
    private static double DistanceUnits(RooftopTrack track) => track.DistanceMeters * RooftopTrack.UnitsPerMeter;

    /// <summary>Left edge of a board: waiting at its start, then drifting left</summary>
    private static float BoardX(PostedBoard board, RooftopTrack track)
        => board.StartX - (float)(Math.Max(0, DistanceUnits(track) - board.StartUnits) * BoardParallax);

    /// <summary>True while a board is anywhere on screen (the task force callout moves out of its way)</summary>
    private bool BoardShowing(RooftopTrack track)
        => _boards.Exists(b => BoardX(b, track) < World.Width && BoardX(b, track) + BoardWidth > 0);

    /// <summary>Just under the boards' screens (where the task force callout goes while one is up)</summary>
    private static float BelowBoards => BoardTop + (BoardScreen.Y + BoardScreen.Height) * BoardScale + 30;

    /// <summary>Puts up a teaching board (from <see cref="NewsBoardRaised"/>)</summary>
    private void PostBoard(NewsBoard kind)
    {
        switch (kind)
        {
            case NewsBoard.Controls:
                // a new run: anything left over from the last one goes
                _boards.Clear();
                PostBoard("LIVE", "MENACE ON THE RUN!",
                    "TAP SPACE: JUMP\nHOLD SPACE: JUMP HIGHER\nIN THE AIR, PRESS AGAIN AND HOLD: SWING!",
                    onScreen: true);
                break;

            case NewsBoard.SpiderSense:
                PostBoard("EXCLUSIVE", "TRUST THE TINGLE!",
                    "HIS SPIDER-SENSE WARNS HIM BEFORE EVERY ATTACK.\nWHEN IT FLASHES, MOVE!",
                    preview: BoardPreview.SpiderSense);
                break;

            case NewsBoard.Soldiers:
                PostBoard("BREAKING", "TASK FORCE ENGAGES!",
                    "ONE TOUCH TAKES AN OFFICER DOWN.\nBUT THEIR ROUNDS ARE LETHAL - DODGE THEM!",
                    preview: BoardPreview.Takedown);
                break;
        }
    }

    private void PostBoard(string kicker, string headline, string body, bool onScreen = false,
        BoardPreview preview = BoardPreview.Live)
    {
        // comes in from just off the right edge (or starts on screen). Only one board at a time:
        // it waits off screen until the one before it is gone
        double start = DistanceUnits(_session.World.Track);
        if (_boards.Count > 0)
            start = Math.Max(start, _boards[^1].GoneUnits);

        _boards.Add(new PostedBoard(kicker, headline, body, onScreen ? FirstBoardX : World.Width + 10, start,
            preview, _replayRandom.Next(3)));
    }

    /// <summary>
    /// Draws the boards that are on screen, behind the rooftops (pixel art batch; leaves it that way)
    /// </summary>
    private void DrawNewsBoards(Camera2D camera, RooftopTrack track, Runner runner, float left, float right, float bottom)
    {
        _boards.RemoveAll(b => BoardX(b, track) + BoardWidth < Math.Min(left, 0) - 20);

        var sheet = _hazardSprites?.BugleBoard;
        if (sheet == null || _font == null || _boards.Count == 0)
            return;

        int frame = (int)(_clock * 6) % BoardFrames;
        var source = new Rectangle(frame % 2 * BoardFrameWidth, frame / 2 * BoardFrameHeight, BoardFrameWidth, BoardFrameHeight);

        foreach (var board in _boards)
        {
            float x = MathF.Round(BoardX(board, track));
            if (x > right || x + BoardWidth < left)
                continue;

            // the legs go on down behind the roofs (the bottom row of the sprite, stretched), so
            // they never stop in mid-air over a gap
            float spriteBottom = BoardTop + BoardFrameHeight * BoardScale;
            if (bottom > spriteBottom)
            {
                var legs = new Rectangle(source.X, source.Bottom - 1, BoardFrameWidth, 1);
                SpriteBatch.Draw(sheet, new Rectangle((int)x, (int)spriteBottom, (int)BoardWidth, (int)MathF.Ceiling(bottom - spriteBottom)),
                    legs, Color.White);
            }
            SpriteBatch.Draw(sheet, new Vector2(x, BoardTop), source, Color.White, 0, Vector2.Zero, BoardScale,
                SpriteEffects.None, 0);

            var screen = new RectangleF(x + BoardScreen.X * BoardScale, BoardTop + BoardScreen.Y * BoardScale,
                BoardScreen.Width * BoardScale, BoardScreen.Height * BoardScale);
            DrawBoardFeed(camera, track, runner, screen, board);
            DrawBoardText(camera, board, screen);
        }

        Rebegin(camera, BlendState.AlphaBlend, SamplerState.PointClamp);
    }

    /// <summary>
    /// The left side of the screen: a blinking light, and the feed under it. Either live (the
    /// real rooftops and Spider-Man around where he is right now, redrawn small) or a replay (see
    /// <see cref="DrawReplay"/>), clipped to the box
    /// </summary>
    private void DrawBoardFeed(Camera2D camera, RooftopTrack track, Runner runner, RectangleF screen, PostedBoard board)
    {
        var box = new RectangleF(screen.X + FeedLeft, screen.Y + FeedTop, FeedWidth, screen.Height - FeedTop - 8);

        // frame + the sky behind the feed (the time-of-day color)
        Fill(new RectangleF(box.X - 2, box.Y - 2, box.Width + 4, box.Height + 4), FeedFrame);
        Fill(box, BackgroundColor);

        // blinking "LIVE" light
        if ((int)(_clock * 2) % 2 == 0)
            Fill(new RectangleF(screen.X + 9, screen.Y + 7, 10, 10), BoardRed);

        // where to look: around him, a little ahead (the way he's going)
        var look = new Vector2(RooftopTrack.RunnerX + 30, runner.FeetY - 30);
        float halfWidth = box.Width / 2 / FeedZoom;
        float halfHeight = box.Height / 2 / FeedZoom;

        // clip to the box: the scissor rectangle is in screen pixels
        var topLeft = camera.WorldToScreen(new Vector2(box.X, box.Y));
        var bottomRight = camera.WorldToScreen(new Vector2(box.X + box.Width, box.Y + box.Height));
        var clip = Rectangle.Intersect(
            new Rectangle((int)topLeft.X, (int)topLeft.Y, (int)(bottomRight.X - topLeft.X), (int)(bottomRight.Y - topLeft.Y)),
            camera.Viewport);
        if (clip.Width <= 0 || clip.Height <= 0)
            return;

        if (board.Preview != BoardPreview.Live)
        {
            DrawReplay(camera, box, clip, board);
            DrawScanlines(box);
            return;
        }

        // world -> feed: move what we're looking at to the origin, shrink it, put it in the box,
        // then the normal camera on top of that
        var feed = Matrix.CreateTranslation(-look.X, -look.Y, 0)
                   * Matrix.CreateScale(FeedZoom)
                   * Matrix.CreateTranslation(box.X + box.Width / 2, box.Y + box.Height / 2, 0)
                   * camera.Transform;

        SpriteBatch.End();
        Game.GraphicsDevice.ScissorRectangle = clip;
        SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, ScissorRaster,
            null, feed);

        // the same drawing as the real thing (DrawSpiderMan is run again for real later this
        // frame, which resets what it remembers for the invulnerable glow)
        DrawBuildings(track, look.X - halfWidth - 40, look.X + halfWidth + 40, look.Y + halfHeight + 40);
        DrawSpiderMan(track, runner);

        Rebegin(camera, BlendState.AlphaBlend, SamplerState.PointClamp);
        DrawScanlines(box);
    }

    /// <summary>Scanlines over a feed, so it reads as a TV picture</summary>
    private void DrawScanlines(RectangleF box)
    {
        for (float y = box.Y + 1; y < box.Y + box.Height; y += 4)
            Fill(new RectangleF(box.X, y, box.Width, 1), Color.Black * 0.18f);
    }

    // ---- replays: little looping scenes drawn with the game's own sprites ----

    private static readonly Color ReplayRoof = new(186, 78, 78);
    private static readonly Color ReplayLedge = new(232, 168, 150);
    private static readonly Color ReplaySeam = new(150, 58, 62);
    private static readonly Color EssenceRed = new(235, 45, 45);
    private static readonly Color EssenceBlue = new(50, 120, 245);
    private static readonly Color BulletColor = new(255, 230, 120);

    /// <summary>Where he stands in a replay, and the roof line (replay units: the box at <see cref="FeedZoom"/>)</summary>
    private const float ReplayHeroX = 60f;
    private const float ReplayRoofFromBottom = 40f;

    /// <summary>
    /// A replay in the feed box: the spider sense one (he runs, the sense goes off, he jumps a
    /// pylon, a pumpkin bomb or a bullet) or the takedown one (a soldier on the roof or in the
    /// air comes at him and gets taken down, bursting into essence). Loops every few seconds
    /// </summary>
    private void DrawReplay(Camera2D camera, RectangleF box, Rectangle clip, PostedBoard board)
    {
        if (_hazardSprites == null)
            return;

        // replay units: the box at FeedZoom (so things are the same size as the live feed)
        float width = box.Width / FeedZoom;
        float roofY = box.Height / FeedZoom - ReplayRoofFromBottom;
        var scene = Matrix.CreateScale(FeedZoom) * Matrix.CreateTranslation(box.X, box.Y, 0) * camera.Transform;

        SpriteBatch.End();
        Game.GraphicsDevice.ScissorRectangle = clip;
        SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, ScissorRaster,
            null, scene);

        // the roof he's running on, scrolling
        const float roofSpeed = 140f;
        Fill(new RectangleF(-10, roofY, width + 20, ReplayRoofFromBottom + 10), ReplayRoof);
        Fill(new RectangleF(-10, roofY, width + 20, 4), ReplayLedge);
        float seamOffset = (float)(_clock * roofSpeed % 48);
        for (float x = -seamOffset; x < width + 48; x += 48)
            Fill(new RectangleF(x, roofY + 4, 2, ReplayRoofFromBottom), ReplaySeam);

        if (board.Preview == BoardPreview.SpiderSense)
            DrawSenseReplay((float)(_clock % SenseReplaySeconds), roofY, board.Variant);
        else
            DrawTakedownReplay((float)(_clock % TakedownReplaySeconds), roofY, board.Variant);

        Rebegin(camera, BlendState.AlphaBlend, SamplerState.PointClamp);
    }

    private const float SenseReplaySeconds = 3.2f;
    private const float TakedownReplaySeconds = 3.0f;

    /// <summary>Spider-Man running, or in a jump from <paramref name="jumpStart"/> lasting <paramref name="jumpSeconds"/></summary>
    private void DrawReplayHero(float t, float roofY, float jumpStart, float jumpSeconds, float jumpHeight,
        AttackKind? attack = null, float attackStart = 0)
    {
        float s = (t - jumpStart) / jumpSeconds;
        bool jumping = s is >= 0f and < 1f;
        var feet = new Vector2(ReplayHeroX, MathF.Round(roofY - (jumping ? jumpHeight * 4 * s * (1 - s) : 0)));

        if (attack is AttackKind kind && t >= attackStart && t < attackStart + 0.35f)
            DrawFrame(SpiderFrames.AttackFrame(kind, t - attackStart), feet);
        else if (!jumping)
            DrawFrame(SpiderFrames.Running[(int)(t * 12) % SpiderFrames.Running.Length], feet);
        else if (s < 0.4f)
            DrawFrame(SpiderFrames.Flip[(int)((t - jumpStart) / 0.07f) % SpiderFrames.Flip.Length], feet);
        else
            DrawFrame(s < 0.6f ? SpiderFrames.Apex : SpiderFrames.Falling, feet);
    }

    /// <summary>The spider sense replay. Variant 0: pumpkin bomb, 1: a shooter's bullet, 2: a pylon on the roof</summary>
    private void DrawSenseReplay(float t, float roofY, int variant)
    {
        const float jumpStart = 1.3f, jumpSeconds = 0.9f, jumpHeight = 85f;
        const float passes = jumpStart + jumpSeconds / 2;      // the threat goes under him at the top of the jump

        switch (variant)
        {
            case 0:
            {
                // a pumpkin bomb flying in at chest height
                float x = ReplayHeroX + (passes - t) * 170;
                SpriteBatch.Draw(_hazardSprites.Pumpkin, new Vector2(x, roofY - 26), null, Color.White, -t * 9,
                    HazardSprites.PumpkinCenter, HazardSprites.PumpkinScale, SpriteEffects.None, 0);
                break;
            }
            case 1:
            {
                // a shooter on the roof ahead, aiming; the shot comes at chest height
                var soldierFeet = new Vector2(185, roofY);
                SpriteBatch.Draw(_hazardSprites.Soldier, soldierFeet, HazardSprites.SoldierFrame(HazardSprites.StandFrame),
                    Color.White, 0, HazardSprites.SoldierFeet, HazardSprites.SoldierScale, SpriteEffects.None, 0);
                const float bulletSpeed = 200f, muzzleX = 150f;
                float fired = passes - (muzzleX - ReplayHeroX) / bulletSpeed;
                if (t >= fired)
                {
                    float x = muzzleX - (t - fired) * bulletSpeed;
                    Fill(new RectangleF(x - 5, roofY - 33, 10, 4), BulletColor);
                }
                break;
            }
            default:
            {
                // a pylon standing on the roof, coming at him with the roof
                int frame = (int)(t * 8) % HazardSprites.PylonFrames;
                float x = ReplayHeroX + (passes - t) * 140;
                SpriteBatch.Draw(_hazardSprites.Pylon, new Vector2(MathF.Round(x), roofY), HazardSprites.PylonFrame(frame),
                    Color.White, 0, HazardSprites.PylonBottom, HazardSprites.PylonScale, SpriteEffects.None, 0);
                break;
            }
        }

        DrawReplayHero(t, roofY, jumpStart, jumpSeconds, jumpHeight);

        // the spider sense goes off over his head before he jumps
        if (t is >= 0.55f and < jumpStart + 0.25f)
        {
            float seconds = t - 0.55f;
            float scale = 1.25f * MathF.Min(1, seconds / 0.08f) * (1 + 0.12f * MathF.Sin(seconds * 22));
            int frame = (int)(seconds * 14) % HazardSprites.SenseFrames;
            SpriteBatch.Draw(_hazardSprites.SpiderSense, new Vector2(ReplayHeroX, roofY - 88),
                HazardSprites.SenseFrame(frame), Color.White, 0, HazardSprites.SenseCenter, scale, SpriteEffects.None, 0);
        }
    }

    /// <summary>The takedown replay. Variant 0 (and 2): a soldier running at him on the roof, jabbed. 1: a flyer, uppercut</summary>
    private void DrawTakedownReplay(float t, float roofY, int variant)
    {
        const float hit = 1.2f;                 // when he connects
        bool flyer = variant == 1;
        float soldierY = flyer ? roofY - 105 : roofY;
        var contact = new Vector2(ReplayHeroX + (flyer ? 14 : 28), soldierY);

        if (t < hit)
        {
            // coming at him
            float x = contact.X + (hit - t) * 110;
            int frame = flyer ? HazardSprites.JumpFrame : (int)(t * 12) % HazardSprites.RunFrames;
            SpriteBatch.Draw(_hazardSprites.Soldier, new Vector2(MathF.Round(x), soldierY), HazardSprites.SoldierFrame(frame),
                Color.White, 0, HazardSprites.SoldierFeet, HazardSprites.SoldierScale, SpriteEffects.None, 0);
        }
        else if (t < hit + 0.45f)
        {
            // knocked away, spinning and flashing, like in the game
            float k = t - hit;
            var middle = contact + new Vector2(140 * k, (flyer ? -220 : -170) * k + 450 * k * k)
                         - new Vector2(0, (HazardSprites.SoldierFeet.Y - HazardSprites.SoldierMiddle.Y) * HazardSprites.SoldierScale);
            var tint = (int)(k * 30) % 2 == 0 ? Color.White : new Color(255, 120, 120);
            SpriteBatch.Draw(_hazardSprites.Soldier, middle, HazardSprites.SoldierFrame(HazardSprites.HurtFrame), tint,
                k * 12, HazardSprites.SoldierMiddle, HazardSprites.SoldierScale, SpriteEffects.None, 0);
        }
        else if (t < hit + 1.0f)
        {
            // bursts into essence
            float k = t - hit - 0.45f;
            var burst = contact + new Vector2(140 * 0.45f, (flyer ? -220 : -170) * 0.45f + 450 * 0.45f * 0.45f - 40);
            float fade = 1 - k / 0.55f;
            for (int i = 0; i < 8; i++)
            {
                float angle = i * MathF.Tau / 8;
                var at = burst + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (12 + k * 90);
                Fill(new RectangleF(at.X - 3, at.Y - 3, 6, 6), (i % 2 == 0 ? EssenceRed : EssenceBlue) * fade);
            }
        }

        // he runs into a soldier on the roof (jab), or jumps up into a flyer (uppercut)
        if (flyer)
            DrawReplayHero(t, roofY, hit - 0.4f, 0.95f, 110f, AttackKind.Uppercut, hit);
        else
            DrawReplayHero(t, roofY, -1, 0.1f, 0, AttackKind.Jab, hit - 0.05f);
    }

    /// <summary>The kicker ("LIVE"/"BREAKING"), headline and body text on the right of the screen</summary>
    private void DrawBoardText(Camera2D camera, PostedBoard board, RectangleF screen)
    {
        // scaled text needs smooth sampling to be readable
        Rebegin(camera, BlendState.AlphaBlend, SamplerState.LinearClamp);

        float line = _font.LineSpacing;

        // over the feed: what kind of news this is, beside the blinking light
        DrawBoardString(board.Kicker, new Vector2(screen.X + 24, screen.Y + 3), BoardRed, KickerScale, FeedWidth - 16);

        // the right column
        float columnX = screen.X + ColumnLeft;
        float columnWidth = screen.X + screen.Width - 8 - columnX;
        float y = screen.Y + 5;

        float headlineScale = TextLayout.FitScale(_font, board.Headline, columnWidth, HeadlineScale);
        DrawBoardString(board.Headline, new Vector2(columnX, y), BoardRed, headlineScale, columnWidth);
        y += line * headlineScale + 2;

        // a rule under the headline, like a newspaper
        Fill(new RectangleF(columnX, y, columnWidth, 2), BoardInk);
        y += 5;

        // the body, wrapped to the column; shrinks if it'd run off the bottom of the screen
        float bottom = screen.Y + screen.Height - 4;
        float scale = BodyScale;
        string body = TextLayout.Wrap(_font, board.Body, columnWidth, scale);
        while (scale > 0.2f && y + LineCount(body) * line * scale > bottom)
        {
            scale -= 0.02f;
            body = TextLayout.Wrap(_font, board.Body, columnWidth, scale);
        }
        foreach (var text in body.Split('\n'))
        {
            DrawBoardString(text, new Vector2(columnX, y), BoardInk, scale, columnWidth);
            y += line * scale;
        }

        Rebegin(camera, BlendState.AlphaBlend, SamplerState.PointClamp);
    }

    private static int LineCount(string text) => text.Split('\n').Length;

    /// <summary>Plain text (no outline: it's dark ink on a white screen), shrunk to fit if needed</summary>
    private void DrawBoardString(string text, Vector2 position, Color color, float scale, float maxWidth)
    {
        scale = TextLayout.FitScale(_font, text, maxWidth, scale);
        SpriteBatch.DrawString(_font, text, position, color, 0, Vector2.Zero, scale, SpriteEffects.None, 0);
    }
}
