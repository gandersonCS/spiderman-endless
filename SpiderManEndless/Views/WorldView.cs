using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using CIS580.UI;
using CIS580.Views;
using SpiderManEndless.Settings;
using SpiderManEndless.Simulation;

namespace SpiderManEndless.Views;

/// <summary>
/// Draws everything in the runner: parallax skyline (<see cref="CityBackdrop"/>), the scrolling tile rooftops, essence orbs,
/// Spider-Man (plus his web when swinging), the distance/essence counters, and the end screen.
/// The hazards (soldiers, pylons, bombs, bullets, spider sense, combo) are in WorldView.Hazards.cs.
/// </summary>
/// <remarks>
/// This is purely a view: it reads the <see cref="GameSession"/> and draws it, but never changes
/// it. The camera doesn't move - the <see cref="RooftopTrack"/> scrolls instead, so everything gets
/// drawn at (track position - scroll distance).
/// </remarks>
public partial class WorldView : WorldLayer
{
    private static readonly RectangleF WorldArea = new(0, 0, World.Width, World.Height);

    // city tileset = grid of 16px tiles, referenced by (column, row)
    private const int Src = 16;
    private static Rectangle Tile(int column, int row) => new(column * Src, row * Src, Src, Src);

    // each building style is a 3-column panel in the tileset. buildings use 3-slice drawing:
    // left column for the left edge, right column for the right edge, middle column repeated in
    // between. rows are: roof, the row under the roof, and a plain body row.
    private readonly record struct Panel(int FirstColumn, int RoofRow, int SecondRow, int BodyRow);
    private static readonly Panel[] Panels =
    {
        new(2, 1, 2, 2),     // Blue:  columns 2-4
        new(8, 1, 2, 5),     // Green: columns 8-10
        new(12, 1, 2, 5),    // Red:   columns 12-14
    };

    // window is 2x2 tiles (32x32px) in the tileset
    private static readonly Rectangle WindowSource = new(32, 160, 32, 32);

    // 1 tileset pixel = 2 world units, so a 16px tile = TileSize (32)
    private const float PixelScale = RooftopTrack.TileSize / (float)Src;

    private static readonly Color RopeColor = new(235, 240, 245);
    private static readonly Color BuildingOutline = new(8, 8, 14);
    private static readonly Color RoofRim = new Color(255, 225, 200) * 0.45f;
    private static readonly Color NewBestColor = new(255, 220, 70);
    private static readonly Color SavedColor = new(150, 235, 160);
    private static readonly Color NotSavedColor = new(255, 140, 130);
    private static readonly Color CantAffordColor = new(255, 140, 130);
    private static readonly Color ContinueColor = new(120, 215, 255);
    private static readonly Color EndPanelColor = new(13, 19, 32);
    private static readonly Color AccentColor = new(232, 72, 60);

    // text sizes (scales of the game font), picked so everything is readable at 800x480
    private const float TitleScale = 1.15f;
    private const float DistanceScale = 0.85f;
    private const float BestScale = 0.6f;
    private const float SummaryScale = 0.55f;
    private const float SmallScale = 0.45f;
    private const float ButtonTextScale = 0.5f;

    // end-of-run panel and its buttons (world units, which are pixels at the default window size)
    private const float EndPanelWidth = 600;
    private const float EndPanelPadding = 16;
    private const float ButtonHeight = 42;
    private const float ContinueButtonWidth = 300;
    private const float MenuButtonWidth = 180;

    // orb settings in world units: core radius + bob/spin
    private const float OrbRadius = 5f;
    private const float OrbBob = 3f;
    private const float OrbSpinSpeed = 4f;

    // small burst effect when you grab an orb
    private const float BurstSeconds = 0.35f;
    private readonly record struct Burst(double X, float Y, float Start);
    private readonly List<Burst> _bursts = new();

    // essence icon next to the counter (under the distance)
    private static readonly Vector2 EssenceIconCenter = new(26, 70);
    private const float EssenceIconRadius = 7f;

    private readonly GameSession _session;
    private readonly SpriteFont _font;
    private readonly Texture2D _tileset;
    private readonly CityBackdrop _backdrop;
    private readonly Texture2D _spidey;
    private readonly Texture2D _attackOverlay;
    private readonly SaveFile _save;
    private readonly HazardSprites _hazardSprites;
    private readonly Func<bool> _canContinue;
    private readonly Func<bool> _buttonsActive;
    private Rectangle? _continueButton;
    private Rectangle? _menuButton;
    private EssenceArt _essenceArt;
    private RunRecord? _runRecord;
    private Runner _recordedRunner;
    private Runner _backdropRunner;     // run the backdrop's start was picked for
    private RooftopTrack _backdropTrack; // that run's track, to see how far it got
    private float _clock;

