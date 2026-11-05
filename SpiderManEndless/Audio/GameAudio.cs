using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Content;
using CIS580.Audio;
using SpiderManEndless.Settings;
using SpiderManEndless.Simulation;

namespace SpiderManEndless.Audio;

/// <summary>
/// This game's sounds and music, and what plays them.
/// </summary>
/// <remarks>
/// <para>
/// The simulation never plays sounds itself (it doesn't even know sound exists). Instead it
/// raises events, and <see cref="OnGameEvent"/> decides what each one sounds like. To add a
/// sound for a new event, load a cue below and add a case for the event.
/// </para>
/// <para>
/// The player's settings (sound on or off, and the volumes) are applied to the audio buses
/// every frame, so changes in the options menu take effect immediately.
/// </para>
/// </remarks>
public sealed class GameAudio
{
    private readonly GameSettings _settings;
    private readonly Random _random = new();
    private int _lastRunTrack = -1;
    private readonly SoundCue _playerJoined;
    private readonly SoundCue _playerLeft;
    private readonly SoundCue _jump;
    private readonly SoundCue _webShoot;
    private readonly SoundCue _essence;
    private readonly SoundCue _deathHit;
    private readonly SoundCue _deathScream;
    private readonly SoundCue _punchLight;
    private readonly SoundCue _punchHeavy;

    // Spider-Essence pickups climb in pitch while you grab a pack in one go
    private int _essenceStreak;
    private int _essencePack = -1;
    private TimeSpan _lastEssenceTime;

    // ---- swing sounds (the whoosh and the web tension) ----
    // These follow the swing itself, so instead of reacting to events they look at the runner every
    // frame (through the world from the last event). SoundCue only does fire-and-forget one-shots,
    // so their instances are managed here, kept in step with the Effects bus's volume and pause.
    private World _world;
    private bool _webConnected;           // whether the web was connected last frame

    private readonly SoundEffect[] _whooshSounds;
    private SoundEffectInstance _whoosh;
    private int _lastWhoosh = -1;
    private float _whooshLevel;           // 0 to 1: how much of the whoosh this swing has earned
    private float _whooshPitch;           // a little random pitch per whoosh, so they don't repeat exactly

    private readonly SoundEffect _tensionSound;
    private SoundEffectInstance _tension;
    private float _tensionLevel;          // current fade level, 0 to 1

    /// <summary>
    /// How long the web takes to fly out to its anchor (WorldView draws it over 0.07s). The swing
    /// sounds start once it has, so they only play while the web is actually attached to something
    /// </summary>
    private const float WebFlySeconds = 0.07f;

    /// <summary>The whoosh's volume for a full-strength swing, 0 to 1</summary>
    private const float WhooshVolume = 0.6f;

    /// <summary>
    /// How long a swing takes to earn the full whoosh (seconds after the web connects). Letting
    /// go sooner leaves it at that fraction, so a split-second swing barely makes a sound
    /// </summary>
    private const float WhooshRampSeconds = 0.3f;

    /// <summary>
    /// Swing speeds (units/sec) for the quietest and loudest whoosh. The track runs 360 to 720,
    /// and a swing can go up to about 1.5x that
    /// </summary>
    private const float SlowSwingSpeed = 300f, FastSwingSpeed = 900f;

    /// <summary>How loud the tension loop is, 0 to 1</summary>
    private const float TensionVolume = 0.8f;

    /// <summary>
    /// How quickly the tension fades in when the web connects, and out when it lets go (seconds).
    /// Both very short: just enough to avoid a click, so it's only heard while the web is attached
    /// </summary>
    private const float TensionFadeIn = 0.04f, TensionFadeOut = 0.03f;

    /// <summary>Each pickup in a streak is this much higher than the last (in octaves: 1/12 is a semitone)</summary>
    private const float EssencePitchStep = 1f / 12f;

    /// <summary>The highest the pickup pitch goes (in octaves)</summary>
    private const float EssenceMaxPitch = 0.75f;

