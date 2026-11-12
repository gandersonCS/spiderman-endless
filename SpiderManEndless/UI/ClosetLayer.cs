using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using CIS580.Input;
using CIS580.UI;
using CIS580.UI.Widgets;
using CIS580.Views;
using SpiderManEndless.Settings;
using SpiderManEndless.Views;

namespace SpiderManEndless.UI;

/// <summary>
/// The Closet screen. Shows saved stats (best run, last run, essence), with Outfits and Powerups
/// buttons under the stats box and Back at the bottom.
/// </summary>
/// <remarks>
/// Stats are read from the <see cref="SaveFile"/> service, which saves after every run and every
/// purchase, so they're always current. The Outfits/Powerups buttons are plain boxes for now -
/// swap <see cref="DrawShopButton"/> for the real icon art when it's ready.
/// </remarks>
public class ClosetLayer : MenuLayer
{
    public const string ComingSoon = "More outfits and powerups coming soon!";

    // same colors as the main menu
    internal static readonly Color PanelColor = new(13, 19, 32);
    internal static readonly Color AccentColor = new(232, 72, 60);
    internal static readonly Color LabelColor = new(125, 138, 163);
    internal static readonly Color ValueColor = new(235, 240, 248);

    private const float PanelWidth = 440;
    private const float RowHeight = 46;
    private const float EssenceIconRadius = 8;
    private const float ShopButtonHeight = 44;
    private const float ShopButtonGap = 16;

    // menu order: the two shop buttons side by side, then Back
    private const int OutfitsIndex = 0;
    private const int PowerupsIndex = 1;
    private const int BackIndex = 2;

    private readonly float[] _fade = new float[3];
    private SaveFile _save;
    private EssenceArt _essenceArt;
    private float _clock;

    public ClosetLayer() : base("The Closet")
    {
        DimBackground = true;
        FitToScreen = false;
        Menu.Add(new ButtonItem("Outfits", _ => Open(new OutfitsLayer())))
            .Add(new ButtonItem("Powerups", _ => Open(new PowerupsLayer())))
            .Add(new ButtonItem("Back", _ => Close()));
    }

    protected override void Load()
    {
        base.Load();
        _save = Game.Services.GetService<SaveFile>();
        _essenceArt = EssenceArt.For(Game.GraphicsDevice);
    }

    public override void HandleInput(GameTime gameTime)
    {
        // Outfits and Powerups sit next to each other, so left/right hops between them
        int selected = Menu.SelectedIndex;
        if (selected == OutfitsIndex || selected == PowerupsIndex)
        {
            int target = -1;
            if (Ui.WasPressed(UiControl.Left)) target = OutfitsIndex;
            else if (Ui.WasPressed(UiControl.Right)) target = PowerupsIndex;
            if (target >= 0 && target != selected)
            {
                Menu.SelectedIndex = target;
                Theme.Sounds.Play(UiSound.Move);
            }
        }

        base.HandleInput(gameTime);
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);
        _clock = (float)gameTime.TotalGameTime.TotalSeconds;

