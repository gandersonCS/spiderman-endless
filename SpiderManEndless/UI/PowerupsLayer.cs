using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using CIS580.UI;
using CIS580.UI.Widgets;

namespace SpiderManEndless.UI;

/// <summary>
/// Placeholder for the Powerups shop (opened from the Closet). Just says they're coming soon.
/// </summary>
public class PowerupsLayer : MenuLayer
{
    private const string Message = "Powerups are coming soon!";

    public PowerupsLayer() : base("Powerups")
    {
        DimBackground = true;
        FitToScreen = false;
        Menu.Add(new ButtonItem("Back", _ => Close()));
    }

    public override void Draw(GameTime gameTime)
    {
        var viewport = Game.GraphicsDevice.Viewport;
        float centerX = viewport.Width / 2f;
        float alpha = Transition.Alpha;

        DrawBackdrop(gameTime);

        SpriteBatch.Begin();

        var font = Theme.TextFont;
        var size = font.MeasureString(Message);
        SpriteBatch.DrawString(font, Message, new Vector2(centerX, viewport.Height * 0.42f + Transition.Offset * 60),
            Theme.TextColor * alpha, 0, size / 2, 1f, SpriteEffects.None, 0);

        var note = ClosetLayer.ComingSoon;
        var noteSize = font.MeasureString(note) * 0.6f;
        SpriteBatch.DrawString(font, note, new Vector2(centerX - noteSize.X / 2, viewport.Height * 0.42f + 30 + Transition.Offset * 60),
            Theme.DisabledColor * alpha, 0, Vector2.Zero, 0.6f, SpriteEffects.None, 0);

        Menu.Draw(SpriteBatch, Theme, new Vector2(centerX, viewport.Height - 70), Transition, gameTime);

        var titleFont = Theme.TitleFont;
        SpriteBatch.DrawString(titleFont, Title, new Vector2(centerX, Theme.TitleTop - Transition.Offset * 100),
            Theme.TitleColor * alpha, 0, titleFont.MeasureString(Title) / 2, Theme.TitleScale, SpriteEffects.None, 0);

        SpriteBatch.End();
    }
}