    /// <param name="session">The game to draw</param>
    /// <param name="font">The font to draw with</param>
    /// <param name="tileset">Textures/tileset_city_free</param>
    /// <param name="backdrop">The parallax city behind the rooftops (day/dusk/night)</param>
    /// <param name="spidey">Textures/spiderman (sprite sheet with the background removed)</param>
    /// <param name="save">The player's progress, for showing how much Spider-Essence they have toward a continue</param>
    /// <param name="canContinue">Whether the player can continue right now (has continues left and can pay)</param>
    /// <param name="buttonsActive">Whether the end screen's buttons can be clicked right now (e.g. not while paused)</param>
    /// <param name="hazardSprites">Sprites for the soldiers, pylons, pumpkin bombs and spider sense (hazards aren't drawn without them)</param>
    /// <param name="attackOverlay">The equipped suit's attack overlay (e.g. Iron Spider's spider arms), drawn behind him while he attacks</param>
    public WorldView(GameSession session, SpriteFont font, Texture2D tileset, CityBackdrop backdrop, Texture2D spidey,
        SaveFile save = null, Func<bool> canContinue = null, Func<bool> buttonsActive = null,
        HazardSprites hazardSprites = null, Texture2D attackOverlay = null)
    {
        _attackOverlay = attackOverlay;
        _hazardSprites = hazardSprites;
        _canContinue = canContinue ?? (() => session.World.CanContinue);
        _buttonsActive = buttonsActive ?? (() => true);
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _font = font;
        _tileset = tileset;
        _backdrop = backdrop;
        _spidey = spidey;
        _save = save;

        // a new game (from the menu) starts at a random time of day. retries carry on from where
        // the last run ended (see Update)
        _backdrop?.RandomizeStart();
        _backdropRunner = session.World.Runner;
        _backdropTrack = session.World.Track;

        // fills anything outside the world; follows the time of day (see Update)
        BackgroundColor = backdrop?.FillColor(0) ?? Color.Black;

        Transition.OnDuration = TimeSpan.FromSeconds(1.5);
        Transition.OffDuration = TimeSpan.FromSeconds(0.5);
    }

    protected override void Load()
    {
        base.Load();
        _essenceArt = EssenceArt.For(Game.GraphicsDevice);
    }

    /// <summary>
    /// Where the end screen's Continue button is on screen, or null if it isn't showing.
    /// (It shows greyed out when the player can't continue; check that before acting on a click.)
    /// </summary>
    public Rectangle? ContinueButton => EndScreenShowing ? _continueButton : null;

    /// <summary>
    /// Where the end screen's Main Menu button is on screen, or null if it isn't showing
    /// </summary>
    public Rectangle? MenuButton => EndScreenShowing ? _menuButton : null;

    /// <summary>
    /// The end screen waits this long after he dies, so you can see what got him first
    /// (it's pointed out with a red ring)
    /// </summary>
    public const float EndScreenDelay = 0.7f;

    private bool EndScreenShowing
        => _session.World.Runner.State == RunnerState.Dead && _session.World.Runner.DeadSeconds >= EndScreenDelay;

    /// <summary>
    /// Handles sim events: a burst where essence was picked up, plus the hazard effects
    /// (hit flashes, explosions, dust, popups). (boss flash would go here later)
    /// </summary>
    public void OnGameEvent(GameEvent gameEvent)
    {
        if (gameEvent is EssenceCollected collected)
            _bursts.Add(new Burst(collected.X, collected.Y, _clock));
        OnHazardEvent(gameEvent);
    }

    /// <summary>
    /// Opens a popup over the world (like the continue prompt). It belongs to the world view,
    /// so it closes along with it
    /// </summary>
    public T ShowPopup<T>(T popup) where T : Layer => Open(popup);

    /// <summary>
    /// Shows the save results (new best, essence banked) on the end-of-run screen until the next
    /// run starts
    /// </summary>
    public void ShowRunRecord(RunRecord record)
    {
        _runRecord = record;
        _recordedRunner = _session.World.Runner;
    }

    public override void Update(GameTime gameTime)
    {
        _clock = (float)gameTime.TotalGameTime.TotalSeconds;
        _bursts.RemoveAll(b => _clock - b.Start > BurstSeconds);
        UpdateHazardEffects();

        // new run started (or he continued), clear the old summary
        if (_runRecord != null &&
            (_session.World.Runner != _recordedRunner || _session.World.Runner.State != RunnerState.Dead))
            _runRecord = null;

        // retried after dying: the world made a new runner + track, so start the new run on the
        // time of day the old one ended on (the old track still knows how far it got)
        if (_backdrop != null && _session.World.Runner != _backdropRunner)
        {
            _backdrop.ContinueFrom(_backdropTrack.DistanceMeters);
            _backdropRunner = _session.World.Runner;
            _backdropTrack = _session.World.Track;
        }

        if (_backdrop != null)
            BackgroundColor = _backdrop.FillColor(_session.World.Track.DistanceMeters);

        // pointing at a button that can be clicked shows the hand pointer
        if (IsHovered(MenuButton) || (_canContinue() && IsHovered(ContinueButton)))
            Stack.RequestHandCursor();

        base.Update(gameTime);
    }