        float step = (float)gameTime.ElapsedGameTime.TotalSeconds * 8;
        for (int i = 0; i < _fade.Length; i++)
        {
            bool isSelected = HasFocus && i == Menu.SelectedIndex;
            _fade[i] = Math.Clamp(_fade[i] + (isSelected ? step : -step), 0f, 1f);
        }
    }

    public override void Draw(GameTime gameTime)
    {
        var viewport = Game.GraphicsDevice.Viewport;
        float centerX = viewport.Width / 2f;
        float alpha = Transition.Alpha;
        float slide = Transition.Offset * 60;     // panel slides up as the closet opens

        DrawBackdrop(gameTime);

        var data = _save?.Data ?? new SaveData();
        bool hasPlayed = _save?.HasPlayed ?? false;

        // stats panel sits between the title and the shop buttons
        float panelTop = Theme.TitleTop + 48 + slide;
        var panel = new RectangleF(centerX - PanelWidth / 2, panelTop, PanelWidth, RowHeight * 3 + 20);
        var essenceIconCenter = new Vector2(panel.X + 28 + EssenceIconRadius, panel.Y + 10 + RowHeight * 2.5f);

        // Outfits + Powerups, side by side under the stats box, same width as it
        float buttonTop = panel.Y + panel.Height + 14;
        float buttonWidth = (PanelWidth - ShopButtonGap) / 2;
        var outfitsButton = new RectangleF(panel.X, buttonTop, buttonWidth, ShopButtonHeight);
        // var powerupsButton = new RectangleF(panel.X + buttonWidth + ShopButtonGap, buttonTop, buttonWidth, ShopButtonHeight);
        float noteTop = buttonTop + ShopButtonHeight + 12;
        float backTop = MathF.Max(noteTop + 34, viewport.Height - 70);

        SpriteBatch.Begin();

        Fill(panel, PanelColor * (0.88f * alpha));
        Fill(new RectangleF(panel.X, panel.Y, 4, panel.Height), AccentColor * alpha);

        DrawRow(panel, 0, "Best Run", hasPlayed ? $"{data.HighScoreMeters} m" : "--", 0, alpha);
        DrawRow(panel, 1, "Last Run", hasPlayed ? $"{data.LastScoreMeters} m" : "--", 0, alpha);
        DrawRow(panel, 2, "Spider-Essence", data.SpiderEssence.ToString(), EssenceIconRadius * 2 + 10, alpha);

        // thin lines between rows
        for (int i = 1; i < 3; i++)
            Fill(new RectangleF(panel.X + 20, panel.Y + 10 + RowHeight * i, panel.Width - 40, 1), LabelColor * (0.3f * alpha));

        Menu.ClearItemBounds();
        DrawShopButton(OutfitsIndex, outfitsButton, alpha);
        // DrawShopButton(PowerupsIndex, powerupsButton, alpha);

        var font = Theme.TextFont;
        var noteSize = font.MeasureString(ComingSoon) * 0.6f;
        SpriteBatch.DrawString(font, ComingSoon, new Vector2(centerX - noteSize.X / 2, noteTop),
            Theme.DisabledColor * alpha, 0, Vector2.Zero, 0.6f, SpriteEffects.None, 0);

        DrawBackButton(new Vector2(centerX, backTop), alpha, gameTime);

        // title slides down while the menu transitions in, same as the other menus
        var titleFont = Theme.TitleFont;
        var titlePosition = new Vector2(centerX, Theme.TitleTop - Transition.Offset * 100);
        SpriteBatch.DrawString(titleFont, Title, titlePosition, Theme.TitleColor * alpha, 0,
            titleFont.MeasureString(Title) / 2, Theme.TitleScale, SpriteEffects.None, 0);

        SpriteBatch.End();

        DrawEssenceIcon(essenceIconCenter, alpha);
    }

    /// <summary>
    /// A boxed button (placeholder until the icon buttons/backdrop art is ready)
    /// </summary>
    private void DrawShopButton(int index, RectangleF area, float alpha)
    {
        var item = Menu.Items[index];
        float fade = _fade[index];

        Fill(area, Color.Lerp(PanelColor, new Color(40, 24, 30), fade) * (0.9f * alpha));
        DrawOutline(area, Color.Lerp(LabelColor * 0.5f, AccentColor, fade) * alpha, fade > 0.5f ? 2 : 1);

        var font = Theme.TextFont;
        string text = item.Text;
        var size = font.MeasureString(text);
        var color = Color.Lerp(Theme.TextColor, Theme.SelectedColor, fade);
        SpriteBatch.DrawString(font, text, new Vector2(area.X + area.Width / 2, area.Y + area.Height / 2),
            color * alpha, 0, size / 2, 0.9f, SpriteEffects.None, 0);

        Menu.SetItemBounds(item, new Rectangle((int)area.X, (int)area.Y, (int)area.Width, (int)area.Height));
    }

    private void DrawBackButton(Vector2 topCenter, float alpha, GameTime gameTime)
    {
        var item = Menu.Items[BackIndex];
        var font = Theme.TextFont;
        float fade = _fade[BackIndex];
        float pulse = (float)Math.Sin(gameTime.TotalGameTime.TotalSeconds * 6) + 1;
        string text = item.Text;
        var size = font.MeasureString(text);
        var center = new Vector2(topCenter.X, topCenter.Y + font.LineSpacing / 2f);

        SpriteBatch.DrawString(font, text, center, Color.Lerp(Theme.TextColor, Theme.SelectedColor, fade) * alpha,
            0, size / 2, 1 + pulse * 0.05f * fade, SpriteEffects.None, 0);

        float clickWidth = Math.Max(size.X, 120) + 40;
        Menu.SetItemBounds(item, new Rectangle((int)(center.X - clickWidth / 2), (int)topCenter.Y,
            (int)clickWidth, font.LineSpacing));
    }

    /// <summary>
    /// A single stats row - label on the left, value on the right
    /// </summary>
    private void DrawRow(RectangleF panel, int row, string label, string value, float labelIndent, float alpha)
    {
        var font = Theme.TextFont;
        float middle = panel.Y + 10 + RowHeight * (row + 0.5f);

        var labelOrigin = new Vector2(0, font.MeasureString(label).Y / 2);
        SpriteBatch.DrawString(font, label, new Vector2(panel.X + 28 + labelIndent, middle), LabelColor * alpha, 0,
            labelOrigin, 1f, SpriteEffects.None, 0);

        const float valueScale = 1.2f;
        var valueSize = font.MeasureString(value);
        SpriteBatch.DrawString(font, value, new Vector2(panel.X + panel.Width - 24, middle), ValueColor * alpha, 0,
            new Vector2(valueSize.X, valueSize.Y / 2), valueScale, SpriteEffects.None, 0);
    }

    private void DrawEssenceIcon(Vector2 center, float alpha)
    {
        if (_essenceArt == null)
            return;

        float spin = _clock * 2;
        SpriteBatch.Begin(blendState: BlendState.Additive, samplerState: SamplerState.LinearClamp);
        _essenceArt.DrawGlow(SpriteBatch, center, EssenceIconRadius, spin, alpha * 0.9f);
        SpriteBatch.End();

        SpriteBatch.Begin(samplerState: SamplerState.LinearClamp);
        _essenceArt.DrawCores(SpriteBatch, center, EssenceIconRadius, spin, alpha);
        SpriteBatch.End();
    }

    private void DrawOutline(RectangleF area, Color color, float thickness)
    {
        Fill(new RectangleF(area.X, area.Y, area.Width, thickness), color);
        Fill(new RectangleF(area.X, area.Y + area.Height - thickness, area.Width, thickness), color);
        Fill(new RectangleF(area.X, area.Y, thickness, area.Height), color);
        Fill(new RectangleF(area.X + area.Width - thickness, area.Y, thickness, area.Height), color);
    }

    private void Fill(RectangleF area, Color color)
        => SpriteBatch.Draw(Stack.BlankTexture, new Vector2(area.X, area.Y), null, color, 0, Vector2.Zero,
            new Vector2(area.Width, area.Height), SpriteEffects.None, 0);
}
