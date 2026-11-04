using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using CIS580.UI;

namespace SpiderManEndless.UI;

/// <summary>
/// The background behind the menus. It is a backdrop, so it stays visible as menus come and go.
/// </summary>
public class BackgroundLayer : Layer
{
    private Texture2D _texture;

    public BackgroundLayer()
    {
        IsBackdrop = true;
        InputMode = InputMode.None;
    }

    protected override void Load()
    {
        _texture = Game.Content.Load<Texture2D>("Textures/background");
    }

    public override void Draw(GameTime gameTime)
    {
        float brightness = Transition.Alpha;

        SpriteBatch.Begin();
        SpriteBatch.Draw(_texture, Game.GraphicsDevice.Viewport.Bounds, new Color(brightness, brightness, brightness));
        SpriteBatch.End();
    }
}
