using CIS580.Flow;
using CIS580.Input;
using SpiderManEndless.Audio;
using SpiderManEndless.Input;
using SpiderManEndless.UI;

namespace SpiderManEndless.States;

/// <summary>
/// The main menu, where the game starts. Any player, on any device, may use it.
/// </summary>
public sealed class MainMenuState : AppState
{
    protected override void Enter()
    {
        ListenTo(Controls.AllDevices());
        Game.Services.GetService<GameAudio>().PlayMenuMusic();

        // background for the submenus (Options, The Closet). the main menu draws its own scene
        Show(new BackgroundLayer());
        Show(new MainMenuLayer(
            onPlay: StartGame,
            onExit: () => Game.Exit()));
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
