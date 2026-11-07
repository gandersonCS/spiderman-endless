using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using CIS580.Feedback;
using CIS580.Flow;
using CIS580.Input;
using CIS580.Simulation;
using SpiderManEndless.Audio;
using SpiderManEndless.Feedback;
using SpiderManEndless.Settings;
using SpiderManEndless.Simulation;
using SpiderManEndless.UI;
using SpiderManEndless.Views;

namespace SpiderManEndless.States;

/// <summary>
/// A game in progress.
/// </summary>
/// <remarks>
/// This state owns the game: the <see cref="GameSession"/> (the simulation), the
/// <see cref="SessionRunner"/> that advances it, and the local players feeding it commands.
/// Each frame it reads the players' input, runs the simulation, and watches for players
/// pausing. The views only read the session.
/// </remarks>
public sealed class InGameState : AppState, ILoadable
{
    private readonly IReadOnlyList<InputSource> _inputSources;
    private readonly List<LocalPlayer> _players = new();
    private readonly HashSet<LocalPlayer> _lostDevice = new();

    private ContentManager _content;
    private SpriteFont _gameFont;
    private Texture2D _tileset;
    private CityBackdrop _backdrop;
    private Texture2D _spidey;
    private HazardSprites _hazardSprites;
    private WorldView _worldView;
    private HudLayer _hud;
    private PausePolicy _pausePolicy;
    private GameAudio _audio;
    private GameSettings _settings;
    private SaveFile _save;
    private int _bankedEssence;        // essence from this run already added to the save, so a continue doesn't count it twice
    private bool _runCounted;          // this run already counted toward "runs played"
    private ContinuePromptLayer _continuePrompt;   // "Continue for N essence?" while it's open
    private bool _continueConfirmed;                // they said yes: send the continue to the simulation
    private int _quietFrames;                       // ignore death-screen input for a moment after the prompt closes
    private readonly Rumble _rumble = new();
    private readonly HitStop _hitStop = new();

    /// <summary>
    /// The simulation
    /// </summary>
    public GameSession Session { get; private set; }

    /// <summary>
    /// Advances the simulation
    /// </summary>
    public SessionRunner Runner { get; private set; }

    /// <summary>
    /// The players on this machine
    /// </summary>
    public IReadOnlyList<LocalPlayer> Players => _players;

    /// <summary>
    /// The rules for pausing
    /// </summary>
    public PausePolicy PausePolicy => _pausePolicy;

    /// <summary>
    /// Raised for each event the simulation raises (once per event, after the tick it happened
    /// in). Views, audio, and effects subscribe to this to react to what happens in the game.
    /// </summary>
    public event Action<GameEvent> GameEventRaised;

    /// <param name="inputSources">One input source for each local player</param>
    public InGameState(IReadOnlyList<InputSource> inputSources)
    {
        if (inputSources == null || inputSources.Count == 0)
            throw new ArgumentException("A game needs at least one player.", nameof(inputSources));
        _inputSources = inputSources;
    }

    /// <summary>
    /// Loads the game's content and creates the session (run by a LoadingState)
    /// </summary>
    public IEnumerable<float> Load()
    {
        var services = Game.Services;

        // Gameplay content is loaded with its own ContentManager, so it can all be
        // unloaded when the game ends
        _content = new ContentManager(services, "Content");
        _gameFont = _content.Load<SpriteFont>("Fonts/gamefont");
        _tileset = _content.Load<Texture2D>("Textures/tileset_city_free");
        _backdrop = new CityBackdrop(_content);
        // whichever suit is equipped in the Closet (all suits share the same frame layout)
        _spidey = _content.Load<Texture2D>(Costumes.Equipped(services.GetService<SaveFile>()).SpriteSheet);
        _hazardSprites = new HazardSprites(_content);
        yield return 0.5f;

        _pausePolicy = services.GetService<PausePolicy>() ?? new PausePolicy();
        _audio = services.GetService<GameAudio>();
        _settings = services.GetService<GameSettings>();
        _save = services.GetService<SaveFile>();

        Session = new GameSession(seed: (ulong)Environment.TickCount64);
        Runner = new SessionRunner(Session);

        var profile = services.GetService<InputProfile>();
        for (int i = 0; i < _inputSources.Count; i++)
        {
            var player = new LocalPlayer(new PlayerId(i + 1), _inputSources[i], profile);
            _players.Add(player);
            Runner.AddPlayer(player.Id, player.Commands);
        }
        yield return 1f;
    }

