using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using CIS580.Input;

namespace CIS580.UI;

/// <summary>
/// A popup asking the player to confirm or cancel, such as "Are you sure you want to quit?"
/// It has OK and Cancel buttons that can be clicked, as well as the confirm and cancel controls.
/// </summary>
/// <example>
/// <code>
/// var prompt = new PromptLayer("Are you sure you want to exit?");
/// prompt.Accepted += source => Game.Exit();
/// Open(prompt);     // from another layer, so the prompt closes along with it
/// </code>
/// </example>
public class PromptLayer : Layer
{
    private const float ButtonGap = 24;
    private const float HintScale = 0.6f;

    private readonly string _message;
    private readonly bool _showHints;
    private string _hint;

    // where the buttons were last drawn, for the mouse
    private Rectangle _okButton;
    private Rectangle _cancelButton;

    /// <summary>
    /// Raised when the player confirms, with who confirmed
    /// </summary>
    public event Action<InputSource> Accepted;

    /// <summary>
    /// Raised when the player cancels, with who cancelled
    /// </summary>
    public event Action<InputSource> Cancelled;

    /// <summary>
    /// The text on the confirm button
    /// </summary>
    public string OkText { get; init; } = "OK";

    /// <summary>
    /// The text on the cancel button
    /// </summary>
    public string CancelText { get; init; } = "Cancel";

    /// <param name="message">The question to ask</param>
    /// <param name="showHints">Whether to list the confirm and cancel controls under the buttons</param>
    public PromptLayer(string message, bool showHints = true)
    {
        _message = message;
        _showHints = showHints;
        IsPopup = true;
    }

    protected internal override void Load()
    {
        Transition.OnDuration = Theme.PopupTransition;
        Transition.OffDuration = Theme.PopupTransition;

        // The hints are built from the current bindings, so they stay correct if controls are rebound
        _hint = _showHints
            ? $"{Ui.DescribeBindings(UiControl.Confirm)} = {OkText}     {Ui.DescribeBindings(UiControl.Cancel)} = {CancelText}"
            : null;
    }

    public override void HandleInput(GameTime gameTime)
    {
        var mouseSource = Ui.MouseSource;
        var mouse = Input.MousePosition;
        bool overOk = mouseSource != null && _okButton.Contains(mouse);
        bool overCancel = mouseSource != null && _cancelButton.Contains(mouse);
        if (overOk || overCancel)
            Stack.RequestHandCursor();

        bool clicked = Input.WasLeftClicked;
        if (clicked && overOk)
            Accept(mouseSource);
        else if (clicked && overCancel)
            Cancel(mouseSource);
        else if (Ui.WasPressed(UiControl.Confirm, out var source))
            Accept(source);
        else if (Ui.WasPressed(UiControl.Cancel, out source))
            Cancel(source);
    }

    private void Accept(InputSource source)
    {
        Theme.Sounds.Play(UiSound.Confirm);
        Accepted?.Invoke(source);
        Close();
    }

    private void Cancel(InputSource source)
    {
        Theme.Sounds.Play(UiSound.Cancel);
        Cancelled?.Invoke(source);
        Close();
    }

