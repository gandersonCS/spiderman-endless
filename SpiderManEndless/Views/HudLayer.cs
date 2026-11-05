using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using CIS580.Input;
using CIS580.UI;
using SpiderManEndless.Simulation;

namespace SpiderManEndless.Views;

/// <summary>
/// The heads-up display drawn over the world during play.
/// </summary>
/// <remarks>
/// The HUD is a popup (the world stays visible beneath it) that never takes input itself. It
/// shows a pause button in the top right (which can be clicked, and lists the player's current
/// pause bindings) and, only when several people are playing, when players join or leave.
/// (Single player games shouldn't show multiplayer details.) The game state checks for clicks
/// on <see cref="PauseButton"/>, since pausing is up to it.
/// </remarks>
public class HudLayer : Layer
{
    private static readonly TimeSpan MessageDuration = TimeSpan.FromSeconds(2.5);
    private static readonly Color ButtonColor = new(13, 19, 32);
    private static readonly Color HighlightColor = new(232, 72, 60);

    private const float Margin = 12;
    private const float LabelScale = 0.75f;

    private readonly IReadOnlyList<LocalPlayer> _players;
    private readonly Func<bool> _showPause;
    private readonly Func<bool> _buttonsActive;
    private readonly List<(string Text, TimeSpan Shown)> _messages = new();
    private TimeSpan _now;
    private Rectangle? _pauseButton;

    /// <param name="players">The players on this machine</param>
    /// <param name="showPause">Whether to show the pause button right now (e.g. not on the death screen)</param>
    /// <param name="buttonsActive">Whether the button can be clicked right now (e.g. not while paused)</param>
    public HudLayer(IReadOnlyList<LocalPlayer> players, Func<bool> showPause = null, Func<bool> buttonsActive = null)
    {
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _showPause = showPause ?? (() => true);
        _buttonsActive = buttonsActive ?? (() => true);
        IsPopup = true;
        InputMode = InputMode.None;
    }

    /// <summary>
    /// Where the pause button is on screen, or null if it isn't showing
    /// </summary>
    public Rectangle? PauseButton => _showPause() ? _pauseButton : null;

    private bool IsMultiplayer => _players.Count > 1;

    private bool IsHovered => PauseButton is Rectangle bounds && _buttonsActive() && Game.IsActive
                              && bounds.Contains(Input.MousePosition);

    /// <summary>
    /// Reacts to something that happened in the simulation
    /// </summary>
    public void OnGameEvent(GameEvent gameEvent)
    {
        if (!IsMultiplayer)
            return;

        switch (gameEvent)
        {
            case PlayerJoined joined:
                _messages.Add(($"Player {joined.Player.Value} joined", _now));
                break;
            case PlayerLeft left:
                _messages.Add(($"Player {left.Player.Value} left", _now));
                break;
        }
    }

    public override void Update(GameTime gameTime)
    {
        _now = gameTime.TotalGameTime;
        _messages.RemoveAll(m => _now - m.Shown > MessageDuration);

        if (IsHovered)
            Stack.RequestHandCursor();
    }

    public override void Draw(GameTime gameTime)
    {
        var font = Theme.TextFont;
        var viewport = Game.GraphicsDevice.Viewport;
        float alpha = Transition.Alpha;

        SpriteBatch.Begin();

        if (_showPause())
            DrawPauseButton(font, viewport, alpha);
        else
            _pauseButton = null;

        // Join and leave messages, fading out, in the bottom left
        float lineHeight = font.LineSpacing * LabelScale;
        var position = new Vector2(Margin, viewport.Height - Margin - lineHeight);
        foreach (var (text, shown) in Enumerable.Reverse(_messages))
        {
            float fade = 1f - (float)((_now - shown) / MessageDuration);
            SpriteBatch.DrawString(font, text, position, Theme.TextColor * (fade * alpha), 0, Vector2.Zero, LabelScale,
                SpriteEffects.None, 0);
            position.Y -= lineHeight;
        }

        SpriteBatch.End();
    }

    /// <summary>
    /// A small "II PAUSE (Esc)" button in the top right, built from the first player's bindings
    /// </summary>
    private void DrawPauseButton(SpriteFont font, Viewport viewport, float alpha)
    {
        string keys = _players[0].Input.DescribeBindings(StandardControls.Pause);
        string label = string.IsNullOrEmpty(keys) ? "PAUSE" : $"PAUSE ({keys})";

        var textSize = font.MeasureString(label) * LabelScale;
        const float iconWidth = 12, padX = 10, gap = 8;
        float height = MathF.Round(font.LineSpacing * LabelScale) + 4;
        float width = padX + iconWidth + gap + textSize.X + padX;
        var bounds = new Rectangle((int)(viewport.Width - Margin - width), (int)Margin, (int)width, (int)height);
        _pauseButton = bounds;

        bool hovered = IsHovered;
        var accent = hovered ? HighlightColor : Color.White * 0.5f;
        Fill(bounds, ButtonColor * ((hovered ? 0.85f : 0.6f) * alpha));
        Fill(new Rectangle(bounds.X, bounds.Bottom - 2, bounds.Width, 2), accent * alpha);

        // the "II" icon
        int barHeight = (int)(height * 0.45f);
        int barTop = bounds.Y + (bounds.Height - barHeight) / 2;
        Fill(new Rectangle(bounds.X + (int)padX, barTop, 4, barHeight), Color.White * alpha);
        Fill(new Rectangle(bounds.X + (int)padX + 8, barTop, 4, barHeight), Color.White * alpha);

        var textPosition = new Vector2(MathF.Round(bounds.X + padX + iconWidth + gap),
            TextLayout.CenteredTop(font, label, bounds.Center.Y, LabelScale));
        SpriteBatch.DrawString(font, label, textPosition, Color.White * ((hovered ? 1f : 0.85f) * alpha), 0,
            Vector2.Zero, LabelScale, SpriteEffects.None, 0);
    }

    private void Fill(Rectangle area, Color color) => SpriteBatch.Draw(Stack.BlankTexture, area, color);
}