    /// <summary>A gap longer than this between pickups starts the climb over, even in the same pack</summary>
    private static readonly TimeSpan EssenceStreakWindow = TimeSpan.FromSeconds(0.5);

    /// <summary>The chance the "no!" scream plays on top of the hit when he dies</summary>
    private const double ScreamChance = 1.0 / 8.0;

    /// <param name="audio">The audio manager</param>
    /// <param name="content">Loads the sounds; they stay loaded while the game runs</param>
    /// <param name="settings">The player's settings</param>
    public GameAudio(AudioManager audio, ContentManager content, GameSettings settings)
    {
        Audio = audio ?? throw new ArgumentNullException(nameof(audio));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        MenuMusic = content.Load<SoundEffect>("Audio/Music/Title Menu");

        // each run picks one of these at random. intro plays once, then it loops
        RunMusic = Array.ConvertAll(RunTracks, track => LoadLoopingTrack(content, track));

        // Gameplay sounds go on the Effects bus, which is paused along with the game
        _playerJoined = new SoundCue(audio.Effects, content.Load<SoundEffect>("Audio/Sfx/player_join"))
        {
            PitchVariation = 0.05f,
        };
        _playerLeft = new SoundCue(audio.Effects, content.Load<SoundEffect>("Audio/Sfx/player_leave"));

        // a low push-off thump + shoe scuff + cloth flick: weighty but short (the old one is Sfx/Jump)
        _jump = new SoundCue(audio.Effects,
            content.Load<SoundEffect>("Audio/Sfx/NimbleJump1"),
            content.Load<SoundEffect>("Audio/Sfx/NimbleJump2"),
            content.Load<SoundEffect>("Audio/Sfx/NimbleJump3"),
            content.Load<SoundEffect>("Audio/Sfx/NimbleJump4"),
            content.Load<SoundEffect>("Audio/Sfx/NimbleJump5"))
        {
            PitchVariation = 0.04f,
            VolumeVariation = 0.1f,
            Volume = 0.75f,
        };

        // the web shot, designed in Thwip Lab: a heavy click-clack with a solenoid buzz, a short
        // spray, a high springy zip and a juicy splat. the four files are variations exported
        // together (their settings are in Sfx_Originals/thwip-settings.json). picks one at random
        // each time, never the same one twice in a row
        _webShoot = new SoundCue(audio.Effects,
            content.Load<SoundEffect>("Audio/Sfx/WebThwip1"),
            content.Load<SoundEffect>("Audio/Sfx/WebThwip2"),
            content.Load<SoundEffect>("Audio/Sfx/WebThwip3"),
            content.Load<SoundEffect>("Audio/Sfx/WebThwip4"))
        {
            PitchVariation = 0.05f,
            VolumeVariation = 0.1f,
        };

        // the soft swoosh of the swing, starting when the web connects. how loud it gets depends on
        // how fast and how long he swings (see UpdateSwingSounds). the old, louder one is Sfx/AirDash
        _whooshSounds = new[]
        {
            content.Load<SoundEffect>("Audio/Sfx/WebWhoosh1"),
            content.Load<SoundEffect>("Audio/Sfx/WebWhoosh2"),
            content.Load<SoundEffect>("Audio/Sfx/WebWhoosh3"),
            content.Load<SoundEffect>("Audio/Sfx/WebWhoosh4"),
        };

        // the steady creak and fiber crackle of the web pulled taut, looped while it's attached
        // (see UpdateSwingSounds)
        _tensionSound = content.Load<SoundEffect>("Audio/Sfx/WebTension");

        // no random pitch here: the streak sets the pitch so the climb sounds like a clean scale
        // a short bright "point": the bubble's pop pitched up, with a gem sparkle (old one: Sfx/Pickup4)
        _essence = new SoundCue(audio.Effects, content.Load<SoundEffect>("Audio/Sfx/EssencePickup"))
        {
            MaxInstances = 8,
            Volume = 0.9f,
        };

        // punches for combat. light = an attack connecting, heavy = getting hit or a big finisher.
        // nothing raises combat events yet, so for now these are played through PlayPunch()
        _punchLight = new SoundCue(audio.Effects,
            content.Load<SoundEffect>("Audio/Sfx/PunchLight1"),
            content.Load<SoundEffect>("Audio/Sfx/PunchLight2"),
            content.Load<SoundEffect>("Audio/Sfx/PunchLight3"),
            content.Load<SoundEffect>("Audio/Sfx/PunchLight4"))
        {
            PitchVariation = 0.06f,
            VolumeVariation = 0.15f,
            MaxInstances = 3,
        };
        _punchHeavy = new SoundCue(audio.Effects,
            content.Load<SoundEffect>("Audio/Sfx/PunchHeavy1"),
            content.Load<SoundEffect>("Audio/Sfx/PunchHeavy2"),
            content.Load<SoundEffect>("Audio/Sfx/PunchHeavy3"),
            content.Load<SoundEffect>("Audio/Sfx/PunchHeavy4"))
        {
            PitchVariation = 0.04f,
            VolumeVariation = 0.1f,
            MaxInstances = 2,
        };

        // dying is getting hit hard, so it uses the heavy punches (old one: Sfx/Hit)
        _deathHit = _punchHeavy;
        // the "no!" falling away: drops in pitch, dulls and echoes out (the original is spiderman-screaming-no.mp3)
        _deathScream = new SoundCue(audio.Effects, content.Load<SoundEffect>("Audio/Sfx/ScreamFall"))
        {
            MaxInstances = 1,
        };
    }

