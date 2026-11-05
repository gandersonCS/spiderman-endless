using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using CIS580.Input;
using CIS580.UI;
using CIS580.UI.Widgets;
using CIS580.Views;
using SpiderManEndless.Settings;
using SpiderManEndless.Views;

namespace SpiderManEndless.UI;

/// <summary>
/// The main menu. Buttons are listed on the left, and on the right Spider-Man stands on a rooftop
/// and randomly switches between idling and fighting. This layer only shows the options - the
/// state that opened it decides what each one does.
/// </summary>
/// <remarks>
/// When the game starts it opens on the title screen first (<c>showTitle</c>): the logo, with
/// Spider-Man hanging under it from a web, swinging like a pendulum, and "press any button" below.
/// Pressing anything makes him let go and leap down to his spot on the rooftop, shrinking as he
/// goes, while the menu panel slides in. It's all one layer so the skyline and Spider-Man carry
/// straight on from the title into the menu.
/// </remarks>
public class MainMenuLayer : MenuLayer
{
    // colors I pulled from the mockup
    private static readonly Color PanelColor = new(13, 19, 32);
    private static readonly Color ButtonColor = new(201, 211, 227);
    private static readonly Color HighlightColor = new(232, 72, 60);
    private static readonly Color HintColor = new(125, 138, 163);
    private static readonly Color RoofColor = new(26, 35, 54);
    private static readonly Color RoofEdgeColor = new(46, 59, 88);

    // layout (at the default 800x480 window): the panel is about half the screen, and the
    // button text is scaled down a little so the longest label fits inside it
    private const float PanelMinWidth = 384;
    private const float PanelMargin = 36;
    private const float FadeWidth = 96;
    private const float ButtonScale = 0.85f;
    private const float HintScale = 0.7f;

    // ---- title screen ----

    /// <summary>Where the menu is: the title screen, the leap from the title into the menu, or the menu itself</summary>
    private enum Phase { Title, Intro, Menu }

    private static readonly Color WebColor = new(235, 240, 245);
    private static readonly Color LogoRed = new(214, 38, 38);
    private static readonly Color LogoOutline = new(10, 12, 20);

    private const string PressText = "PRESS ANY BUTTON TO START";

    /// <summary>How long the leap from the title screen into the menu takes, in seconds</summary>
    private const float IntroSeconds = 0.95f;

    /// <summary>How long the web takes to zip back up to the logo once he lets go</summary>
    private const float WebRetractSeconds = 0.15f;

    // the pendulum: each half-swing picks a new random width (degrees) and length (seconds per full swing)
    private const float MinSwingDegrees = 7f, MaxSwingDegrees = 24f;
    private const float MinSwingPeriod = 2.2f, MaxSwingPeriod = 3.0f;

    // every gamepad button, for "press any button"
    private static readonly Buttons[] AllButtons = Enum.GetValues<Buttons>();

    private readonly Action _onTitleDismissed;
    private readonly Action _onOptionsOpened;
    private readonly Action _onOptionsClosed;
    private readonly Random _random = new();
    private Phase _phase;
    private Texture2D _logo;           // Textures/title_logo, if it exists (otherwise the title is drawn as text)
    private float _introTime;
    private float _releaseAngle;       // the web's angle when he let go
    private float _swingPhase;         // where in the swing he is (radians: 0 = middle, swinging right)
    private float _swingPeriod = 2.6f;
    private float _swingDegrees;       // how wide the swing is right now
    private float _swingTarget;        // ...and how wide it's easing toward

    // little description for each button, shown under the list (same order as the buttons)
    private static readonly string[] Hints =
    {
        "Start a new run",
        "Your stats, Spider-Essence and costumes",
        "Audio, controls, display",
        "Quit to desktop",
    };

    private readonly Action _onExit;
    private readonly MenuSpiderMan _spiderMan = new();
    private readonly float[] _fade;
    private Texture2D _skyline;
    private Texture2D _spidey;
    private SaveFile _save;
    private Costume _costume;
    private SpriteFont _buttonFont;
    private float _clock;