    protected override void Enter()
    {
        // No menus are open during play
        ListenTo(Array.Empty<InputSource>());

        // The world, with the HUD over it; both react to the simulation's events
        // Their on-screen buttons only respond while the game is the top state (not under the pause menu)
        _worldView = Show(new WorldView(Session, _gameFont, _tileset, _backdrop, _spidey, _save,
            canContinue: () => Session.World.CanContinue && CanAffordContinue(),
            buttonsActive: () => IsTop && _continuePrompt == null,
            hazardSprites: _hazardSprites));
        _hud = Show(new HudLayer(_players,
            showPause: () => Session.World.Runner.State != RunnerState.Dead,
            buttonsActive: () => IsTop));
        GameEventRaised += _worldView.OnGameEvent;
        GameEventRaised += _hud.OnGameEvent;

        // So do the sounds and the game feel (shake, rumble, hit-stop)
        var feedback = new GameFeedback(_players, _worldView, _rumble, _hitStop);
        GameEventRaised += feedback.OnGameEvent;
        GameEventRaised += gameEvent => _audio.OnGameEvent(gameEvent, Session.World);
        _audio.PlayGameMusic();

        // auto-save progress whenever a run ends
        GameEventRaised += OnGameEvent;
        Game.Exiting += OnGameExiting;
    }

    private void OnGameEvent(GameEvent gameEvent)
    {
        switch (gameEvent)
        {
            case RunEnded ended:
                // bank what they collected (that's also what pays for a continue)
                if (RecordRun(ended.DistanceMeters) is RunRecord saved)
                {
                    _worldView.ShowRunRecord(saved with { EssenceCollected = Session.World.Essence.CollectedThisRun });
                    if (saved.NewHighScore)
                        _audio.PlayNewBest();
                }
                break;

            case RunContinued continued:
                // pay for it out of the total Spider-Essence in the save
                _save?.TrySpend(continued.Cost);
                break;
        }
    }

    /// <summary>
    /// True if the player can pay for the next continue (they always can if there's no save to charge)
    /// </summary>
    private bool CanAffordContinue()
        => _save == null || _save.Data.SpiderEssence >= Session.World.NextContinueCost;

    /// <summary>
    /// Saves this run's distance + the essence collected since the last save. After a continue the
    /// same run gets saved again, so only the new essence is added and it isn't counted as another run.
    /// </summary>
    private RunRecord? RecordRun(double meters)
    {
        if (_save == null || Session == null)
            return null;

        int collected = Session.World.Essence.CollectedThisRun;
        int newEssence = collected - _bankedEssence;
        bool firstTime = !_runCounted;
        _bankedEssence = collected;
        _runCounted = true;
        return _save.RecordRun(meters, newEssence, countsAsRun: firstTime);
    }

    /// <summary>
    /// If you quit in the middle of a run you still keep the essence you got (and the distance so far)
    /// </summary>
    private void SaveRunInProgress()
    {
        var world = Session?.World;
        if (world != null && world.Runner.State != RunnerState.Dead && world.Track.DistanceMeters >= 1)
            RecordRun(world.Track.DistanceMeters);
    }

    private void OnGameExiting(object sender, EventArgs args) => SaveRunInProgress();

    protected override void Covered()
    {
        // Paused: don't leave gamepads vibrating
        _rumble.StopAll();
    }

    protected override void Uncovered()
    {
        // Back from the pause menu
        ListenTo(Array.Empty<InputSource>());
    }

    protected override void Exit()
    {
        // quit mid-run from the pause menu - save what they collected
        Game.Exiting -= OnGameExiting;
        SaveRunInProgress();

        // Stop the game's sounds and vibration (the music changes with the next state)
        _rumble.StopAll();
        _audio?.StopEffects();      // also stops the web tension loop

        // The world view keeps drawing (with this content) while it fades out, so only
        // unload the content once it is gone
        if (_worldView != null)
            _worldView.Removed += () => _content.Unload();
        else
            _content?.Unload();
    }