    /// <summary>
    /// The audio manager
    /// </summary>
    public AudioManager Audio { get; }

    /// <summary>
    /// The music for the menus
    /// </summary>
    public SoundEffect MenuMusic { get; }

    /// <summary>
    /// The songs that can play during a run (random pick each run)
    /// </summary>
    public LoopingTrack[] RunMusic { get; }

    /// <summary>
    /// A song with an intro, plus its loop points (seconds from the start of the file)
    /// </summary>
    /// <param name="File">Name of the .wav in Assets/Audio/Loops, no extension</param>
    /// <param name="LoopStart">Loop start time. Anything before this is the intro and only plays once</param>
    /// <param name="LoopEnd">Where it jumps back to LoopStart (null = end of the file)</param>
    private readonly record struct LoopInfo(string File, double LoopStart, double? LoopEnd = null);

    // loop points for the run music. I worked these out from each song's tempo and sections so
    // the loop is a whole number of bars and the jump back lands on a downbeat. if one sounds off,
    // nudge the numbers but keep the loop length a whole number of bars
    private static readonly LoopInfo[] RunTracks =
    {
        // 90 BPM, 4s intro, 36 bar loop that stops before the fade-out at the end
        new("Action A", LoopStart: 4.385, LoopEnd: 100.385),
        // 128 BPM, 2s quiet intro, rest of the song loops (53 bars)
        new("Action B", LoopStart: 2.340),
        // 120 BPM, 1s riser, rest of the song loops (34 bars)
        new("Tutorial", LoopStart: 1.0096),
    };

    private static LoopingTrack LoadLoopingTrack(ContentManager content, LoopInfo info)
    {
        // these .wavs get copied as-is by the content builder instead of built, so we read them straight from disk
        string path = Path.Combine(content.RootDirectory, "Audio", "Loops", info.File + ".wav");
        using var stream = TitleContainer.OpenStream(path);
        return LoopingTrack.FromWav(stream, TimeSpan.FromSeconds(info.LoopStart),
            info.LoopEnd is double end ? TimeSpan.FromSeconds(end) : null);
    }

    /// <summary>
    /// Cross-fades to the menu music (if it isn't already playing)
    /// </summary>
    public void PlayMenuMusic() => Audio.MusicPlayer.Play(MenuMusic);

