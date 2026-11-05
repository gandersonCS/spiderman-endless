using CIS580.Flow;
using CIS580.Input;
using SpiderManEndless.Audio;
using SpiderManEndless.Input;
using SpiderManEndless.UI;

namespace SpiderManEndless.States;

/// <summary>
/// The main menu, where the game starts. Any player, on any device, may use it.
/// </summary>
/// <remarks>
/// When the game first launches it opens on the title screen ("press any button"), which leads
/// into the menu. Coming back from a run goes straight to the menu.
/// </remarks>
public sealed class MainMenuState : AppState
{
    private readonly bool _showTitle;

    /// <param name="showTitle">Open on the title screen first (when the game launches)</param>
    public MainMenuState(bool showTitle = false)
    {
        _showTitle = showTitle;
    }

    protected override void Enter()
    {
        ListenTo(Controls.AllDevices());
        var audio = Game.Services.GetService<GameAudio>();
        if (_showTitle)
            audio.PlayTitleMusic();
        else
            audio.PlayMenuMusic();

        // background for the submenus (Options, The Closet). the main menu draws its own scene
        Show(new BackgroundLayer());
        Show(new MainMenuLayer(
            onPlay: StartGame,
            onExit: () => Game.Exit(),
            showTitle: _showTitle,
            onTitleDismissed: () =>
            {
                audio.PlayMenuMusic();
                audio.PlayJump();       // he leaps from the web down to the rooftop
            },
            // Options gets the submenu music (the Closet keeps the menu music)
            onOptionsOpened: audio.PlaySubmenuMusic,
            onOptionsClosed: audio.PlayMenuMusic));
    }

    /// <summary>
    /// Starts a solo run for whoever picked "Run", on whatever device they used to pick it
    /// </summary>
    private void StartGame(InputSource chosenWith)
    {
        var game = new InGameState(new[] { Controls.SinglePlayerFor(chosenWith) });
        Flow.Change(new LoadingState(game));
    }

    private GameControls Controls => Game.Services.GetService<GameControls>();
}