    protected override void UpdateCameras(GameTime gameTime)
    {
        // camera stays still and fits the whole world in the window
        foreach (var camera in Cameras)
            camera.FitToArea(WorldArea);
    }

    protected override void DrawWorld(Camera2D camera, GameTime gameTime)
    {
        // pixel art can't be smoothed, so restart the batch with point sampling
        SpriteBatch.End();
        SpriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: camera.Transform);

        // visible area (can be bigger than the world if the window aspect ratio is different)
        float halfWidth = camera.Viewport.Width / 2f / camera.Zoom;
        float halfHeight = camera.Viewport.Height / 2f / camera.Zoom;
        float left = camera.Position.X - halfWidth;
        float right = camera.Position.X + halfWidth;
        float bottom = camera.Position.Y + halfHeight;

        var track = _session.World.Track;
        var runner = _session.World.Runner;
        var hazards = _session.World.Hazards;

        _backdrop?.Draw(SpriteBatch, track.ScrollX, track.DistanceMeters, left, right);
        DrawNewsBoards(camera, track, runner, left, right, bottom);     // behind the roofs
        DrawBuildings(track, left, right, bottom);
        DrawDropShadows(hazards, track);
        DrawHazards(camera, hazards, track, left, right);
        DrawEssence(camera, track, left, right);
        DrawHazardGlows(camera, hazards, track, left, right);
        DrawSpiderMan(track, runner);
        DrawInvulnerableGlow(camera, hazards, runner);
        DrawBullets(camera, hazards, track);
        DrawEffects(camera, track);
        DrawSpiderSense(hazards, runner);
        DrawWhatGotHim(camera, hazards, track, runner);
        DrawOffscreenMarker(hazards, runner);
        DrawText(camera, track, runner);
    }

    /// <summary>
    /// Ends the batch and starts a new one with the camera transform. Used to switch between
    /// pixel art (point sampling) and the smooth glowy orbs (linear sampling)
    /// </summary>
    private void Rebegin(Camera2D camera, BlendState blend, SamplerState sampler)
    {
        SpriteBatch.End();
        SpriteBatch.Begin(blendState: blend, samplerState: sampler, transformMatrix: camera.Transform);
    }

    private void DrawEssence(Camera2D camera, RooftopTrack track, float left, float right)
    {
        var orbs = _session.World.Essence.Orbs;
        if (_essenceArt == null || (orbs.Count == 0 && _bursts.Count == 0))
            return;

        // glows + collect bursts first (these brighten what's behind them)...
        Rebegin(camera, BlendState.Additive, SamplerState.LinearClamp);
        foreach (var orb in orbs)
        {
            if (OrbOnScreen(orb, track, left, right, out var center, out float spin))
                _essenceArt.DrawGlow(SpriteBatch, center, OrbRadius, spin, pulse: 1 + 0.12f * MathF.Sin(_clock * 6 + spin));
        }
        foreach (var burst in _bursts)
        {
            float t = (_clock - burst.Start) / BurstSeconds;
            var center = new Vector2((float)(burst.X - track.ScrollX), burst.Y);
            _essenceArt.DrawGlow(SpriteBatch, center, OrbRadius * (1 + 2.5f * t), _clock * 12, alpha: 1 - t);
        }

        // ...then draw the actual orbs on top
        Rebegin(camera, BlendState.AlphaBlend, SamplerState.LinearClamp);
        foreach (var orb in orbs)
        {
            if (OrbOnScreen(orb, track, left, right, out var center, out float spin))
                _essenceArt.DrawCores(SpriteBatch, center, OrbRadius, spin);
        }

        // switch back to pixel art for Spider-Man
        Rebegin(camera, BlendState.AlphaBlend, SamplerState.PointClamp);
    }

    /// <summary>
    /// Gets where to draw an orb (with the bob) and its spin, returns false if it's off screen
    /// </summary>
    private bool OrbOnScreen(EssenceOrb orb, RooftopTrack track, float left, float right, out Vector2 center, out float spin)
    {
        float x = (float)(orb.X - track.ScrollX);

        // give each orb its own phase based on position so a pack shimmers instead of moving in sync
        float phase = (float)(orb.X * 0.05 % (2 * Math.PI));
        spin = _clock * OrbSpinSpeed + phase;
        center = new Vector2(x, orb.Y + OrbBob * MathF.Sin(_clock * 3 + phase));
        return x > left - 20 && x < right + 20;
    }

    private void DrawBuildings(RooftopTrack track, float left, float right, float bottom)
    {
        const int size = RooftopTrack.TileSize;
        int lastRow = (int)MathF.Ceiling(bottom / size);

        foreach (var building in track.Buildings)
        {
            // building's left edge on screen, rounded to whole units so we don't get tiny gaps
            // between tiles
            float x0 = MathF.Round((float)(building.StartColumn * (double)size - track.ScrollX));
            if (x0 > right || x0 + building.WidthTiles * size < left)
                continue;

            var panel = Panels[(int)building.Style];

            // only draw columns that are on screen
            int firstColumn = Math.Max(0, (int)MathF.Floor((left - x0) / size));
            int lastColumn = Math.Min(building.WidthTiles - 1, (int)MathF.Ceiling((right - x0) / size));

            for (int column = firstColumn; column <= lastColumn; column++)
            {
                // pick left edge / middle / right edge of the panel
                int panelColumn = panel.FirstColumn + (column == 0 ? 0 : column == building.WidthTiles - 1 ? 2 : 1);

                for (int row = building.TopRow; row <= lastRow; row++)
                {
                    int panelRow = row == building.TopRow ? panel.RoofRow
                                 : row == building.TopRow + 1 ? panel.SecondRow
                                 : panel.BodyRow;

                    var destination = new Rectangle((int)x0 + column * size, row * size, size, size);
                    SpriteBatch.Draw(_tileset, destination, Tile(panelColumn, panelRow), Color.White);
                }
            }

            foreach (var window in building.Windows)
            {
                var destination = new Rectangle((int)x0 + window.Column * size, window.Row * size, size * 2, size * 2);
                SpriteBatch.Draw(_tileset, destination, WindowSource, Color.White);
            }

            DrawBuildingOutline(x0, building.WidthTiles * size, building.TopRow * size, lastRow * size + size);
        }
    }

    /// <summary>
    /// A heavy dark outline around the roof and walls of a building you can land on, with a pale
    /// rim along the roof edge, so the roofs stand out from the city in the background
    /// </summary>
    private void DrawBuildingOutline(float x, float width, float roofY, float bottom)
    {
        const float thickness = 3;
        Fill(new RectangleF(x - 1, roofY - 1, width + 2, thickness), BuildingOutline);              // roof
        Fill(new RectangleF(x - 1, roofY - 1, thickness, bottom - roofY + 1), BuildingOutline);     // left wall
        Fill(new RectangleF(x + width - 2, roofY - 1, thickness, bottom - roofY + 1), BuildingOutline); // right wall
        Fill(new RectangleF(x + 2, roofY + 2, width - 4, 2), RoofRim);                              // lit roof edge
    }

    private void DrawLine(Vector2 from, Vector2 to, Color color, float width)
    {
        var d = to - from;
        SpriteBatch.Draw(Stack.BlankTexture, from, null, color, MathF.Atan2(d.Y, d.X),
            new Vector2(0, 0.5f), new Vector2(d.Length(), width), SpriteEffects.None, 0);
    }

    private void DrawSpiderMan(RooftopTrack track, Runner runner)
    {
        _spideyTint = SpideyTint(_session.World.Hazards);
        _drawnSpidey.Clear();
        var feet = new Vector2(RooftopTrack.RunnerX, MathF.Round(runner.FeetY));

        // attacking on a roof or in the air replaces the normal pose (swinging is handled below,
        // since the web still gets drawn)
        if (runner.IsAttacking && runner.State is RunnerState.Running or RunnerState.Airborne)
        {
            DrawAttack(SpiderFrames.AttackFrame(runner.Attack, runner.AttackSeconds), feet);
            return;
        }

        switch (runner.State)
        {
            case RunnerState.Running:
            {
                // run animation is tied to the ground: next frame every X distance traveled, so his legs
                // speed up as the city speeds up
                const double unitsPerFrame = 30;
                int index = (int)(track.ScrollX / unitsPerFrame) % SpiderFrames.Running.Length;
                DrawFrame(SpiderFrames.Running[index], feet);
                break;
            }

            case RunnerState.Airborne:
            {
                if (runner.VelocityY < -250)
                {
                    // going up: do a flip
                    int index = (int)(runner.AirSeconds / 0.07f) % SpiderFrames.Flip.Length;
                    DrawFrame(SpiderFrames.Flip[index], feet);
                }
                else if (runner.VelocityY < 250)
                    DrawFrame(SpiderFrames.Apex, feet);
                else
                    DrawFrame(SpiderFrames.Falling, feet);
                break;
            }

            case RunnerState.Swinging:
            {
                var anchor = runner.Anchor.Value;
                var hand = new Vector2(RooftopTrack.RunnerX, runner.HandY);
                var anchorOnScreen = new Vector2((float)(anchor.X - track.ScrollX), anchor.Y);

                // web shoots out to the anchor quickly, then he hangs from it. pose depends on the
                // rope angle (or he kicks while hanging on)
                var tip = Vector2.Lerp(hand, anchorOnScreen, MathF.Min(1f, runner.SwingSeconds / 0.07f));
                DrawLine(hand, tip, Color.Black * 0.35f, 5);
                DrawLine(hand, tip, RopeColor, 2.5f);
                if (runner.IsAttacking)
                    DrawAttack(SpiderFrames.AttackFrame(runner.Attack, runner.AttackSeconds), feet);
                else
                    DrawFrame(SpiderFrames.SwingFor(runner.RopeAngleDegrees(track)), hand);
                break;
            }

            case RunnerState.Dead:
                DrawFrame(SpiderFrames.Dead, feet);
                break;
        }
    }

    /// <summary>An attack frame, with the suit's attack overlay (spider arms) behind him if it has one</summary>
    private void DrawAttack(SpiderFrames.Frame frame, Vector2 position)
    {
        if (_attackOverlay != null && SpiderArms.TryGetCell(frame, out var armSource, out var armOrigin))
            SpriteBatch.Draw(_attackOverlay, position, armSource, _spideyTint, 0, armOrigin,
                PixelScale, SpriteEffects.None, 0);
        DrawFrame(frame, position);
    }

    private void DrawFrame(SpiderFrames.Frame frame, Vector2 position, float rotation = 0)
    {
        SpriteBatch.Draw(_spidey, position, frame.Source, _spideyTint, rotation, frame.Origin,
            PixelScale, SpriteEffects.None, 0);
        _drawnSpidey.Add((frame, position, rotation));     // so the invulnerable glow can trace him
    }

    private void DrawText(Camera2D camera, RooftopTrack track, Runner runner)
    {
        if (_font == null)
            return;

        // text is drawn scaled, so it needs smooth (linear) sampling or it gets jagged and hard to read
        Rebegin(camera, BlendState.AlphaBlend, SamplerState.LinearClamp);

        DrawOutlined($"{track.DistanceMeters:0} m", new Vector2(16, 8), Vector2.Zero, Color.White, 0.9f);

        // essence count for this run + icon, right below the distance
        int essence = _session.World.Essence.CollectedThisRun;
        var countPosition = new Vector2(EssenceIconCenter.X + EssenceIconRadius + 10, EssenceIconCenter.Y);
        DrawOutlined(essence.ToString(), countPosition, new Vector2(0, _font.LineSpacing / 2f), Color.White, 0.65f);
        DrawEssenceIcon(camera, EssenceIconCenter, EssenceIconRadius);
        DrawCombo(_session.World.Hazards);
        DrawPopups(track);
        DrawAdaptCallout();

        _continueButton = null;
        _menuButton = null;
        if (EndScreenShowing)
            DrawEndScreen(camera, track, runner);

        Rebegin(camera, BlendState.AlphaBlend, SamplerState.PointClamp);
    }

    /// <summary>
    /// The end-of-run screen: a panel with why the run ended, how far it got, the save summary,
    /// and buttons to continue (with what it costs) or go back to the main menu. The buttons
    /// can be clicked; the game state checks <see cref="ContinueButton"/> and <see cref="MenuButton"/>.
    /// </summary>
    private void DrawEndScreen(Camera2D camera, RooftopTrack track, Runner runner)
    {
        const float centerX = World.Width / 2;
        float line = _font.LineSpacing;
        var world = _session.World;
        int have = _save?.Data.SpiderEssence ?? 0;
        bool canContinue = _canContinue();

        // work out the panel's height first, so it can be centered on screen
        var record = _runRecord;
        float contentHeight = line * TitleScale + line * DistanceScale
                              + (record != null ? line * (BestScale + SummaryScale + SmallScale) : 0)
                              + 14 + ButtonHeight + 10 + line * SmallScale + 4 + line * SmallScale;
        var panel = new RectangleF(centerX - EndPanelWidth / 2, MathF.Round((World.Height - contentHeight) / 2 - EndPanelPadding),
            EndPanelWidth, contentHeight + EndPanelPadding * 2);

        Fill(panel, EndPanelColor * 0.82f);
        Fill(new RectangleF(panel.X, panel.Y, panel.Width, 3), AccentColor);

        float y = panel.Y + EndPanelPadding;
        string why = _deathLine ?? DeathLines[0];
        y = DrawTextLine(why, y, Color.White, TitleScale);
        y = DrawTextLine($"{track.DistanceMeters:0} m", y, Color.White, DistanceScale);

        if (record is RunRecord saved)
        {
            if (saved.NewHighScore)
                y = DrawTextLine("NEW BEST!", y, NewBestColor, BestScale);
            else
                y = DrawTextLine($"Best: {saved.HighScoreMeters} m", y, Color.White * 0.85f, BestScale);

            // e.g. "+12 Spider-Essence (340 total)" with the icon in front
            string text = $"+{saved.EssenceCollected} Spider-Essence  ({saved.TotalEssence} total)";
            float textWidth = _font.MeasureString(text).X * SummaryScale;
            const float iconRadius = 6f, gap = 10f;
            float startX = centerX - (iconRadius * 2 + gap + textWidth) / 2;
            float middle = y + line * SummaryScale / 2;
            DrawCentered(text, new Vector2(startX + iconRadius * 2 + gap + textWidth / 2, middle), Color.White, SummaryScale);
            DrawEssenceIcon(camera, new Vector2(startX + iconRadius, middle), iconRadius);
            y += line * SummaryScale;

            y = DrawTextLine(saved.Saved ? "Progress saved" : "Couldn't save progress", y,
                saved.Saved ? SavedColor : NotSavedColor, SmallScale);
        }

        // the two buttons, side by side
        y += 14;
        const float buttonGap = 20;
        float left = centerX - (ContinueButtonWidth + buttonGap + MenuButtonWidth) / 2;
        var continueArea = new RectangleF(left, y, ContinueButtonWidth, ButtonHeight);
        var menuArea = new RectangleF(left + ContinueButtonWidth + buttonGap, y, MenuButtonWidth, ButtonHeight);
        _continueButton = ToScreen(camera, continueArea);
        _menuButton = ToScreen(camera, menuArea);

        string continueLabel = world.CanContinue ? $"CONTINUE  -  {world.NextContinueCost} ESSENCE" : "NO CONTINUES LEFT";
        DrawButton(continueArea, continueLabel, ContinueColor, canContinue, IsHovered(_continueButton));
        DrawButton(menuArea, "MAIN MENU", Color.White, enabled: true, IsHovered(_menuButton));
        y += ButtonHeight + 10;

        // what the keys do, then how many continues are left / why you can't continue
        y = DrawTextLine(canContinue ? "SPACE / A: continue      ESC: main menu" : "ESC: main menu", y,
            Color.White * 0.75f, SmallScale);
        y += 4;
        if (!world.CanContinue)
            DrawTextLine("No continues left this run", y, CantAffordColor, SmallScale);
        else if (_save != null && have < world.NextContinueCost)
            DrawTextLine($"Not enough Spider-Essence to continue ({have} / {world.NextContinueCost})", y, CantAffordColor, SmallScale);
        else
            DrawTextLine($"{world.ContinuesLeft} of {World.MaxContinues} continues left   |   You have {have} Spider-Essence",
                y, Color.White * 0.8f, SmallScale);
    }

    private void DrawButton(RectangleF area, string label, Color textColor, bool enabled, bool hovered)
    {
        hovered &= enabled;
        Fill(area, (hovered ? AccentColor * 0.45f : Color.Black * 0.45f));
        var edge = !enabled ? Color.Gray * 0.5f : hovered ? AccentColor : Color.White * 0.55f;
        Fill(new RectangleF(area.X, area.Y, area.Width, 2), edge);
        Fill(new RectangleF(area.X, area.Y + area.Height - 2, area.Width, 2), edge);
        Fill(new RectangleF(area.X, area.Y, 2, area.Height), edge);
        Fill(new RectangleF(area.X + area.Width - 2, area.Y, 2, area.Height), edge);

        // centered on the letters themselves, so it sits in the middle of the button
        var color = !enabled ? Color.Gray : hovered ? Color.White : textColor;
        float scale = TextLayout.FitScale(_font, label, area.Width - 16, ButtonTextScale);
        float width = _font.MeasureString(label).X * scale;
        var position = new Vector2(MathF.Round(area.X + (area.Width - width) / 2),
            TextLayout.CenteredTop(_font, label, area.Y + area.Height / 2, scale));
        DrawOutlined(label, position, Vector2.Zero, color, scale);
    }

    /// <summary>
    /// Draws a centered line of text with its top at <paramref name="top"/>, returning where the next line goes
    /// </summary>
    private float DrawTextLine(string text, float top, Color color, float scale)
    {
        float height = _font.LineSpacing * scale;
        DrawCentered(text, new Vector2(World.Width / 2, top + height / 2), color, scale, maxWidth: EndPanelWidth - 32);
        return top + height;
    }

    private bool IsHovered(Rectangle? button)
        => button is Rectangle bounds && _buttonsActive() && Game.IsActive && bounds.Contains(Input.MousePosition);

    private static Rectangle ToScreen(Camera2D camera, RectangleF area)
    {
        var topLeft = camera.WorldToScreen(new Vector2(area.X, area.Y));
        var bottomRight = camera.WorldToScreen(new Vector2(area.X + area.Width, area.Y + area.Height));
        return new Rectangle((int)topLeft.X, (int)topLeft.Y, (int)(bottomRight.X - topLeft.X), (int)(bottomRight.Y - topLeft.Y));
    }

    private void Fill(RectangleF area, Color color)
        => SpriteBatch.Draw(Stack.BlankTexture, new Vector2(area.X, area.Y), null, color, 0, Vector2.Zero,
            new Vector2(area.Width, area.Height), SpriteEffects.None, 0);

    /// <summary>
    /// Draws the essence icon (pair of slowly spinning orbs). Puts the batch back the way the
    /// text drawing uses it afterwards (linear sampled, alpha blended).
    /// </summary>
    private void DrawEssenceIcon(Camera2D camera, Vector2 center, float radius)
    {
        if (_essenceArt == null)
            return;

        float spin = _clock * 2;
        Rebegin(camera, BlendState.Additive, SamplerState.LinearClamp);
        _essenceArt.DrawGlow(SpriteBatch, center, radius, spin, alpha: 0.9f);
        Rebegin(camera, BlendState.AlphaBlend, SamplerState.LinearClamp);
        _essenceArt.DrawCores(SpriteBatch, center, radius, spin);
    }

    /// <summary>
    /// Draws text centered on a point, with a dark outline. It shrinks if it would be wider than
    /// <paramref name="maxWidth"/> (by default, the screen minus a margin), so it never runs off screen.
    /// </summary>
    private void DrawCentered(string text, Vector2 position, Color color, float scale, float maxWidth = World.Width - 40)
    {
        scale = TextLayout.FitScale(_font, text, maxWidth, scale);
        DrawOutlined(text, position, _font.MeasureString(text) / 2, color, scale);
    }

    private void DrawOutlined(string text, Vector2 position, Vector2 origin, Color color, float scale)
    {
        // dark outline so text is readable on any background
        foreach (var offset in new[] { new Vector2(-2, 0), new Vector2(2, 0), new Vector2(0, -2), new Vector2(0, 2) })
            SpriteBatch.DrawString(_font, text, position + offset, Color.Black * 0.7f, 0, origin, scale, SpriteEffects.None, 0);
        SpriteBatch.DrawString(_font, text, position, color, 0, origin, scale, SpriteEffects.None, 0);
    }
}