    /// <summary>
    /// Cross-fades into a random run song. Never picks the same one as last run so it doesn't get
    /// repetitive. Plays the intro first, then loops without the intro.
    /// </summary>
    public void PlayGameMusic()
    {
        int next;
        if (_lastRunTrack < 0)
            next = _random.Next(RunMusic.Length);
        else
        {
            next = _random.Next(RunMusic.Length - 1);
            if (next >= _lastRunTrack)
                next++;
        }

        _lastRunTrack = next;
        Audio.MusicPlayer.Play(RunMusic[next]);
    }

    /// <summary>
    /// Plays the sound (if any) for something that happened in the game
    /// </summary>
    /// <param name="gameEvent">What happened</param>
    /// <param name="world">The world it happened in, e.g. to find where a sound comes from</param>
    public void OnGameEvent(GameEvent gameEvent, World world)
    {
        _world = world;     // the swing sounds watch this world's runner every frame

        switch (gameEvent)
        {
            case PlayerJoined joined:
                var avatar = world.FindPlayer(joined.Player);
                _playerJoined.Play(pan: avatar != null ? PanFor(avatar.Position) : 0f);
                break;
            case PlayerLeft:
                _playerLeft.Play();
                break;
            case RunnerJumped:
                _jump.Play();
                break;
            case WebAttached:
                _webShoot.Play();
                break;
            case EssenceCollected collected:
                PlayEssence(collected);
                break;
            case RunEnded:
                _deathHit.Play();
                if (_random.NextDouble() < ScreamChance)
                    _deathScream.Play();
                break;
        }
    }

    /// <summary>
    /// Plays the essence pickup, a semitone higher for each orb grabbed in a row from the same
    /// pack. A new pack, or a pause between pickups, starts back at the normal pitch.
    /// </summary>
    private void PlayEssence(EssenceCollected collected)
    {
        TimeSpan now = Audio.Now;
        bool continuesStreak = collected.Pack == _essencePack
            && _essenceStreak > 0
            && now - _lastEssenceTime <= EssenceStreakWindow;

        _essenceStreak = continuesStreak ? _essenceStreak + 1 : 1;
        _essencePack = collected.Pack;
        _lastEssenceTime = now;

        float pitch = MathF.Min((_essenceStreak - 1) * EssencePitchStep, EssenceMaxPitch);
        _essence.Play(pitch: pitch);
    }

    /// <summary>
    /// Plays a punch landing. Once combat raises its own events (e.g. an AttackHit or PlayerHit
    /// record in GameEvents.cs), add cases for them in <see cref="OnGameEvent"/> that call this.
    /// </summary>
    /// <param name="heavy">A heavy hit (getting hit, a finisher) rather than a light one</param>
    /// <param name="pan">Where the hit is, from -1 (left) to 1 (right)</param>
    public void PlayPunch(bool heavy, float pan = 0f)
        => (heavy ? _punchHeavy : _punchLight).Play(pan: pan);

    /// <summary>
    /// Applies the settings to the audio buses, and updates the audio. Call once per frame.
    /// </summary>
    public void Update(GameTime gameTime)
    {
        Audio.Master.Muted = !_settings.SoundEnabled;
        Audio.Music.Volume = _settings.MusicVolume / (float)GameSettings.MaxVolume;
        Audio.Effects.Volume = _settings.EffectsVolume / (float)GameSettings.MaxVolume;
        Audio.Ui.Volume = Audio.Effects.Volume;

        Audio.Update(gameTime.ElapsedGameTime);
        UpdateSwingSounds((float)gameTime.ElapsedGameTime.TotalSeconds);
    }

    /// <summary>
    /// Stops every gameplay sound right away, including the swing sounds (which aren't on the
    /// Effects bus's list, so Effects.StopAll() alone would miss them). Call when leaving the game.
    /// </summary>
    public void StopEffects()
    {
        Audio.Effects.StopAll();
        _world = null;
        _webConnected = false;
        _whoosh?.Dispose();
        _whoosh = null;
        _tension?.Dispose();
        _tension = null;
        _tensionLevel = 0f;
    }

