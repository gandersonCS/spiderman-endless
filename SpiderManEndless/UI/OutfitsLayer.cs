using System;
using System.Collections.Generic;
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
/// The Outfits screen (opened from the Closet). Every costume is a card in a row: its artwork next
/// to how it looks in game, then its name, first appearance and price, then a Buy / Equip /
/// Equipped button.
/// </summary>
/// <remarks>
/// Left/right picks a card, Confirm presses its button, Down goes to Back. The mouse can hover a
/// card to pick it and click its button. Buying asks to confirm first, then spends the essence and
/// saves; it doesn't equip the suit, the button just turns into Equip.
/// </remarks>
public class OutfitsLayer : MenuLayer
{
    private const float CardWidth = 300;
    private const float CardHeight = 316;
    private const float CardGap = 24;
    private const float Padding = 12;
    private const float PortraitWidth = 128;
    private const float PortraitHeight = 160;
    private const float ButtonHeight = 34;
    private const float SpriteScale = 2;
    private const float RunFrameSeconds = 0.07f;
    private const float EssenceIconRadius = 7;
    private const float ToastSeconds = 2.5f;

    private static readonly Color CardColor = new(17, 24, 40);
    private static readonly Color StageColor = new(8, 11, 20);
    private static readonly Color EquippedColor = new(90, 200, 120);

    private readonly IReadOnlyList<Costume> _costumes = Costumes.All;
    private readonly Dictionary<Costume, Texture2D> _portraits = new();
    private readonly Dictionary<Costume, Texture2D> _sheets = new();
    private readonly Rectangle[] _cardBounds;
    private readonly Rectangle[] _buttonBounds;
    private readonly float[] _cardFade;
    private readonly List<(Vector2 Center, float Alpha)> _essenceIcons = new();

    private SaveFile _save;
    private EssenceArt _essenceArt;
    private int _selected;
    private bool _backFocused;
    private float _backFade;
    private float _scroll;
    private float _clock;
    private string _toast;
    private float _toastLeft;

    public OutfitsLayer() : base("Outfits")
    {
        DimBackground = true;
        FitToScreen = false;
        Menu.Add(new ButtonItem("Back", _ => Close()));

        _cardBounds = new Rectangle[_costumes.Count];
        _buttonBounds = new Rectangle[_costumes.Count];
        _cardFade = new float[_costumes.Count];
    }

    protected override void Load()
    {
        base.Load();
        _save = Game.Services.GetService<SaveFile>();
        _essenceArt = EssenceArt.For(Game.GraphicsDevice);
        foreach (var costume in _costumes)
        {
            _portraits[costume] = Game.Content.Load<Texture2D>(costume.Portrait);
            _sheets[costume] = Game.Content.Load<Texture2D>(costume.SpriteSheet);
        }

        // start on the suit being worn
        var equipped = Costumes.Equipped(_save);
        for (int i = 0; i < _costumes.Count; i++)
        {
            if (_costumes[i] == equipped)
                _selected = i;
        }
    }

    // ---------------------------------------------------------------- input

    public override void HandleInput(GameTime gameTime)
    {
        if (HandleMouse())
            return;

        if (Ui.WasPressed(UiControl.Cancel, out var cancelSource))
        {
            Theme.Sounds.Play(UiSound.Cancel);
            OnCancel(cancelSource);
            return;
        }

        if (_backFocused)
        {
            if (Ui.WasPressed(UiControl.Up))
            {
                _backFocused = false;
                Theme.Sounds.Play(UiSound.Move);
            }
            else if (Ui.WasPressed(UiControl.Confirm))
            {
                Theme.Sounds.Play(UiSound.Cancel);
                Close();
            }
            return;
        }

        if (Ui.WasPressed(UiControl.Left) && _selected > 0)
        {
            _selected--;
            Theme.Sounds.Play(UiSound.Move);
        }
        if (Ui.WasPressed(UiControl.Right) && _selected < _costumes.Count - 1)
        {
            _selected++;
            Theme.Sounds.Play(UiSound.Move);
        }
        if (Ui.WasPressed(UiControl.Down))
        {
            _backFocused = true;
            Theme.Sounds.Play(UiSound.Move);
        }
        if (Ui.WasPressed(UiControl.Confirm))
            PressButton(_selected);
    }