/// <summary>
/// Frame locations on Textures/spiderman.png. Each rect is cropped tight around a frame, so
/// the widths aren't all the same.
/// </summary>
public static class SpiderFrames
{
    /// <summary>A frame plus the point in it (sheet pixels) that lines up with the draw position</summary>
    public readonly record struct Frame(Rectangle Source, Vector2 Origin)
    {
        /// <summary>Origin at bottom center (his feet)</summary>
        public static Frame Feet(int x, int y, int width, int height)
            => new(new Rectangle(x, y, width, height), new Vector2(width / 2f, height));

        /// <summary>Origin at the center (for the flip)</summary>
        public static Frame Middle(int x, int y, int width, int height)
            => new(new Rectangle(x, y, width, height), new Vector2(width / 2f, height / 2f + 14));

        /// <summary>Origin at top center (where the web attaches)</summary>
        public static Frame Hanging(int x, int y, int width, int height)
            => new(new Rectangle(x, y, width, height), new Vector2(width / 2f, 0));
    }

    /// <summary>Sheet y of his feet in the running frames</summary>
    private const int RunBaseline = 364;

    private static Frame Run(int x, int y, int width, int height)
        => new(new Rectangle(x, y, width, height), new Vector2(width / 2f, RunBaseline - y));

    // all use the same baseline so his feet don't bob up and down mid-stride
    public static readonly Frame[] Running =
    {
        Run(3, 330, 40, 34), Run(59, 334, 34, 30), Run(106, 336, 44, 28),
        Run(166, 333, 54, 30), Run(233, 333, 48, 30), Run(295, 334, 33, 30),
        Run(342, 334, 44, 30), Run(397, 334, 52, 30), Run(466, 332, 46, 31),
    };