    /// <summary>
    /// Runs the swing whoosh and the web tension from what the runner is actually doing:
    /// <list type="bullet">
    /// <item>both start when the web connects (it has flown out to its anchor)</item>
    /// <item>the tension loops only while the web is attached, with a pitch that rises over the swing</item>
    /// <item>the whoosh's volume grows with how long he's been swinging and how fast he's going, then
    /// stays at that level for its tail after he lets go</item>
    /// </list>
    /// </summary>
    private void UpdateSwingSounds(float dt)
    {
        var bus = Audio.Effects;
        if (bus.IsPaused)
        {
            Pause(_whoosh);
            Pause(_tension);
            return;
        }
        Resume(_whoosh);
        Resume(_tension);

        var runner = _world?.Runner;
        bool connected = runner != null && runner.State == RunnerState.Swinging
            && runner.SwingSeconds >= WebFlySeconds;
        if (connected && !_webConnected)
            StartWhoosh();
        _webConnected = connected;

        // 0 at a slow swing, 1 at a fast one
        float speed = 0f;
        if (runner != null)
        {
            float vx = _world.Track.Speed, vy = runner.VelocityY;
            speed = Math.Clamp((MathF.Sqrt(vx * vx + vy * vy) - SlowSwingSpeed) / (FastSwingSpeed - SlowSwingSpeed), 0f, 1f);
        }
        float swingTime = connected ? runner.SwingSeconds - WebFlySeconds : 0f;

        // whoosh
        if (_whoosh != null && _whoosh.State == SoundState.Stopped)
        {
            _whoosh.Dispose();
            _whoosh = null;
        }
        if (_whoosh != null)
        {
            if (connected)
            {
                float earned = Math.Clamp(swingTime / WhooshRampSeconds, 0f, 1f);
                _whooshLevel = earned * (0.15f + 0.85f * speed);
                _whoosh.Pitch = Math.Clamp(-0.08f + 0.2f * speed + _whooshPitch, -1f, 1f);
            }
            _whoosh.Volume = Math.Clamp(_whooshLevel * WhooshVolume * bus.EffectiveVolume, 0f, 1f);
        }

        // tension
        float step = dt / (connected ? TensionFadeIn : TensionFadeOut);
        _tensionLevel = Math.Clamp(_tensionLevel + (connected ? step : -step), 0f, 1f);
        if (_tensionLevel > 0f && _tension == null)
        {
            _tension = _tensionSound.CreateInstance();
            _tension.IsLooped = true;
            _tension.Volume = 0f;
            _tension.Play();
        }
        if (_tension != null)
        {
            if (_tensionLevel <= 0f)
            {
                _tension.Dispose();
                _tension = null;
            }
            else
            {
                if (connected)
                    _tension.Pitch = Math.Clamp(-0.1f + 0.25f * swingTime, -0.1f, 0.2f);
                _tension.Volume = Math.Clamp(_tensionLevel * TensionVolume * bus.EffectiveVolume, 0f, 1f);
            }
        }
    }

    /// <summary>
    /// Starts a whoosh (a different one from last time), silent until the swing earns it
    /// </summary>
    private void StartWhoosh()
    {
        _whoosh?.Dispose();
        int pick = _random.Next(_whooshSounds.Length - 1);
        if (pick >= _lastWhoosh && _lastWhoosh >= 0)
            pick++;
        _lastWhoosh = pick;
        _whoosh = _whooshSounds[pick].CreateInstance();
        _whooshLevel = 0f;
        _whooshPitch = (float)(_random.NextDouble() * 0.12 - 0.06);
        _whoosh.Volume = 0f;
        _whoosh.Play();
    }

    private static void Pause(SoundEffectInstance instance)
    {
        if (instance?.State == SoundState.Playing)
            instance.Pause();
    }

    private static void Resume(SoundEffectInstance instance)
    {
        if (instance?.State == SoundState.Paused)
            instance.Resume();
    }

    /// <summary>
    /// Pans a sound toward the side of the world it comes from (gently: hard left or right
    /// sounds odd on headphones)
    /// </summary>
    private static float PanFor(Vector2 position)
        => 0.6f * Math.Clamp(position.X / World.Width * 2f - 1f, -1f, 1f);
}