    public override void Draw(GameTime gameTime)
    {
        Stack.FillScreen(Color.Black * (Theme.DimOpacity * Transition.Alpha));

        var viewport = Game.GraphicsDevice.Viewport;
        var font = Theme.TextFont;
        var padding = Theme.PanelPadding;
        float alpha = Transition.Alpha;

        // the message wraps so the panel always fits on screen
        float maxPanelWidth = viewport.Width - 48;
        float maxTextWidth = maxPanelWidth - padding.X * 2;
        string message = TextLayout.Wrap(font, _message, maxTextWidth);
        var messageSize = font.MeasureString(message);

        // two buttons side by side under the message
        var okSize = font.MeasureString(OkText);
        var cancelSize = font.MeasureString(CancelText);
        float buttonWidth = MathF.Max(MathF.Max(okSize.X, cancelSize.X) + 40, 130);
        float buttonHeight = font.LineSpacing + 8;
        float buttonsWidth = buttonWidth * 2 + ButtonGap;

        float hintScale = _hint == null ? 0 : TextLayout.FitScale(font, _hint, maxTextWidth, HintScale);
        float hintHeight = _hint == null ? 0 : font.LineSpacing * hintScale + 8;

        float contentWidth = MathF.Max(MathF.Max(messageSize.X, buttonsWidth), _hint == null ? 0 : font.MeasureString(_hint).X * hintScale);
        float contentHeight = messageSize.Y + 16 + buttonHeight + hintHeight;
        var panel = new Rectangle(
            (int)((viewport.Width - contentWidth) / 2 - padding.X), (int)((viewport.Height - contentHeight) / 2 - padding.Y),
            (int)(contentWidth + padding.X * 2), (int)(contentHeight + padding.Y * 2));

        float centerX = viewport.Width / 2f;
        float y = panel.Y + padding.Y;

        SpriteBatch.Begin();
        if (Theme.PanelTexture != null)
            SpriteBatch.Draw(Theme.PanelTexture, panel, Color.White * alpha);
        else
            SpriteBatch.Draw(Stack.BlankTexture, panel, Theme.PanelColor * alpha);

        // message lines, centered
        foreach (string line in TextLayout.Lines(message))
        {
            float width = font.MeasureString(line).X;
            SpriteBatch.DrawString(font, line, new Vector2(MathF.Round(centerX - width / 2), y), Theme.TextColor * alpha);
            y += font.LineSpacing;
        }
        y += 16;

        float buttonsLeft = centerX - buttonsWidth / 2;
        _okButton = new Rectangle((int)buttonsLeft, (int)y, (int)buttonWidth, (int)buttonHeight);
        _cancelButton = new Rectangle((int)(buttonsLeft + buttonWidth + ButtonGap), (int)y, (int)buttonWidth, (int)buttonHeight);

        var mouse = Input.MousePosition;
        bool canClick = HasFocus && Ui.MouseSource != null;
        DrawButton(_okButton, OkText, canClick && _okButton.Contains(mouse), alpha);
        DrawButton(_cancelButton, CancelText, canClick && _cancelButton.Contains(mouse), alpha);
        y += buttonHeight + 8;

        if (_hint != null)
        {
            float width = font.MeasureString(_hint).X * hintScale;
            SpriteBatch.DrawString(font, _hint, new Vector2(MathF.Round(centerX - width / 2), y), Theme.TextColor * (0.75f * alpha),
                0, Vector2.Zero, hintScale, SpriteEffects.None, 0);
        }

        SpriteBatch.End();
    }

    private void DrawButton(Rectangle bounds, string text, bool hovered, float alpha)
    {
        var font = Theme.TextFont;
        var edge = hovered ? Theme.SelectedColor : Theme.TextColor * 0.6f;

        SpriteBatch.Draw(Stack.BlankTexture, bounds, Color.Black * ((hovered ? 0.55f : 0.35f) * alpha));
        SpriteBatch.Draw(Stack.BlankTexture, new Rectangle(bounds.X, bounds.Y, bounds.Width, 2), edge * alpha);
        SpriteBatch.Draw(Stack.BlankTexture, new Rectangle(bounds.X, bounds.Bottom - 2, bounds.Width, 2), edge * alpha);
        SpriteBatch.Draw(Stack.BlankTexture, new Rectangle(bounds.X, bounds.Y, 2, bounds.Height), edge * alpha);
        SpriteBatch.Draw(Stack.BlankTexture, new Rectangle(bounds.Right - 2, bounds.Y, 2, bounds.Height), edge * alpha);

        var size = font.MeasureString(text);
        var position = new Vector2(MathF.Round(bounds.Center.X - size.X / 2), TextLayout.CenteredTop(font, text, bounds.Center.Y));
        SpriteBatch.DrawString(font, text, position, (hovered ? Theme.SelectedColor : Theme.TextColor) * alpha);
    }
}