    public static readonly Frame[] Flip =
    {
        Frame.Middle(16, 511, 24, 21), Frame.Middle(67, 491, 21, 24), Frame.Middle(126, 482, 24, 21),
    };

    public static readonly Frame Apex = Frame.Feet(172, 493, 39, 34);
    public static readonly Frame Falling = Frame.Feet(230, 509, 27, 32);
    public static readonly Frame Dead = Frame.Middle(242, 2533, 35, 29);

    // ---- attacks ----
    // These frames are lined up by his eyes (the light blue lens pixels on the sheet) instead of
    // their bounding boxes, so his head stays put while his arms/legs swing around.

    /// <summary>Sheet y of his feet in the jab frames</summary>
    private const int JabBaseline = 1213;

    /// <summary>How far his eyes are in front of the feet origin while running (sheet px), so the jab lines up with the run</summary>
    private const float RunEyeAhead = 16;

    /// <summary>Where his eyes sit relative to his feet in the jump frames (sheet px), so the kicks line up with the jump</summary>
    private const float AirEyeAhead = 2;
    private const float AirEyeAbove = 26;

    /// <summary>Jab frame: feet on the baseline, eyes where they are when running</summary>
    private static Frame Jab(int x, int y, int width, int height, int eyeX)
        => new(new Rectangle(x, y, width, height), new Vector2(eyeX - x - RunEyeAhead, JabBaseline - y));