    /// <param name="onPlay">Runs when someone picks Run, and gets told who picked it</param>
    /// <param name="onExit">Called when the player confirms they want to exit</param>
    /// <param name="showTitle">Start on the title screen ("press any button") instead of the menu</param>
    /// <param name="onTitleDismissed">Called when someone presses a button on the title screen</param>
    /// <param name="onOptionsOpened">Called when the Options menu opens</param>
    /// <param name="onOptionsClosed">Called once the Options menu has closed</param>
    public MainMenuLayer(Action<InputSource> onPlay, Action onExit, bool showTitle = false,
        Action onTitleDismissed = null, Action onOptionsOpened = null, Action onOptionsClosed = null)
        : base("")
    {
        _onExit = onExit;
        _onTitleDismissed = onTitleDismissed;
        _onOptionsOpened = onOptionsOpened;
        _onOptionsClosed = onOptionsClosed;
        _phase = showTitle ? Phase.Title : Phase.Menu;
        FitToScreen = false;

        Menu.Add(new ButtonItem("Run", onPlay))
            .Add(new ButtonItem("The Closet", _ => Open(new ClosetLayer())))
            .Add(new ButtonItem("Options", _ => OpenOptions()))
            .Add(new ButtonItem("Exit Game", OnCancel));

        _fade = new float[Menu.Items.Count];

        // start mid-swing so he isn't hanging dead still on the first frame
        _swingDegrees = _swingTarget = RandomBetween(MinSwingDegrees, MaxSwingDegrees);
        _swingPhase = RandomBetween(0, MathF.Tau);
    }

    private void OpenOptions()
    {
        _onOptionsOpened?.Invoke();
        var options = Open(new OptionsLayer());
        options.Removed += () => _onOptionsClosed?.Invoke();
    }

    protected override void Load()
    {
        base.Load();
        _skyline = Game.Content.Load<Texture2D>("Textures/background_tileset_city_free");
        _save = Game.Services.GetService<SaveFile>();
        LoadCostume();
        _buttonFont = Game.Content.Load<SpriteFont>("Fonts/buttonfont");    // Action Comics Black

        // the title logo. until there's art for it the title is drawn as text: drop a
        // Textures/title_logo.png in Assets and it gets used automatically
        try
        {
            _logo = Game.Content.Load<Texture2D>("Textures/title_logo");
        }
        catch (ContentLoadException)
        {
            _logo = null;
        }
    }

    /// <summary>
    /// Loads the sprite sheet for whichever suit is equipped (it can change in the Closet)
    /// </summary>
    private void LoadCostume()
    {
        _costume = Costumes.Equipped(_save);
        _spidey = Game.Content.Load<Texture2D>(_costume.SpriteSheet);
    }

    /// <summary>
    /// Cancelling the main menu asks the player to confirm they want to exit
    /// </summary>
    protected override void OnCancel(InputSource source)
    {
        var prompt = new PromptLayer("Are you sure you want to exit?");
        prompt.Accepted += _ => _onExit();
        Open(prompt);
    }

    public override void HandleInput(GameTime gameTime)
    {
        switch (_phase)
        {
            case Phase.Title:
                if (AnyButtonPressed())
                    StartIntro();
                break;
            case Phase.Intro:
                // pressing again skips the leap
                if (AnyButtonPressed())
                    FinishIntro();
                break;
            default:
                base.HandleInput(gameTime);
                break;
        }
    }

    /// <summary>
    /// True if any key, mouse button or gamepad button was pressed this frame
    /// </summary>
    private bool AnyButtonPressed()
    {
        var now = Input.Current;
        var before = Input.Previous;

        if (now.Keyboard.GetPressedKeys().Any(key => before.Keyboard.IsKeyUp(key)))
            return true;

        if (Game.IsActive && (Input.WasLeftClicked || Input.WasRightClicked))
            return true;

        for (int pad = 0; pad < InputState.MaxGamePads; pad++)
        {
            if (!now.GamePad(pad).IsConnected)
                continue;
            foreach (var button in AllButtons)
            {
                if (Input.WasButtonPressed(button, pad))
                    return true;
            }
        }
        return false;
    }