    public override void Update(GameTime gameTime)
    {
        if (!IsTop && _pausePolicy.PausesSimulation)
            return;

        // Read each player's input before running the simulation, so it responds immediately.
        // Players using a menu (e.g. the pause menu, if it doesn't stop the simulation) stand still.
        // After dying, pressing jump (or clicking Continue) asks "Continue for N Spider-Essence?"
        // first; only a yes sends the continue to the simulation. If they can't pay, nothing
        // happens (the screen tells them to press Esc for the menu)
        var world = Session.World;
        bool dead = world.Runner.State == RunnerState.Dead;
        bool canContinue = world.CanContinue && CanAffordContinue();
        if (!dead)
            _continueConfirmed = false;
        if (_quietFrames > 0)
            _quietFrames--;

        // The death screen's buttons can be clicked: Main Menu, or Continue (which asks first)
        if (dead && IsTop && _continuePrompt == null && _quietFrames == 0 && !_continueConfirmed)
        {
            if (ClickedOn(_worldView.MenuButton))
            {
                Flow.Change(new MainMenuState());
                return;
            }

            bool asked = ClickedOn(_worldView.ContinueButton)
                         || _players.Any(p => p.Input.WasPressed(nameof(GameAction.Jump), Input));
            if (asked && canContinue && world.Runner.CanContinueNow)
                OpenContinuePrompt();
        }

        foreach (var player in _players)
        {
            // a confirmed continue holds jump for the first player until he's back up (the
            // simulation continues on the press, and he doesn't swing from the hold)
            if (_continueConfirmed && player == _players[0])
                player.Commands.Submit(new PlayerCommand { Actions = ActionSet.Empty.With(GameAction.Jump) });
            else if (!dead && (IsTop || !Ui.Sources.Contains(player.Input)))
                player.SubmitCommand<GameAction>(Input);
            else
                player.SubmitIdle();
        }

        // A hit-stop freezes the simulation for a moment by swallowing the elapsed time
        Runner.Update(_hitStop.Apply(gameTime.ElapsedGameTime));

        // Pass on what happened in the simulation, then clear the events for the next frame
        foreach (var gameEvent in Session.Events.Pending)
            GameEventRaised?.Invoke(gameEvent);
        Session.Events.Clear();

        _rumble.Enabled = _settings?.Vibration ?? true;
        _rumble.Update(gameTime);

        // not while the continue prompt is open (its Esc means "no", not "main menu")
        if (IsTop && _continuePrompt == null && _quietFrames == 0)
            CheckForPause();
    }

    /// <summary>
    /// Asks whether to spend Spider-Essence on a continue. Yes continues, no goes back to the
    /// death screen
    /// </summary>
    private void OpenContinuePrompt()
    {
        var prompt = new ContinuePromptLayer(Session.World.NextContinueCost, _save?.Data.SpiderEssence);
        prompt.Accepted += _ =>
        {
            _continueConfirmed = true;
            CloseContinuePrompt();
        };
        prompt.Cancelled += _ => CloseContinuePrompt();

        // the prompt answers to the players' controls while it's open
        ListenTo(_players.Select(p => p.Input).ToArray());
        _continuePrompt = _worldView.ShowPopup(prompt);
    }

    private void CloseContinuePrompt()
    {
        _continuePrompt = null;
        _quietFrames = 2;       // so the key that answered it doesn't also count on the death screen
        ListenTo(Array.Empty<InputSource>());
    }

    private void CheckForPause()
    {
        bool dead = Session.World.Runner.State == RunnerState.Dead;

        // Clicking the HUD's pause button pauses for the first player
        if (!dead && ClickedOn(_hud.PauseButton))
        {
            Pause(_players[0]);
            return;
        }

        foreach (var player in _players)
        {
            // Esc (or Start on a gamepad): mid-run it opens the pause menu, but once the run is
            // over (on the death screen) it goes back to the main menu. Exit() saves the
            // essence they collected so far
            if (player.Input.WasPressed(StandardControls.Pause, Input))
            {
                if (dead)
                    Flow.Change(new MainMenuState());
                else
                    Pause(player);
                return;
            }

            // Pause when a player's gamepad is unplugged (only once, not every frame it stays unplugged)
            bool lost = player.Input.HasLostDevice(Input);
            if (lost && _lostDevice.Add(player) && _pausePolicy.PauseWhenDeviceLost)
            {
                Flow.Push(new PausedState(this, player));
                return;
            }
            if (!lost)
                _lostDevice.Remove(player);
        }
    }

    private void Pause(LocalPlayer player)
    {
        if (_pausePolicy.CanPause(player, _players))
            Flow.Push(new PausedState(this, player));
    }

    /// <summary>
    /// True if the left mouse button was clicked this frame inside the given on-screen button
    /// </summary>
    private bool ClickedOn(Rectangle? button)
        => button is Rectangle bounds && Game.IsActive && Input.WasLeftClicked && bounds.Contains(Input.MousePosition);
}