    /// <summary>Kick frame: placed so his eyes are where they'd be in a normal jump frame</summary>
    private static Frame Kick(int x, int y, int width, int height, int eyeX, int eyeY)
        => new(new Rectangle(x, y, width, height), new Vector2(eyeX - x - AirEyeAhead, eyeY - y + AirEyeAbove));

    /// <summary>"Jab Punch" on the sheet: crouch, lunge + punch, recover</summary>
    public static readonly Frame[] JabPunch =
    {
        Jab(8, 1179, 36, 34, 36), Jab(61, 1180, 50, 34, 92), Jab(123, 1180, 49, 34, 154),
        Jab(181, 1180, 48, 33, 213), Jab(240, 1181, 41, 32, 272), Jab(295, 1183, 37, 30, 325),
        Jab(346, 1180, 30, 33, 373),
    };

    /// <summary>"Upward Kick" on the sheet: tuck, kick straight up (with the swoosh), come back down</summary>
    public static readonly Frame[] UpwardKick =
    {
        Kick(12, 1423, 35, 38, 31, 1438), Kick(68, 1412, 34, 28, 85, 1416), Kick(128, 1392, 41, 50, 143, 1405),
        Kick(209, 1383, 35, 58, 223, 1396), Kick(276, 1386, 31, 54, 289, 1400), Kick(337, 1409, 40, 38, 351, 1415),
        Kick(405, 1424, 32, 39, 421, 1434),
    };