    /// <summary>He lets go of the web and leaps down to the menu</summary>
    private void StartIntro()
    {
        _phase = Phase.Intro;
        _introTime = 0;
        _releaseAngle = SwingAngle;
        Theme.Sounds.Play(UiSound.Confirm);
        _onTitleDismissed?.Invoke();
    }

    /// <summary>He's landed: the menu takes over</summary>
    private void FinishIntro()
    {
        _phase = Phase.Menu;
        _spiderMan.Land();
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);
        _clock = (float)gameTime.TotalGameTime.TotalSeconds;
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

        switch (_phase)
        {
            case Phase.Title:
                UpdateSwing(dt);
                break;
            case Phase.Intro:
                _introTime += dt;
                if (_introTime >= IntroSeconds)
                    FinishIntro();
                break;
            default:
                _spiderMan.Update(gameTime);
                break;
        }

        if (_save != null && Costumes.Equipped(_save) != _costume)
            LoadCostume();

        // highlight fades in/out as you move the selection
        float step = (float)gameTime.ElapsedGameTime.TotalSeconds * 8;
        for (int i = 0; i < _fade.Length; i++)
        {
            bool selected = HasFocus && _phase == Phase.Menu && i == Menu.SelectedIndex;
            _fade[i] = Math.Clamp(_fade[i] + (selected ? step : -step), 0f, 1f);
        }
    }

    public override void Draw(GameTime gameTime)
    {
        var viewport = Game.GraphicsDevice.Viewport;
        float width = viewport.Width, height = viewport.Height;
        float alpha = Transition.Alpha;

        // menu on the left half, Spider-Man's stage on the right
        float panelWidth = MathF.Max(PanelMinWidth, width * 0.48f);    // wide enough for the button font
        float roofTop = MathF.Round(height * 0.82f);
        float stageCenter = panelWidth + (width - panelWidth) / 2;

        var feet = new Vector2(MathF.Round(stageCenter), roofTop);
        var title = TitleLayout(width, height);

        // how much of the title screen and of the menu panel are showing (0 to 1)
        float titleShown, panelShown;
        switch (_phase)
        {
            case Phase.Title:
                titleShown = 1;
                panelShown = 0;
                break;
            case Phase.Intro:
                float u = IntroProgress;
                titleShown = 1 - SmoothStep(u / 0.4f);
                panelShown = SmoothStep((u - 0.3f) / 0.7f);
                break;
            default:
                titleShown = 0;
                panelShown = 1;
                break;
        }

        // --- pixel art (point sampling so it doesn't get blurry) ---
        SpriteBatch.Begin(samplerState: SamplerState.PointClamp);

        DrawSkyline(width, height, alpha);
        Fill(new RectangleF(0, roofTop, width, height - roofTop), RoofColor * alpha);
        Fill(new RectangleF(0, roofTop, width, 4), RoofEdgeColor * alpha);
        switch (_phase)
        {
            case Phase.Title:
                DrawHanging(title, alpha);
                break;
            case Phase.Intro:
                DrawLeap(title, feet, height, alpha);
                break;
            default:
                DrawSpiderMan(feet, height, alpha);
                break;
        }

        SpriteBatch.End();

        // --- title, menu panel + text ---
        SpriteBatch.Begin();

        if (titleShown > 0)
            DrawTitleScreen(title, width, height, alpha * titleShown);

        if (panelShown <= 0)
        {
            SpriteBatch.End();
            return;
        }
        alpha *= panelShown;

        // panel slides in from the left when the menu opens (or as he leaps in from the title)
        float slide = -MathF.Max(Transition.Offset, (1 - panelShown) * (1 - panelShown)) * panelWidth;
        DrawPanel(panelWidth, height, slide, alpha);
        // the buttons can reach a little way into the panel's fade, but no further
        float contentWidth = panelWidth - PanelMargin - FadeWidth / 2;
        DrawLogo(new RectangleF(PanelMargin + slide, height * 0.08f, contentWidth, height * 0.17f), alpha);
        DrawButtons(new Vector2(PanelMargin + slide, height * 0.33f), contentWidth, alpha);

        SpriteBatch.End();
    }

    private void DrawSkyline(float width, float height, float alpha)
    {
        // skyline image is 320x192. scale it to the screen height, tile it horizontally, and scroll
        // it slowly so the menu doesn't look static
        float scale = height / _skyline.Height;
        float tileWidth = _skyline.Width * scale;
        float offset = _clock * 12 % tileWidth;
        var tint = new Color(alpha * 0.8f, alpha * 0.8f, alpha * 0.9f);    // darken it a bit so it looks like night

        for (float x = -offset; x < width; x += tileWidth)
            SpriteBatch.Draw(_skyline, new Vector2(MathF.Round(x), 0), null, tint, 0, Vector2.Zero, scale,
                SpriteEffects.None, 0);
    }

    /// <summary>
    /// His size on the menu: whole number scales so the pixels stay square (3x at the default 800x480)
    /// </summary>
    private static float MenuScale(float height) => MathF.Max(2, MathF.Round(height / 160));

    private void DrawSpiderMan(Vector2 feet, float height, float alpha)
    {
        float scale = MenuScale(height);
        var frame = _spiderMan.CurrentFrame;

        // soft shadow under his feet
        var shadowSize = new Vector2(30 * scale, 3 * scale);
        Fill(new RectangleF(feet.X - shadowSize.X / 2, feet.Y - shadowSize.Y / 2, shadowSize.X, shadowSize.Y),
            Color.Black * (0.35f * alpha));

        SpriteBatch.Draw(_spidey, feet, frame.Source, Color.White * alpha, 0, frame.Origin, scale,
            SpriteEffects.None, 0);
    }

    private void DrawPanel(float panelWidth, float height, float slide, float alpha)
    {
        // solid color behind the buttons, then fades out over the skyline
        const float fadeWidth = FadeWidth;
        float solid = panelWidth - fadeWidth;
        Fill(new RectangleF(slide, 0, solid, height), PanelColor * alpha);

        const int steps = 24;
        float stepWidth = fadeWidth / steps;
        for (int i = 0; i < steps; i++)
        {
            float opacity = 1 - (i + 0.5f) / steps;
            Fill(new RectangleF(slide + solid + i * stepWidth, 0, stepWidth + 1, height), PanelColor * (opacity * alpha));
        }
    }

    private void DrawButtons(Vector2 topLeft, float width, float alpha)
    {
        var font = _buttonFont;
        float rowHeight = font.LineSpacing * ButtonScale + 26;    // room for the tall capitals
        var position = topLeft;

        // remember where each button is so they can be clicked
        Menu.ClearItemBounds();

        // one text size for every button: the preferred size, or smaller if the longest label wouldn't fit
        float scale = ButtonScale;
        foreach (var item in Menu.Items)
            scale = MathF.Min(scale, TextLayout.FitScale(font, item.Text.ToUpperInvariant(), width - 26 - 12, ButtonScale));

        for (int i = 0; i < Menu.Items.Count; i++)
        {
            var item = Menu.Items[i];
            float fade = _fade[i];

            // selected button shifts right a bit and gets a tinted row + red marker
            float nudge = 8 * fade;
            var row = new RectangleF(position.X + nudge, position.Y, width, rowHeight - 6);
            Menu.SetItemBounds(item, new Rectangle((int)position.X, (int)row.Y, (int)(width + 8), (int)row.Height));
            Fill(row, HighlightColor * (0.16f * fade * alpha));
            Fill(new RectangleF(row.X + 8, row.Y + 8, 5, row.Height - 16), HighlightColor * (fade * alpha));

            var color = !item.IsEnabled ? Theme.DisabledColor : Color.Lerp(ButtonColor, Color.White, fade);
            // The button font only has capital letters
            // (centered on the letters themselves: this font's capitals are taller than its line height)
            string label = item.Text.ToUpperInvariant();
            var textPosition = new Vector2(MathF.Round(row.X + 26), TextLayout.CenteredTop(font, label, row.Y + row.Height / 2, scale));
            SpriteBatch.DrawString(font, label, textPosition, color * alpha, 0, Vector2.Zero, scale, SpriteEffects.None, 0);

            position.Y += rowHeight;
        }

        // description of whatever button is selected (wrapped so it stays inside the panel)
        int selected = Menu.SelectedIndex;
        if (selected >= 0 && selected < Hints.Length)
        {
            var hintFont = Theme.TextFont;
            string hint = TextLayout.Wrap(hintFont, Hints[selected], width - 8, HintScale);
            var hintPosition = new Vector2(MathF.Round(topLeft.X + 8), MathF.Round(position.Y + 6));
            SpriteBatch.DrawString(hintFont, hint, hintPosition, HintColor * alpha, 0, Vector2.Zero, HintScale,
                SpriteEffects.None, 0);
        }
    }

    // ======================= title screen =======================

    /// <summary>Where things go on the title screen (worked out from the window size each frame)</summary>
    private readonly record struct TitleSpots(
        RectangleF Logo, Vector2 Anchor, float RopeLength, float Scale, float PressY);

    private TitleSpots TitleLayout(float width, float height)
    {
        // logo across the top, web hanging from the middle of its bottom edge, "press any button" near the bottom
        float logoHeight = height * 0.25f;
        var logo = new RectangleF(width * 0.15f, height * 0.04f, width * 0.7f, logoHeight);
        var anchor = new Vector2(MathF.Round(width / 2), MathF.Round(logo.Y + logoHeight - height * 0.02f));

        // twice his size on the menu (6x at 800x480)
        float scale = MenuScale(height) * 2;
        return new TitleSpots(logo, anchor, height * 0.1f, scale, height * 0.88f);
    }

    /// <summary>How far through the leap from the title into the menu he is, 0 to 1</summary>
    private float IntroProgress => Math.Clamp(_introTime / IntroSeconds, 0f, 1f);

    /// <summary>The web's angle from straight down right now (radians, positive = swung to the right)</summary>
    private float SwingAngle => MathHelper.ToRadians(_swingDegrees) * MathF.Sin(_swingPhase);

    /// <summary>
    /// Swings him back and forth. It's a pendulum whose swing gets wider or narrower (and a bit
    /// faster or slower) at random every time he passes through the middle, so it never settles
    /// into the exact same rhythm
    /// </summary>
    private void UpdateSwing(float dt)
    {
        int halfSwing = (int)(_swingPhase / MathF.PI);
        _swingPhase += dt * MathF.Tau / _swingPeriod;
        if ((int)(_swingPhase / MathF.PI) != halfSwing)
        {
            _swingTarget = RandomBetween(MinSwingDegrees, MaxSwingDegrees);
            _swingPeriod = RandomBetween(MinSwingPeriod, MaxSwingPeriod);
        }
        if (_swingPhase > MathF.Tau * 1000)       // keep the number small
            _swingPhase -= MathF.Tau * 1000;

        _swingDegrees += (_swingTarget - _swingDegrees) * MathF.Min(1, dt * 1.2f);
    }

    /// <summary>The end of the web (where his feet are) for a web at this angle</summary>
    private static Vector2 RopeEnd(TitleSpots title, float angle)
        => title.Anchor + new Vector2(MathF.Sin(angle), MathF.Cos(angle)) * title.RopeLength;

    /// <summary>The middle of his body while hanging from a web at this angle</summary>
    private static Vector2 HangingCenter(TitleSpots title, float angle)
    {
        var frame = MenuSpiderFrames.WebDrop;
        var fromOrigin = (new Vector2(frame.Source.Width, frame.Source.Height) / 2 - frame.Origin) * title.Scale;
        return RopeEnd(title, angle) + Vector2.Transform(fromOrigin, Matrix.CreateRotationZ(-angle));
    }

    /// <summary>Title screen: hanging upside down from the logo by a web</summary>
    private void DrawHanging(TitleSpots title, float alpha)
    {
        float angle = SwingAngle;
        var end = RopeEnd(title, angle);
        DrawWeb(title.Anchor, end, alpha);

        // the sprite turns with the web (MonoGame rotates clockwise, the web angle goes counterclockwise)
        var frame = MenuSpiderFrames.WebDrop;
        SpriteBatch.Draw(_spidey, end, frame.Source, Color.White * alpha, -angle, frame.Origin, title.Scale,
            SpriteEffects.None, 0);
    }

    /// <summary>
    /// The leap from the title into the menu: the web zips back up, and he flips through the air
    /// in an arc down to his spot on the roof, shrinking to his menu size on the way
    /// </summary>
    private void DrawLeap(TitleSpots title, Vector2 feet, float height, float alpha)
    {
        // web zips back up to the logo
        float retract = Math.Clamp(_introTime / WebRetractSeconds, 0f, 1f);
        if (retract < 1)
            DrawWeb(title.Anchor, Vector2.Lerp(RopeEnd(title, _releaseAngle), title.Anchor, retract), alpha);

        float u = IntroProgress;
        float scale = MathHelper.Lerp(title.Scale, MenuScale(height), SmoothStep(u));

        // his body's center moves in a straight line from where he hung to where he'll stand,
        // plus a hop up and over (a parabola), like a jump
        var start = HangingCenter(title, _releaseAngle);
        var falling = SpiderFrames.Falling;
        var end = feet - new Vector2(0, falling.Source.Height / 2f * MenuScale(height));
        var center = Vector2.Lerp(start, end, u) - new Vector2(0, height * 0.22f * 4 * u * (1 - u));

        // tucked flip for most of it, then straightens out to drop onto the roof
        SpiderFrames.Frame frame;
        if (u < 0.7f)
            frame = SpiderFrames.Flip[(int)(_introTime / 0.06f) % SpiderFrames.Flip.Length];
        else if (u < 0.82f)
            frame = SpiderFrames.Apex;
        else
            frame = falling;

        // drawn around the middle of the frame so swapping frames doesn't make him jump around
        var middle = new Vector2(frame.Source.Width, frame.Source.Height) / 2;
        SpriteBatch.Draw(_spidey, center, frame.Source, Color.White * alpha, 0, middle, scale,
            SpriteEffects.None, 0);
    }

    private void DrawWeb(Vector2 from, Vector2 to, float alpha)
    {
        DrawLine(from, to, Color.Black * (0.35f * alpha), 5);
        DrawLine(from, to, WebColor * alpha, 2.5f);
    }

    private void DrawLine(Vector2 from, Vector2 to, Color color, float width)
    {
        var d = to - from;
        if (d.LengthSquared() < 0.01f)
            return;
        SpriteBatch.Draw(Stack.BlankTexture, from, null, color, MathF.Atan2(d.Y, d.X),
            new Vector2(0, 0.5f), new Vector2(d.Length(), width), SpriteEffects.None, 0);
    }

    /// <summary>The logo and the blinking "press any button" (they fade and drift up as the menu comes in)</summary>
    private void DrawTitleScreen(TitleSpots title, float width, float height, float alpha)
    {
        float rise = (1 - alpha) * height * 0.08f;
        var logo = title.Logo with { Y = title.Logo.Y - rise };

        DrawLogo(logo, alpha);

        // "press any button", gently pulsing (it vanishes quickly once something's pressed)
        float pulse = _phase == Phase.Title ? 0.55f + 0.45f * MathF.Sin(_clock * 4f) : 1f;
        var font = _buttonFont;
        float textScale = TextLayout.FitScale(font, PressText, width * 0.8f, 0.7f);
        var textSize = font.MeasureString(PressText) * textScale;
        var textPosition = new Vector2(MathF.Round((width - textSize.X) / 2),
            TextLayout.CenteredTop(font, PressText, title.PressY - rise, textScale));
        float pressAlpha = alpha * alpha * pulse;
        DrawOutlinedText(font, PressText, textPosition, textScale, Color.White * pressAlpha, LogoOutline * pressAlpha, 2);
    }

    /// <summary>
    /// The logo, fit inside the box (keeping its shape) and centered along the box's bottom edge,
    /// which is where the web hangs from on the title screen
    /// </summary>
    private void DrawLogo(RectangleF box, float alpha)
    {
        if (_logo == null)
        {
            DrawTextLogo(box, alpha);
            return;
        }

        float scale = MathF.Min(box.Width / _logo.Width, box.Height / _logo.Height);
        var size = new Vector2(_logo.Width, _logo.Height) * scale;
        var position = new Vector2(box.X + (box.Width - size.X) / 2, box.Y + box.Height - size.Y);
        SpriteBatch.Draw(_logo, position, null, Color.White * alpha, 0, Vector2.Zero, scale, SpriteEffects.None, 0);
    }

    /// <summary>
    /// Stand-in logo until there's art: "SPIDER-MAN" in the Amazing Spider-Man font, "ENDLESS" under it
    /// </summary>
    private void DrawTextLogo(RectangleF box, float alpha)
    {
        const string top = "SPIDER-MAN", bottom = "ENDLESS";

        // each line as big as fits: "SPIDER-MAN" gets the full width and ~60% of the height,
        // "ENDLESS" 60% of the width and ~30% of the height
        var titleFont = Theme.TitleFont;
        var (topInkTop, topInkBottom) = TextLayout.InkExtent(titleFont, top);
        float topScale = MathF.Min(TextLayout.FitScale(titleFont, top, box.Width, 4f),
            box.Height * 0.6f / (topInkBottom - topInkTop));
        float topWidth = titleFont.MeasureString(top).X * topScale;

        var font = _buttonFont;
        var (bottomInkTop, bottomInkBottom) = TextLayout.InkExtent(font, bottom);
        float bottomScale = MathF.Min(TextLayout.FitScale(font, bottom, box.Width * 0.6f, 4f),
            box.Height * 0.3f / (bottomInkBottom - bottomInkTop));
        float bottomWidth = font.MeasureString(bottom).X * bottomScale;

        // stack the two lines (by their visible pixels) and sit them on the bottom of the box
        float gap = box.Height * 0.04f;
        float topInk = (topInkBottom - topInkTop) * topScale;
        float bottomInk = (bottomInkBottom - bottomInkTop) * bottomScale;
        float inkTop = box.Y + box.Height - (topInk + gap + bottomInk);

        var topPosition = new Vector2(MathF.Round(box.X + (box.Width - topWidth) / 2), MathF.Round(inkTop - topInkTop * topScale));
        var bottomPosition = new Vector2(MathF.Round(box.X + (box.Width - bottomWidth) / 2),
            MathF.Round(inkTop + topInk + gap - bottomInkTop * bottomScale));

        DrawOutlinedText(titleFont, top, topPosition, topScale, LogoRed * alpha, LogoOutline * alpha, 3);
        DrawOutlinedText(font, bottom, bottomPosition, bottomScale, Color.White * alpha, LogoOutline * alpha, 3);
    }

    private void DrawOutlinedText(SpriteFont font, string text, Vector2 position, float scale, Color color,
        Color outline, float thickness)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx != 0 || dy != 0)
                    SpriteBatch.DrawString(font, text, position + new Vector2(dx, dy) * thickness, outline, 0,
                        Vector2.Zero, scale, SpriteEffects.None, 0);
            }
        }
        SpriteBatch.DrawString(font, text, position, color, 0, Vector2.Zero, scale, SpriteEffects.None, 0);
    }

    private static float SmoothStep(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3 - 2 * t);
    }

    private float RandomBetween(float min, float max) => min + (float)_random.NextDouble() * (max - min);

    private void Fill(RectangleF area, Color color)
        => SpriteBatch.Draw(Stack.BlankTexture, new Vector2(area.X, area.Y), null, color, 0, Vector2.Zero,
            new Vector2(area.Width, area.Height), SpriteEffects.None, 0);
}