    /// <returns>True if the mouse clicked something</returns>
    private bool HandleMouse()
    {
        if (Ui.MouseSource == null)
            return false;

        var mouse = Input.MousePosition;
        for (int i = 0; i < _costumes.Count; i++)
        {
            if (!_cardBounds[i].Contains(mouse))
                continue;

            if (Input.MouseMoved && (i != _selected || _backFocused))
            {
                _selected = i;
                _backFocused = false;
                Theme.Sounds.Play(UiSound.Move);
            }

            bool overButton = _buttonBounds[i].Contains(mouse) && !_save.IsEquipped(_costumes[i]);
            if (overButton)
                Stack.RequestHandCursor();
            if (overButton && Input.WasLeftClicked)
            {
                _selected = i;
                PressButton(i);
                return true;
            }
            return false;
        }

        if (Menu.IsClickableAt(mouse))
        {
            Stack.RequestHandCursor();
            if (Input.MouseMoved && !_backFocused)
            {
                _backFocused = true;
                Theme.Sounds.Play(UiSound.Move);
            }
            if (Input.WasLeftClicked)
            {
                Theme.Sounds.Play(UiSound.Cancel);
                Close();
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The card's button: Buy (asks first), Equip, or nothing if it's already equipped
    /// </summary>
    private void PressButton(int index)
    {
        var costume = _costumes[index];

        if (_save.IsEquipped(costume))
            return;    // "Equipped" can't be pressed again

        if (_save.Owns(costume))
        {
            _save.Equip(costume);
            Theme.Sounds.Play(UiSound.Confirm);
            ShowToast($"{costume.Name} equipped!");
            return;
        }

        int balance = _save.Data.SpiderEssence;
        if (costume.Price > balance)
        {
            Theme.Sounds.Play(UiSound.Cancel);
            ShowToast($"Not enough Spider-Essence - you need {costume.Price - balance} more");
            return;
        }

        Theme.Sounds.Play(UiSound.Confirm);
        var prompt = new PromptLayer($"Buy the {costume.Name} for {costume.Price} Spider-Essence?") { OkText = "Buy" };
        prompt.Accepted += _ =>
        {
            if (_save.TryBuyCostume(costume))
                ShowToast($"{costume.Name} unlocked! Press Equip to wear it.");
        };
        Open(prompt);
    }

    private void ShowToast(string message)
    {
        _toast = message;
        _toastLeft = ToastSeconds;
    }

    // ---------------------------------------------------------------- update

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _clock = (float)gameTime.TotalGameTime.TotalSeconds;
        _toastLeft = MathF.Max(0, _toastLeft - dt);

        float step = dt * 8;
        for (int i = 0; i < _cardFade.Length; i++)
        {
            bool on = HasFocus && !_backFocused && i == _selected;
            _cardFade[i] = Math.Clamp(_cardFade[i] + (on ? step : -step), 0f, 1f);
        }
        _backFade = Math.Clamp(_backFade + (HasFocus && _backFocused ? step : -step), 0f, 1f);

        // slide the deck so the picked card stays on screen
        float width = Game.GraphicsDevice.Viewport.Width;
        _scroll = MathHelper.Lerp(_scroll, TargetScroll(width), Math.Min(1, dt * 10));
    }

    private float DeckWidth => _costumes.Count * CardWidth + (_costumes.Count - 1) * CardGap;

    /// <summary>How far the deck is shifted left: 0 when every card fits, otherwise centers the pick</summary>
    private float TargetScroll(float screenWidth)
    {
        float overflow = DeckWidth - (screenWidth - 40);
        if (overflow <= 0)
            return 0;
        float pickedCenter = _selected * (CardWidth + CardGap) + CardWidth / 2;
        return Math.Clamp(pickedCenter - DeckWidth / 2, -overflow / 2, overflow / 2);
    }

    // ---------------------------------------------------------------- drawing

    public override void Draw(GameTime gameTime)
    {
        var viewport = Game.GraphicsDevice.Viewport;
        float centerX = viewport.Width / 2f;
        float alpha = Transition.Alpha;
        float slide = Transition.Offset * 60;

        DrawBackdrop(gameTime);
        _essenceIcons.Clear();

        float deckLeft = centerX - DeckWidth / 2 - _scroll;
        float cardTop = Theme.TitleTop + 34 + slide;
        float backTop = MathF.Max(cardTop + CardHeight + 30, viewport.Height - 40);

        SpriteBatch.Begin(samplerState: SamplerState.LinearClamp);

        for (int i = 0; i < _costumes.Count; i++)
        {
            float lift = _cardFade[i] * 6;
            var card = new RectangleF(MathF.Round(deckLeft + i * (CardWidth + CardGap)), MathF.Round(cardTop - lift),
                CardWidth, CardHeight);
            DrawCard(i, card, alpha);
        }

        // balance, top right
        var font = Theme.TextFont;
        string balance = (_save?.Data.SpiderEssence ?? 0).ToString();
        var balanceSize = font.MeasureString(balance) * 0.8f;
        var balancePos = new Vector2(viewport.Width - 20 - balanceSize.X, 18);
        SpriteBatch.DrawString(font, balance, balancePos, ClosetLayer.ValueColor * alpha, 0, Vector2.Zero, 0.8f,
            SpriteEffects.None, 0);
        _essenceIcons.Add((new Vector2(balancePos.X - EssenceIconRadius - 10, balancePos.Y + balanceSize.Y / 2), alpha));

        // message line (toast if there is one, otherwise the coming soon note)
        string note = _toastLeft > 0 ? _toast : ClosetLayer.ComingSoon;
        float noteAlpha = _toastLeft > 0 ? MathF.Min(1, _toastLeft * 2) : 1;
        var noteColor = _toastLeft > 0 ? Theme.SelectedColor : Theme.DisabledColor;
        float noteScale = MathF.Min(0.6f, (viewport.Width - 40) / font.MeasureString(note).X);
        var noteSize = font.MeasureString(note) * noteScale;
        SpriteBatch.DrawString(font, note, new Vector2(centerX - noteSize.X / 2, cardTop + CardHeight + 8),
            noteColor * (alpha * noteAlpha), 0, Vector2.Zero, noteScale, SpriteEffects.None, 0);

        DrawBack(new Vector2(centerX, backTop), alpha, gameTime);

        var titleFont = Theme.TitleFont;
        SpriteBatch.DrawString(titleFont, Title, new Vector2(centerX, Theme.TitleTop - Transition.Offset * 100),
            Theme.TitleColor * alpha, 0, titleFont.MeasureString(Title) / 2, Theme.TitleScale, SpriteEffects.None, 0);

        SpriteBatch.End();

        // in-game sprites, point sampled so the pixel art stays crisp
        SpriteBatch.Begin(samplerState: SamplerState.PointClamp);
        for (int i = 0; i < _costumes.Count; i++)
            DrawRunner(i, alpha);
        SpriteBatch.End();

        DrawEssenceIcons();
    }

    private void DrawCard(int index, RectangleF card, float alpha)
    {
        var costume = _costumes[index];
        float fade = _cardFade[index];
        bool equipped = _save.IsEquipped(costume);
        bool owned = _save.Owns(costume);
        var font = Theme.TextFont;

        _cardBounds[index] = ToRectangle(card);

        // card body + border (accent when picked, green stripe when worn)
        Fill(card, CardColor * (0.94f * alpha));
        DrawOutline(card, Color.Lerp(ClosetLayer.LabelColor * 0.4f, ClosetLayer.AccentColor, fade) * alpha,
            fade > 0.5f ? 2 : 1);
        if (equipped)
            Fill(new RectangleF(card.X, card.Y, card.Width, 4), EquippedColor * alpha);

        // artwork on the left
        var portraitArea = new RectangleF(card.X + Padding, card.Y + Padding, PortraitWidth, PortraitHeight);
        if (_portraits.TryGetValue(costume, out var portrait))
            SpriteBatch.Draw(portrait, ToRectangle(portraitArea), Color.White * alpha);

        // "in game" stage on the right (the runner is drawn in the point-sampled pass)
        var stage = new RectangleF(portraitArea.X + PortraitWidth + 8, portraitArea.Y,
            card.Width - Padding * 2 - PortraitWidth - 8, PortraitHeight);
        Fill(stage, StageColor * alpha);
        Fill(new RectangleF(stage.X, stage.Y + stage.Height - 22, stage.Width, 2), ClosetLayer.LabelColor * (0.35f * alpha));
        DrawCentered("In game", new Vector2(stage.X + stage.Width / 2, stage.Y + 12), 0.45f,
            ClosetLayer.LabelColor * alpha);

        // name / first appearance / worth
        float textLeft = card.X + Padding;
        float textWidth = card.Width - Padding * 2;
        float y = portraitArea.Y + PortraitHeight + 8;

        DrawFitted(costume.Name, new Vector2(textLeft, y), 0.85f, textWidth, ClosetLayer.ValueColor * alpha);
        y += 28;
        DrawFitted("First appearance", new Vector2(textLeft, y), 0.45f, textWidth, ClosetLayer.LabelColor * alpha);
        y += 15;
        DrawFitted(costume.FirstAppearance, new Vector2(textLeft, y), 0.55f, textWidth, ClosetLayer.ValueColor * (0.9f * alpha));
        y += 22;

        if (costume.IsFree)
        {
            DrawFitted("Worth: Free", new Vector2(textLeft, y), 0.6f, textWidth, ClosetLayer.LabelColor * alpha);
        }
        else
        {
            const float worthScale = 0.6f;
            string label = "Worth:";
            float labelWidth = font.MeasureString(label).X * worthScale;
            SpriteBatch.DrawString(font, label, new Vector2(textLeft, y), ClosetLayer.LabelColor * alpha, 0,
                Vector2.Zero, worthScale, SpriteEffects.None, 0);
            float iconX = textLeft + labelWidth + 8 + EssenceIconRadius;
            float lineMiddle = y + font.LineSpacing * worthScale / 2;
            _essenceIcons.Add((new Vector2(iconX, lineMiddle), alpha));
            bool affordable = owned || costume.Price <= _save.Data.SpiderEssence;
            SpriteBatch.DrawString(font, costume.Price.ToString(), new Vector2(iconX + EssenceIconRadius + 8, y),
                (affordable ? ClosetLayer.ValueColor : ClosetLayer.AccentColor) * alpha, 0, Vector2.Zero, worthScale,
                SpriteEffects.None, 0);
        }

        // Buy / Equip / Equipped
        var button = new RectangleF(card.X + Padding, card.Y + card.Height - Padding - ButtonHeight,
            card.Width - Padding * 2, ButtonHeight);
        _buttonBounds[index] = ToRectangle(button);

        string text = equipped ? "Equipped" : owned ? "Equip" : "Buy";
        Color fill, textColor;
        if (equipped)
        {
            fill = EquippedColor * 0.25f;
            textColor = EquippedColor;
        }
        else
        {
            fill = Color.Lerp(new Color(60, 22, 26), ClosetLayer.AccentColor, 0.35f + fade * 0.65f);
            textColor = Color.Lerp(Theme.TextColor, Theme.SelectedColor, fade);
        }
        Fill(button, fill * alpha);
        DrawCentered(text, new Vector2(button.X + button.Width / 2, button.Y + button.Height / 2), 0.8f, textColor * alpha);
    }

    /// <summary>
    /// The costume's running loop, standing on the stage's floor line
    /// </summary>
    private void DrawRunner(int index, float alpha)
    {
        var costume = _costumes[index];
        if (!_sheets.TryGetValue(costume, out var sheet))
            return;

        var card = _cardBounds[index];
        float stageLeft = card.X + Padding + PortraitWidth + 8;
        float stageWidth = card.Width - Padding * 2 - PortraitWidth - 8;
        var feet = new Vector2(MathF.Round(stageLeft + stageWidth / 2), card.Y + Padding + PortraitHeight - 22);

        var frames = SpiderFrames.Running;
        var frame = frames[(int)(_clock / RunFrameSeconds) % frames.Length];
        SpriteBatch.Draw(sheet, feet, frame.Source, Color.White * alpha, 0, frame.Origin, SpriteScale,
            SpriteEffects.None, 0);
    }

    private void DrawBack(Vector2 topCenter, float alpha, GameTime gameTime)
    {
        var item = Menu.Items[0];
        var font = Theme.TextFont;
        float pulse = (float)Math.Sin(gameTime.TotalGameTime.TotalSeconds * 6) + 1;
        var size = font.MeasureString(item.Text);
        var center = new Vector2(topCenter.X, topCenter.Y + font.LineSpacing / 2f);

        SpriteBatch.DrawString(font, item.Text, center,
            Color.Lerp(Theme.TextColor, Theme.SelectedColor, _backFade) * alpha, 0, size / 2,
            1 + pulse * 0.05f * _backFade, SpriteEffects.None, 0);

        float clickWidth = Math.Max(size.X, 120) + 40;
        Menu.ClearItemBounds();
        Menu.SetItemBounds(item, new Rectangle((int)(center.X - clickWidth / 2), (int)topCenter.Y,
            (int)clickWidth, font.LineSpacing));
    }

    private void DrawEssenceIcons()
    {
        if (_essenceArt == null || _essenceIcons.Count == 0)
            return;

        float spin = _clock * 2;
        SpriteBatch.Begin(blendState: BlendState.Additive, samplerState: SamplerState.LinearClamp);
        foreach (var (center, a) in _essenceIcons)
            _essenceArt.DrawGlow(SpriteBatch, center, EssenceIconRadius, spin, a * 0.9f);
        SpriteBatch.End();

        SpriteBatch.Begin(samplerState: SamplerState.LinearClamp);
        foreach (var (center, a) in _essenceIcons)
            _essenceArt.DrawCores(SpriteBatch, center, EssenceIconRadius, spin, a);
        SpriteBatch.End();
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Draws text at a scale, shrinking it if it'd be wider than maxWidth</summary>
    private void DrawFitted(string text, Vector2 position, float scale, float maxWidth, Color color)
    {
        var font = Theme.TextFont;
        float width = font.MeasureString(text).X;
        if (width * scale > maxWidth)
            scale = maxWidth / width;
        SpriteBatch.DrawString(font, text, position, color, 0, Vector2.Zero, scale, SpriteEffects.None, 0);
    }

    private void DrawCentered(string text, Vector2 center, float scale, Color color)
    {
        var font = Theme.TextFont;
        SpriteBatch.DrawString(font, text, center, color, 0, font.MeasureString(text) / 2, scale, SpriteEffects.None, 0);
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

    private static Rectangle ToRectangle(RectangleF area)
        => new((int)MathF.Round(area.X), (int)MathF.Round(area.Y), (int)MathF.Round(area.Width), (int)MathF.Round(area.Height));
}