    /// <summary>"Air Kick" on the sheet: wind-up (with the swoosh), then the dive kick he holds</summary>
    public static readonly Frame[] AirKick =
    {
        Kick(422, 1162, 47, 47, 439, 1171), Kick(484, 1160, 46, 49, 501, 1170),
    };

    /// <summary>
    /// "Uppercut" on the sheet, the rising part: only played when he runs into a soldier in the
    /// air (it's not on a button). Anchored at his feet
    /// </summary>
    public static readonly Frame[] Uppercut =
    {
        Frame.Feet(144, 1542, 34, 63), Frame.Feet(206, 1525, 28, 75), Frame.Feet(267, 1525, 29, 65),
        Frame.Feet(328, 1515, 30, 68), Frame.Feet(391, 1510, 30, 75), Frame.Feet(454, 1515, 31, 72),
        Frame.Feet(510, 1529, 29, 66),
    };

    /// <summary>How long the air kick's first frame shows before he holds the kick pose</summary>
    private const float AirKickWindUpSeconds = 0.08f;

    /// <summary>
    /// The frame for an attack this far in. Jab and upward kick spread their frames evenly over
    /// the attack's length (see <see cref="Runner.DurationOf"/>), the air kick holds its last frame
    /// </summary>
    public static Frame AttackFrame(AttackKind kind, float seconds)
    {
        if (kind == AttackKind.AirKick)
            return AirKick[seconds < AirKickWindUpSeconds ? 0 : 1];

        var frames = kind switch
        {
            AttackKind.Jab => JabPunch,
            AttackKind.Uppercut => Uppercut,
            _ => UpwardKick,
        };
        float progress = seconds / Runner.DurationOf(kind);
        int index = Math.Clamp((int)(progress * frames.Length), 0, frames.Length - 1);
        return frames[index];
    }

    // swinging poses, ordered from rope pointing way ahead (first) to straight up/behind (last)
    private static readonly Frame[] Swing =
    {
        Frame.Hanging(221, 1974, 45, 35),
        Frame.Hanging(295, 2268, 42, 34),
        Frame.Hanging(459, 2268, 42, 34),
        Frame.Hanging(44, 2427, 43, 33),
    };

    /// <summary>Picks the swing pose for a rope angle from straight up (positive = anchor ahead)</summary>
    public static Frame SwingFor(float ropeAngleDegrees)
    {
        if (ropeAngleDegrees > 30) return Swing[0];
        if (ropeAngleDegrees > 15) return Swing[1];
        if (ropeAngleDegrees > 0) return Swing[2];
        return Swing[3];
    }
}
