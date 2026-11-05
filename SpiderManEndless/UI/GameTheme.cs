using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using CIS580.Audio;
using CIS580.UI;

namespace SpiderManEndless.UI;

/// <summary>
/// This game's user interface theme
/// </summary>
public static class GameTheme
{
    /// <summary>
    /// Creates the theme, loading its content
    /// </summary>
    /// <param name="content">Loads the theme's fonts, textures and sounds</param>
    /// <param name="audio">Plays the theme's sounds (on the UI bus)</param>
    public static UiTheme Create(ContentManager content, AudioManager audio) => new()
    {
        // Titles: The Amazing Spider-Man. Everything else: Komika Axis. (The main menu's buttons
        // and the in-game text have their own fonts: Fonts/buttonfont and Fonts/gamefont.)
        TitleFont = content.Load<SpriteFont>("Fonts/titlefont"),
        TitleScale = 1f,
        TextFont = content.Load<SpriteFont>("Fonts/menufont"),
        // Title and first item positions chosen so the longest menu (Options, 8 items) fits on an
        // 800x480 window without scrolling
        TitleTop = 62,
        MenuTop = 120,
        PanelTexture = content.Load<Texture2D>("Textures/gradient"),
        Sounds = new UiSoundSet
        {
            // The selection can move quickly (holding a key repeats), so allow only a couple at once
            Move = new SoundCue(audio.Ui, content.Load<SoundEffect>("Audio/Sfx/ui_move")) { MaxInstances = 2 },
            Confirm = new SoundCue(audio.Ui, content.Load<SoundEffect>("Audio/Sfx/ui_confirm")),
            Cancel = new SoundCue(audio.Ui, content.Load<SoundEffect>("Audio/Sfx/ui_back")),
        },
    };
}
