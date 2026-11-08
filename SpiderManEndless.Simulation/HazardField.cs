using System;
using System.Collections.Generic;
using CIS580.Simulation;

namespace SpiderManEndless.Simulation;

/// <summary>
/// Everything that's out to get him: task force soldiers (on roofs and flying), their bullets,
/// pylons (on roofs, or dropped from the sky) and pumpkin bombs (launched along his height from the right).
/// Also runs the spider sense, the combo, close calls, invulnerability, and the spawn director that
/// decides what comes next.
/// </summary>
/// <remarks>
/// <para>
/// Works like <see cref="RooftopTrack"/> and <see cref="EssenceField"/>: it only changes in
/// <see cref="Update"/> (once a tick, after the runner moves), with its own seeded RNG so adding
/// hazards didn't change what city a seed makes.
/// </para>
/// <para>
/// Everything is one hit. Getting shot, crushed, blown up, or running into a soldier or the side
/// of a pylon ends the run (<see cref="Runner.Kill"/>). Attacking a soldier beats him: he gets
/// knocked back in a random direction, then bursts into Spider-Essence that flies to you. Beating
/// a soldier or a close call makes him briefly invulnerable to hazards (not to gaps or walls).
/// </para>
/// <para>
/// Difficulty (<see cref="Difficulty"/>, 0 to 1) climbs with distance: faster bullets, flyers and
/// falling things, more spawns, more pumpkin bombs, and more essence for beating soldiers.
/// The director also adapts to how he plays (<see cref="AirShare"/>): the more time in the air,
/// the more flyers (at his height); the more time on roofs, the more rooftop soldiers and pylons.
/// </para>
/// <para>
/// Fairness: nothing for the first <see cref="GraceMeters"/>, at most <see cref="MaxThreats"/>
/// spider-sense threats at once, pylons go near the middle of wide roofs (never where he lands
/// from a gap), and things on roofs are spaced so there's room to land between them.
/// </para>
/// <para>
/// Every threat has one move that answers it:
/// <list type="bullet">
/// <item>Soldier (shooting or not): run, jump or swing into him and he goes down. Shooters' bullets are dodged by changing height</item>
/// <item>Shielded soldier on a roof (sets off the spider sense): jump or swing over him (he can't be beaten)</item>
/// <item>Pylon on a roof: jump over it (or onto it: the top is solid)</item>
/// <item>Pylon dropping from the sky: swing, to get past before it lands</item>
/// <item>Soldier in the air (shooting or not): jump or swing into him, he's uppercut automatically</item>
/// <item>Pumpkin bomb: shows up at the right edge at his height, then launches along that line: get off the line (jump, drop or swing)</item>
/// </list>
/// </para>
/// </remarks>
public sealed class HazardField
{
    // ---- difficulty ----

    /// <summary>Nothing spawns until he's run this far (the controls hint is still up)</summary>
    public const double GraceMeters = 80;

    /// <summary>Meters after the grace distance it takes to reach full difficulty</summary>
    public const double RampMeters = 1400;

    // seconds between spawns at cruising speed, at the easiest and hardest. Going faster than
    // cruising speed (swinging) makes spawns come faster too, so they keep up with him
    public const float EasySpawnSeconds = 3.0f;
    public const float HardSpawnSeconds = 1.0f;

    // ---- adapting to how he plays ----

    /// <summary>How many seconds of play <see cref="AirShare"/> mostly reflects</summary>
    public const float AdaptSeconds = 6f;

    /// <summary>How many seconds of his height the flyers aim for</summary>
    public const float HeightAverageSeconds = 1.5f;

    // ---- soldiers ----

    /// <summary>Chance a soldier is a shooter, at the easiest and hardest</summary>
    public const float EasyShooterChance = 0.2f;
    public const float HardShooterChance = 0.75f;

    /// <summary>Chance a rooftop soldier has a riot shield, at the easiest and hardest</summary>
    public const float EasyShieldChance = 0.2f;
    public const float HardShieldChance = 0.4f;

    /// <summary>How long a shooter aims before firing (the spider sense warning), easiest and hardest</summary>
    public const float EasyAimSeconds = 0.85f;
    public const float HardAimSeconds = 0.6f;

    /// <summary>The aim stops following him for this long before the shot, so he can dodge it</summary>
    public const float AimLockSeconds = 0.3f;

    /// <summary>Bullet speed on screen, easiest and hardest</summary>
    public const float EasyBulletSpeed = 340f;
    public const float HardBulletSpeed = 560f;

    /// <summary>Time between shots for a shooter that fires more than once</summary>
    public const float ShotCooldownSeconds = 0.55f;

    /// <summary>
    /// Shooters only aim while they're in this band of the screen: far enough ahead that there's
    /// time to react, and not after he's right next to them
    /// </summary>
    /// <remarks>
    /// At top speed a rooftop shooter is only in this band for about the length of one aim, and
    /// swinging past fast enough gets you by before he can shoot at all
    /// </remarks>
    public const float MinShootAhead = 300f;

    /// <summary>
    /// Every bullet takes at least this long to reach him from the moment it's fired. A shooter
    /// that's too close for that doesn't start aiming, and one that ends up too close by the time
    /// he'd fire holds his shot
    /// </summary>
    public const float MinBulletReactionSeconds = 0.55f;

    /// <summary>
    /// A shooter won't start aiming while he's running toward a roof edge he'll reach within this
    /// long, so a bullet never arrives right when he's forced to jump
    /// </summary>
    public const float NoShotBeforeEdgeSeconds = 1.0f;
    public const float MaxShootX = World.Width - 10f;

    /// <summary>How fast a flyer comes at you (on top of the scrolling), easiest and hardest</summary>
    public const float EasyFlyerSpeed = 60f;
    public const float HardFlyerSpeed = 240f;

    /// <summary>
    /// Flying shooters hang back: they move with the city at this fraction of its speed so they
    /// stay on screen long enough to shoot
    /// </summary>
    public const float FlyingShooterKeepUp = 0.55f;

    /// <summary>How fast a rooftop soldier runs at you, easiest and hardest</summary>
    public const float EasyRunnerSpeed = 40f;
    public const float HardRunnerSpeed = 150f;

    /// <summary>Flyers hover this high above the roof (their feet) when he's been on the roofs</summary>
    public const float MinFlyerAboveRoof = 95f;
    public const float MaxFlyerAboveRoof = 185f;

    /// <summary>When he's been in the air, flyers come in this far above/below his usual height</summary>
    public const float FlyerHeightSpread = 50f;

    // where the gun's muzzle is relative to his feet, for each aim frame (sprite pixels * 1.5)
    internal static readonly (float X, float Y) MuzzleLevel = (-31f, -47f);
    internal static readonly (float X, float Y) MuzzleUp = (-28f, -67f);
    internal static readonly (float X, float Y) MuzzleDown = (-28f, -29f);

    /// <summary>Aiming more than this many degrees above/below level uses the up/down frame</summary>
    public const float AimPoseDegrees = 18f;

    /// <summary>How long a knocked-back soldier flies before he bursts into essence</summary>
    public const float KnockSeconds = 0.38f;
    public const float KnockGravity = 900f;

    // ---- pylons ----

    /// <summary>
    /// How long a pylon takes to drop (the warning), easiest and hardest. It's shortened when he's
    /// going fast, so it always lands on screen where he can see it (and doesn't drop at all if it
    /// can't fall for at least <see cref="MinPylonFallSeconds"/>)
    /// </summary>
    public const float EasyPylonFallSeconds = 1.3f;
    public const float HardPylonFallSeconds = 1.0f;
    public const float MinPylonFallSeconds = 0.7f;

    /// <summary>
    /// A dropped pylon lands this long before he'd reach it if he kept running: just in front of
    /// him, never on him. Swing past before it lands, or jump over (or onto) it once it's down
    /// </summary>
    public const float PylonLandsAheadSeconds = 0.35f;

    /// <summary>
    /// A dropped pylon's landing spot can start out at most this far past the right edge of the
    /// screen (it scrolls into view, with its red warning column, before it lands)
    /// </summary>
    public const float PylonDropBeyondScreen = 500f;

    /// <summary>Falling pylons start showing up at this difficulty (about 120 m in)</summary>
    public const float FallingPylonStartDifficulty = 0.03f;

    /// <summary>Pylons only go on roofs at least this wide</summary>
    public const int MinPylonRoofTiles = 10;

    /// <summary>Pylons go within this fraction of the roof's width from its middle</summary>
    public const float PylonCenterJitter = 0.15f;

    // ---- pumpkin bombs ----

    /// <summary>
    /// Pumpkin bombs: the danger up in the air (flyers that don't shoot are just essence now).
    /// They float in from the right, above the height of even a full jump, so running and
    /// jumping on the roofs is always safe from them; they're only in the way of swinging. The
    /// answer is to swing around them: under, over, or not until they've gone by.
    /// Set this to false to switch them off
    /// </summary>
    public static readonly bool PumpkinBombsEnabled = true;

    /// <summary>
    /// A bomb shows up at the right edge and hangs there, fuse lit, with its flight line drawn
    /// across the screen, for this long (easiest and hardest)...
    /// </summary>
    public const float EasyPumpkinWarnSeconds = 0.9f;
    public const float HardPumpkinWarnSeconds = 0.6f;

    /// <summary>...then launches straight along that line this fast (on screen), easiest and hardest</summary>
    public const float EasyPumpkinSpeed = 650f;
    public const float HardPumpkinSpeed = 950f;

    /// <summary>The spider sense only goes off for a bomb whose line is within this of his chest</summary>
    public const float PumpkinLaneWarnDistance = 70f;

    /// <summary>A bomb's line is at least this far below the top of the screen</summary>
    public const float PumpkinTopMargin = 36f;

    /// <summary>How much they bob up and down as they float</summary>
    public const float PumpkinBob = 7f;

    /// <summary>Pumpkin bombs start showing up at this difficulty (about 190 m in)</summary>
    public const float PumpkinStartDifficulty = 0.08f;

    /// <summary>
    /// Where a dropped pylon's bottom starts: just inside the top of the screen, so it's in view
    /// for the whole drop (not hidden above the screen for half of it)
    /// </summary>
    public const float DropStartY = 20f;

    // ---- fairness ----

    /// <summary>Spawn this far past the right edge of the screen</summary>
    public const float SpawnAhead = 60f;

    /// <summary>Things on a roof stay at least this many tiles from the start of the roof (room to land)...</summary>
    public const int MinTilesFromRoofStart = 6;
    /// <summary>...plus this many more after a big gap (he lands further in after a swing)...</summary>
    public const int ExtraTilesAfterBigGap = 4;
    /// <summary>...and this many from the end (room to jump them before the edge)</summary>
    public const int MinTilesFromRoofEnd = 5;

    /// <summary>Things on roofs are at least this far apart, so there's room to land between them</summary>
    public const float MinGroundSpacing = 300f;

    /// <summary>Most spider-sense threats at once: 1 early on, 2 mid-run, 3 when it's hardest</summary>
    public static int MaxThreatsAt(float difficulty) => difficulty < 0.4f ? 1 : difficulty < 0.85f ? 2 : 3;

    // ---- rewards ----

    /// <summary>Beating another soldier within this long keeps the combo going</summary>
    public const float ComboWindowSeconds = 3.5f;

    /// <summary>The combo multiplies the essence a soldier drops, up to this</summary>
    public const int MaxComboMultiplier = 5;

    /// <summary>Essence a soldier drops before the combo, at the start of a run (shooters are worth more)...</summary>
    public const int SoldierEssence = 4;
    public const int ShooterEssence = 6;
    /// <summary>...and it goes up to this many times that at full difficulty</summary>
    public const float HardEssenceBonus = 2f;

    /// <summary>Most essence one soldier can burst into</summary>
    public const int MaxEssencePerSoldier = 40;

    /// <summary>A spider-sense threat that got this close without hitting him is a close call</summary>
    public const float NearMissDistance = 48f;
    public const int NearMissEssence = 3;

    /// <summary>Invulnerable for this long after beating a soldier...</summary>
    public const float DefeatInvulnerableSeconds = 0.45f;
    /// <summary>...and this long after a close call</summary>
    public const float CloseCallInvulnerableSeconds = 0.8f;

    // ---- his body ----

    /// <summary>Half the width of his hurt box (centered on his feet)</summary>
    public const float RunnerHalfWidth = 11f;
    /// <summary>Height of his hurt box (feet up to just under his hands)</summary>
    public const float RunnerHeight = 54f;

    /// <summary>Bullets count as this big for hitting him</summary>
    public const float BulletRadius = 1f;

    // what has to touch him for him to die is smaller than he looks, so a graze that looks like a
    // miss is a miss (close calls still use his full body, so grazes are rewarded instead)
    public const float LethalHalfWidth = 7f;
    public const float LethalHeight = 42f;
    public const float LethalFeetInset = 3f;

    /// <summary>
    /// Running into the side of a landed pylon pops him up onto it, instead of killing him, if his
    /// feet are this close to its top...
    /// </summary>
    public const float VaultReach = 36f;
    /// <summary>...or if he only just landed (he couldn't have jumped yet)</summary>
    public const float VaultAfterLandingSeconds = 0.3f;

    /// <summary>He's been on the ground this long (0 while in the air; starts high, since he didn't just land)</summary>
    private float _groundedSeconds = 10f;

    private readonly DeterministicRandom _random;
    private readonly List<Enemy> _enemies = new();
    private readonly List<Bullet> _bullets = new();
    private readonly List<Pylon> _pylons = new();
    private readonly List<PumpkinBomb> _pumpkins = new();
    private int _nextId;
    private float _spawnIn = EasySpawnSeconds;
    private double _lastScrollX;
    private bool _hasLastScroll;
    private float _difficulty;
    private float _averageFeetY;
    private bool _hasAverageFeetY;

    public HazardField(DeterministicRandom random)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
    }

    public IReadOnlyList<Enemy> Enemies => _enemies;
    public IReadOnlyList<Bullet> Bullets => _bullets;
    public IReadOnlyList<Pylon> Pylons => _pylons;
    public IReadOnlyList<PumpkinBomb> Pumpkins => _pumpkins;

    /// <summary>
    /// Distance the spawning starts at. Normally <see cref="GraceMeters"/>; tests turn it down
    /// </summary>
    internal double StartMeters { get; set; } = GraceMeters;

    /// <summary>0 at the start of the run (after the grace distance), 1 at full difficulty</summary>
    public float Difficulty => _difficulty;

    /// <summary>Most spider-sense threats allowed at once right now</summary>
    public int MaxThreats => MaxThreatsAt(_difficulty);

    /// <summary>
    /// How much he's been in the air lately: 0 = always on the roofs, 1 = always jumping/swinging.
    /// Steers what spawns
    /// </summary>
    public float AirShare { get; private set; }

    /// <summary>Soldiers beaten this run</summary>
    public int Defeated { get; private set; }

    /// <summary>
    /// What the task force is set up for right now, from how he's been playing. Changes (with a
    /// <see cref="TaskForceAdapted"/> event the view shows) once he clearly favors the air or the roofs
    /// </summary>
    public TaskForceFocus Focus { get; private set; } = TaskForceFocus.Mixed;

    /// <summary>He's in the air this much of the time (or less, for the roofs) before the task force switches</summary>
    public const float AirFocusShare = 0.65f;
    public const float GroundFocusShare = 0.3f;

    /// <summary>
    /// When the task force switches focus, the next few spawns come quickly and match it, so the
    /// switch is felt right away
    /// </summary>
    public const int FocusSurgeSpawns = 3;
    private int _surgeLeft;

    /// <summary>Tests can turn off the hold-fire-before-an-edge rule</summary>
    internal bool HoldFireBeforeEdges { get; set; } = true;

    /// <summary>
    /// Where the thing that ended the run was (track space), so the view can point it out before
    /// the end screen comes up. Null if a hazard didn't do it (he fell, or hit a wall)
    /// </summary>
    public (double X, float Y)? KilledBy { get; private set; }

    /// <summary>Close calls this run</summary>
    public int NearMisses { get; private set; }

    // ---- invulnerability ----

    /// <summary>Time left that nothing here can hurt him (gaps and walls still can)</summary>
    public float InvulnerableSeconds { get; private set; }

    /// <summary>True while he can't be hurt by hazards (the view tints him light blue)</summary>
    public bool IsInvulnerable => InvulnerableSeconds > 0;

    // ---- combo ----

    /// <summary>Soldiers beaten in a row (0 if the combo has run out)</summary>
    public int Combo { get; private set; }

    /// <summary>Time left to beat another soldier and keep the combo going</summary>
    public float ComboSecondsLeft { get; private set; }

    /// <summary>What the combo multiplies essence by (1 to <see cref="MaxComboMultiplier"/>)</summary>
    public int ComboMultiplier => Math.Clamp(Combo, 1, MaxComboMultiplier);

    // ---- spider sense ----

    /// <summary>True while anything dangerous is coming at him</summary>
    public bool SpiderSenseActive { get; private set; }

    /// <summary>How long the spider sense has been going, for its animation</summary>
    public float SpiderSenseSeconds { get; private set; }

    /// <summary>Unit vector (screen space) from his head toward the most urgent threat</summary>
    public float SpiderSenseDirX { get; private set; }
    public float SpiderSenseDirY { get; private set; } = -1;

    /// <summary>0 to 1: how close the most urgent threat is to hitting him</summary>
    public float SpiderSenseUrgency { get; private set; }

    /// <summary>Number of spider-sense threats right now (aiming shooters, bullets, falling pylons, bombs)</summary>
    public int ThreatCount => CountThreats();

    /// <summary>Works out the difficulty for a distance</summary>
    public static float DifficultyAt(double meters)
        => (float)Math.Clamp((meters - GraceMeters) / RampMeters, 0, 1);

    private static float Lerp(float easy, float hard, float t) => easy + (hard - easy) * t;

    /// <summary>
    /// Spawns, moves and collides everything. Call once per tick, after the runner moves and
    /// before the essence updates (so essence from a soldier beaten this tick starts flying now).
    /// </summary>
    public void Update(RooftopTrack track, Runner runner, EssenceField essence, EventQueue<GameEvent> events,
        long tick, float dt)
    {
        // things that move relative to the screen (bullets, knocked soldiers, bombs) need to move
        // along with the scroll too, which jumps around during swings
        double scrollDelta = _hasLastScroll ? track.ScrollX - _lastScrollX : 0;
        _lastScrollX = track.ScrollX;
        _hasLastScroll = true;

        _difficulty = DifficultyAt(track.DistanceMeters);
        bool alive = runner.State != RunnerState.Dead;

        if (ComboSecondsLeft > 0)
        {
            ComboSecondsLeft = MathF.Max(0, ComboSecondsLeft - dt);
            if (ComboSecondsLeft == 0)
                Combo = 0;
        }
        InvulnerableSeconds = MathF.Max(0, InvulnerableSeconds - dt);

        if (alive)
        {
            Adapt(runner, dt);
            UpdateFocus(track, events, tick);
        }

        if (alive && track.IsScrolling && track.DistanceMeters >= StartMeters)
            RunDirector(track, runner, events, tick, dt);

        UpdateEnemies(track, runner, essence, events, tick, dt, scrollDelta, alive);
        UpdateBullets(track, runner, essence, events, tick, dt, scrollDelta);
        UpdatePylons(track, runner, essence, events, tick, dt);
        UpdatePumpkins(track, runner, essence, events, tick, dt, scrollDelta);

        if (runner.State == RunnerState.Dead)
        {
            // the run's over: whatever's mid-burst pays out now so it gets saved with the run
            Combo = 0;
            ComboSecondsLeft = 0;
            InvulnerableSeconds = 0;
            foreach (var enemy in _enemies)
            {
                if (enemy.IsKnocked)
                    Burst(enemy, essence, events, tick);
            }
            _enemies.RemoveAll(e => e.IsKnocked);
        }

        UpdateSpiderSense(track, runner, dt);
        Discard(track);
        UpdatePlatforms(track);
    }

    /// <summary>
    /// A continue: clears everything near him so he doesn't come back into a bullet, and gives
    /// him a moment before the next spawn
    /// </summary>
    internal void Resume(RooftopTrack track)
    {
        double clearTo = track.ScrollX + World.Width + SpawnAhead * 4;
        _enemies.RemoveAll(e => e.X < clearTo);
        _pylons.RemoveAll(p => p.X < clearTo);
        _pumpkins.Clear();
        _bullets.Clear();
        _spawnIn = 2f;
        _hasLastScroll = false;
        Combo = 0;
        ComboSecondsLeft = 0;
        InvulnerableSeconds = 0;
        SpiderSenseActive = false;
        SpiderSenseSeconds = 0;
        KilledBy = null;
        _groundedSeconds = 10f;
        _surgeLeft = 0;
        UpdatePlatforms(track);
    }

    // for tests: put something exactly where a test wants it
    internal void Add(Enemy enemy) => _enemies.Add(enemy);
    internal void Add(Bullet bullet) => _bullets.Add(bullet);
    internal void Add(Pylon pylon) => _pylons.Add(pylon);
    internal void Add(PumpkinBomb bomb) => _pumpkins.Add(bomb);
    internal int NextId() => _nextId++;
    internal void SetAirShare(float share) => AirShare = share;

    /// <summary>Tracks how much he's in the air and how high, so the spawns can match how he plays</summary>
    private void Adapt(Runner runner, float dt)
    {
        _groundedSeconds = runner.State == RunnerState.Running ? _groundedSeconds + dt : 0;

        float inAir = runner.State == RunnerState.Running ? 0 : 1;
        AirShare += (inAir - AirShare) * (1 - MathF.Exp(-dt / AdaptSeconds));

        if (!_hasAverageFeetY)
        {
            _averageFeetY = runner.FeetY;
            _hasAverageFeetY = true;
        }
        _averageFeetY += (runner.FeetY - _averageFeetY) * (1 - MathF.Exp(-dt / HeightAverageSeconds));
    }

    /// <summary>Switches the task force's focus when he clearly changes how he plays</summary>
    private void UpdateFocus(RooftopTrack track, EventQueue<GameEvent> events, long tick)
    {
        if (track.DistanceMeters < StartMeters)
            return;

        var focus = Focus;
        if (AirShare >= AirFocusShare)
            focus = TaskForceFocus.Air;
        else if (AirShare <= GroundFocusShare)
            focus = TaskForceFocus.Ground;
        if (focus == Focus)
            return;

        Focus = focus;
        _surgeLeft = FocusSurgeSpawns;
        _spawnIn = MathF.Min(_spawnIn, 0.6f);
        events.Raise(new TaskForceAdapted(tick, focus));
    }

    /// <summary>The landed pylons' tops are solid, like roofs</summary>
    private void UpdatePlatforms(RooftopTrack track)
    {
        track.ClearPlatforms();
        foreach (var pylon in _pylons)
        {
            if (!pylon.IsFalling)
                track.AddPlatform(new Platform(pylon.X - Pylon.HalfWidth, pylon.X + Pylon.HalfWidth, pylon.Top));
        }
    }

    // ================================================================================
    // spawn director
    // ================================================================================

    internal enum SpawnKind { RooftopSoldier, Flyer, GroundPylon, FallingPylon, PumpkinBomb }

    private void RunDirector(RooftopTrack track, Runner runner, EventQueue<GameEvent> events, long tick, float dt)
    {
        // faster than cruising speed (swinging) = spawns come faster, so the density per distance
        // stays about the same no matter how he moves
        float pace = MathF.Max(1f, track.Speed / MathF.Max(1f, track.CruiseSpeed));
        _spawnIn -= dt * pace;
        if (_spawnIn > 0)
            return;

        float d = _difficulty;
        var kind = _surgeLeft > 0 ? PickSurgeSpawn(d) : PickSpawn(d);
        bool spawned = kind switch
        {
            SpawnKind.RooftopSoldier => TrySpawnRooftopSoldier(track, d),
            SpawnKind.Flyer => TrySpawnFlyer(track, d),
            SpawnKind.GroundPylon => TrySpawnGroundPylon(track),
            SpawnKind.FallingPylon => TrySpawnFallingPylon(track, runner, d, events, tick),
            SpawnKind.PumpkinBomb => TrySpawnPumpkin(track, runner, d, events, tick),
            _ => false,
        };

        // didn't fit anywhere fair (no roof, too crowded, too many threats): try again shortly.
        // a surge comes quicker
        if (spawned && _surgeLeft > 0)
            _surgeLeft--;
        float gap = Lerp(EasySpawnSeconds, HardSpawnSeconds, d) * _random.NextFloat(0.75f, 1.25f);
        _spawnIn = !spawned ? 0.2f : _surgeLeft > 0 ? gap * 0.45f : gap;
    }

    /// <summary>Right after the task force switches focus: units that match it</summary>
    private SpawnKind PickSurgeSpawn(float d)
    {
        if (Focus == TaskForceFocus.Air)
        {
            bool bombsReady = PumpkinBombsEnabled && d >= PumpkinStartDifficulty;
            return bombsReady && _random.Chance(0.35f) ? SpawnKind.PumpkinBomb : SpawnKind.Flyer;
        }
        if (Focus == TaskForceFocus.Ground)
            return _random.Chance(0.6f) ? SpawnKind.RooftopSoldier : SpawnKind.GroundPylon;
        return PickSpawn(d);
    }

    internal SpawnKind PickSpawn(float d)
    {
        // what spawns follows how he plays: in the air a lot = flyers, on the roofs = rooftop
        // soldiers and pylons. bombs come either way (they come at whatever height he's at), more
        // later in the run. the falling pylons and bombs ease in after a bit
        float air = AirShare, ground = 1 - AirShare;
        Span<float> weights = stackalloc float[5];
        // there's always a solid share of rooftop stuff (that's where he lands, and where combos
        // start); how he plays tilts the mix
        weights[(int)SpawnKind.RooftopSoldier] = 2f + 2.5f * ground;
        weights[(int)SpawnKind.Flyer] = (0.8f + 2.2f * air) * (1 + 0.5f * d);
        weights[(int)SpawnKind.GroundPylon] = 0.8f + 1.5f * ground;
        weights[(int)SpawnKind.FallingPylon] = d < FallingPylonStartDifficulty ? 0 : (0.8f + 1.2f * ground) * (1 + d);
        weights[(int)SpawnKind.PumpkinBomb] = !PumpkinBombsEnabled || d < PumpkinStartDifficulty ? 0 : (0.2f + 2.2f * air) * (1 + d);

        float total = 0;
        foreach (float w in weights)
            total += w;

        float roll = _random.NextFloat() * total;
        for (int i = 0; i < weights.Length; i++)
        {
            roll -= weights[i];
            if (roll < 0)
                return (SpawnKind)i;
        }
        return SpawnKind.RooftopSoldier;
    }

    /// <summary>Track x just off the right side of the screen</summary>
    private static double SpawnX(RooftopTrack track) => track.ScrollX + World.Width + SpawnAhead;

    private bool TrySpawnRooftopSoldier(RooftopTrack track, float d)
    {
        if (FindGroundSpot(track, SpawnX(track), out double x, out float roof) is false)
            return false;

        // shielded soldiers hold their ground and never shoot; of the rest, shooters hold their
        // ground and the others run at you
        bool shielded = _random.Chance(Lerp(EasyShieldChance, HardShieldChance, d));
        bool shooter = !shielded && _random.Chance(Lerp(EasyShooterChance, HardShooterChance, d));
        float speed = shooter || shielded ? 0 : Lerp(EasyRunnerSpeed, HardRunnerSpeed, d) * _random.NextFloat(0.7f, 1.1f);
        var enemy = new Enemy(_nextId++, EnemyKind.Rooftop, shooter, x, roof, speed)
        {
            IsRunning = speed > 0,
            IsShielded = shielded,
        };
        _enemies.Add(enemy);
        return true;
    }

    private bool TrySpawnFlyer(RooftopTrack track, float d)
    {
        double x = SpawnX(track);
        bool shooter = _random.Chance(Lerp(EasyShooterChance, HardShooterChance, d));

        // on the roofs: over his head (jump or swing up into them). in the air: around the height
        // he's been flying at, so staying up high doesn't dodge everything
        float roof = HighestRoof(track, x - 700, x);
        float overRoof = roof - _random.NextFloat(MinFlyerAboveRoof, MaxFlyerAboveRoof);
        float atHisHeight = _averageFeetY + _random.NextFloat(-FlyerHeightSpread, FlyerHeightSpread);
        float feetY = Lerp(overRoof, atHisHeight, AirShare);
        feetY = Math.Clamp(feetY, Enemy.BodyHeight + 20, roof - MinFlyerAboveRoof);

        float speed = Lerp(EasyFlyerSpeed, HardFlyerSpeed, d) * _random.NextFloat(0.8f, 1.15f);
        _enemies.Add(new Enemy(_nextId++, EnemyKind.Flyer, shooter, x, feetY, speed));
        return true;
    }

    private bool TrySpawnGroundPylon(RooftopTrack track)
    {
        if (FindPylonSpot(track, SpawnX(track), double.MaxValue, out double x, out float roof) is false)
            return false;
        _pylons.Add(new Pylon(x, roof, roof, fallSeconds: 0));
        return true;
    }

    /// <summary>
    /// Drops a pylon straight down onto the roof just in front of where he'll be when it lands
    /// (if he keeps running at the speed he is now). It never moves sideways or follows him.
    /// Only while he's running on a roof (so the prediction is right and he can still jump or
    /// swing), and only where it lands on screen
    /// </summary>
    private bool TrySpawnFallingPylon(RooftopTrack track, Runner runner, float d, EventQueue<GameEvent> events, long tick)
    {
        // not while he's already dealing with something else, or mid-swing (his speed changes
        // through a swing, so where he'll be can't be predicted)
        if (CountThreats() >= MaxThreats || runner.State == RunnerState.Swinging)
            return false;

        // going fast shortens the drop so the spot isn't too far off screen, and if it would be
        // too short, don't drop one
        float speed = MathF.Max(1f, track.Speed);
        float reach = World.Width + PylonDropBeyondScreen - RooftopTrack.RunnerX;
        float seconds = MathF.Min(Lerp(EasyPylonFallSeconds, HardPylonFallSeconds, d),
            reach / speed - PylonLandsAheadSeconds);
        if (seconds < MinPylonFallSeconds)
            return false;

        double target = track.RunnerTrackX + speed * (seconds + PylonLandsAheadSeconds);
        if (DropSpot(track, target) is not float roof || GroundClutterNear(target, Pylon.HalfWidth * 6))
            return false;

        _pylons.Add(new Pylon(target, DropStartY, roof, seconds));
        events.Raise(new SpiderSenseTriggered(tick, ThreatKind.FallingPylon));
        return true;
    }

    /// <summary>The roof y at a landing spot if there's a few tiles of roof around it, else null</summary>
    private static float? DropSpot(RooftopTrack track, double x)
    {
        const int size = RooftopTrack.TileSize;
        if (BuildingAt(track, x, out _) is not Building building)
            return null;
        if (x - building.StartColumn * (double)size < 3 * size || building.EndColumn * (double)size - x < 3 * size)
            return null;
        return building.TopRow * size;
    }

    /// <summary>
    /// Puts a pumpkin bomb at the right edge of the screen, at the height of his chest right now.
    /// That height is its flight line, fixed: it hangs there with the line drawn (the warning),
    /// then launches straight along it. If he's still on the line when it launches, the spider
    /// sense goes off. Get off the line: jump over it, drop under it, or swing past it
    /// </summary>
    internal bool TrySpawnPumpkin(RooftopTrack track, Runner runner, float d, EventQueue<GameEvent> events, long tick)
    {
        if (CountThreats() >= MaxThreats)
            return false;

        // the line is wherever his chest is right now
        float y = Math.Max(runner.FeetY - RunnerHeight / 2, PumpkinTopMargin);

        // it hangs on screen first (so it's never off to the side where you can't see it), and not
        // inside a building
        double x = track.ScrollX + World.Width - 30;
        if (track.RoofAt(x) is float roof && roof < y + PumpkinBomb.Radius + PumpkinBob)
            return false;

        float speed = Lerp(EasyPumpkinSpeed, HardPumpkinSpeed, d);
        float warn = Lerp(EasyPumpkinWarnSeconds, HardPumpkinWarnSeconds, d);
        var bomb = PumpkinBomb.Hovering(x, y, speed, warn);
        _pumpkins.Add(bomb);
        return true;
    }

    /// <summary>True if a bomb's flight line passes near his chest</summary>
    private static bool InLane(PumpkinBomb bomb, Runner runner)
        => MathF.Abs(bomb.BaseY - (runner.FeetY - RunnerHeight / 2)) <= PumpkinLaneWarnDistance;

    /// <summary>
    /// The building at a track x and the gap (in tiles) right before it, or null if it's a gap
    /// </summary>
    private static Building BuildingAt(RooftopTrack track, double x, out int gapBefore)
    {
        gapBefore = 0;
        var buildings = track.Buildings;
        for (int i = 0; i < buildings.Count; i++)
        {
            var building = buildings[i];
            if (x >= building.StartColumn * (double)RooftopTrack.TileSize && x < building.EndColumn * (double)RooftopTrack.TileSize)
            {
                gapBefore = i > 0 ? building.StartColumn - buildings[i - 1].EndColumn : 0;
                return building;
            }
        }
        return null;
    }

    /// <summary>How many tiles in from the start of a roof things have to be, given the gap before it</summary>
    private static int LandingTiles(int gapBefore)
        => MinTilesFromRoofStart + (gapBefore >= RooftopTrack.BigGapMin ? ExtraTilesAfterBigGap : 0);

    /// <summary>
    /// Finds a fair spot for a rooftop soldier at or after <paramref name="from"/>: far enough from
    /// the roof's edges (further after a big gap, where he lands deeper in) and from anything else
    /// on a roof. False if there isn't one nearby.
    /// </summary>
    internal bool FindGroundSpot(RooftopTrack track, double from, out double x, out float roof)
    {
        const int size = RooftopTrack.TileSize;
        for (int step = 0; step < 16; step++)
        {
            double candidate = from + step * size;
            if (BuildingAt(track, candidate, out int gap) is Building building
                && candidate - building.StartColumn * (double)size >= LandingTiles(gap) * size
                && building.EndColumn * (double)size - candidate >= MinTilesFromRoofEnd * size
                && !GroundClutterNear(candidate, MinGroundSpacing))
            {
                x = candidate;
                roof = building.TopRow * size;
                return true;
            }
        }

        x = 0;
        roof = 0;
        return false;
    }

    /// <summary>
    /// Finds a spot for a pylon: near the middle of the first wide enough roof that starts at or
    /// after <paramref name="from"/> (well away from where he lands, and from the edge he jumps
    /// off), and no further than <paramref name="to"/>. False if there isn't one.
    /// </summary>
    internal bool FindPylonSpot(RooftopTrack track, double from, double to, out double x, out float roof)
    {
        const int size = RooftopTrack.TileSize;
        var buildings = track.Buildings;
        for (int i = 0; i < buildings.Count; i++)
        {
            var building = buildings[i];
            double start = building.StartColumn * (double)size;
            double end = building.EndColumn * (double)size;
            double middle = (start + end) / 2;
            if (middle < from || building.WidthTiles < MinPylonRoofTiles)
                continue;
            if (middle > to)
                break;

            int gap = i > 0 ? building.StartColumn - buildings[i - 1].EndColumn : 0;
            double earliest = start + LandingTiles(gap) * size;
            double latest = end - MinTilesFromRoofEnd * size;
            if (earliest > latest)
                continue;

            double jitter = _random.NextFloat(-PylonCenterJitter, PylonCenterJitter) * building.WidthTiles * size;
            double spot = Math.Clamp(middle + jitter, earliest, latest);
            if (spot < from || spot > to || GroundClutterNear(spot, MinGroundSpacing))
                continue;

            x = spot;
            roof = building.TopRow * size;
            return true;
        }

        x = 0;
        roof = 0;
        return false;
    }

    /// <summary>True if a rooftop soldier or a pylon (landed or about to) is within this distance</summary>
    private bool GroundClutterNear(double x, float distance)
    {
        foreach (var pylon in _pylons)
        {
            if (Math.Abs(pylon.X - x) < distance)
                return true;
        }
        foreach (var enemy in _enemies)
        {
            if (enemy.Kind == EnemyKind.Rooftop && !enemy.IsKnocked && Math.Abs(enemy.X - x) < distance)
                return true;
        }
        return false;
    }

    private static float HighestRoof(RooftopTrack track, double from, double to)
    {
        float highest = RooftopTrack.MaxTopRow * RooftopTrack.TileSize;
        for (double x = from; x <= to; x += RooftopTrack.TileSize)
        {
            if (track.RoofAt(x) is float roof)
                highest = MathF.Min(highest, roof);
        }
        return highest;
    }

    // ================================================================================
    // soldiers
    // ================================================================================

    private void UpdateEnemies(RooftopTrack track, Runner runner, EssenceField essence, EventQueue<GameEvent> events,
        long tick, float dt, double scrollDelta, bool alive)
    {
        var body = RunnerBox(track, runner);

        for (int i = 0; i < _enemies.Count; i++)
        {
            var enemy = _enemies[i];
            enemy.Age += dt;

            if (enemy.IsKnocked)
            {
                // flying away from the hit (moves relative to the screen)
                enemy.KnockedSeconds += dt;
                enemy.X += enemy.KnockVX * dt + scrollDelta;
                enemy.KnockVY += KnockGravity * dt;
                enemy.Y += enemy.KnockVY * dt;
                enemy.Spin += enemy.SpinSpeed * dt;
                if (enemy.KnockedSeconds >= KnockSeconds)
                {
                    Burst(enemy, essence, events, tick);
                    _enemies.RemoveAt(i--);
                }
                continue;
            }

            Move(enemy, track, dt);
            if (enemy.IsShooter)
                UpdateShooter(enemy, track, runner, events, tick, dt, alive);

            if (!alive)
                continue;

            // a shielded soldier is a wall: touching him ends the run, nothing beats him
            if (enemy.IsShielded)
            {
                // spider sense as soon as he comes on screen
                if (!enemy.Sensed && enemy.X - track.ScrollX <= World.Width - 20)
                {
                    enemy.Sensed = true;
                    events.Raise(new SpiderSenseTriggered(tick, ThreatKind.ShieldedSoldier));
                }

                // ...and again when he's close, unless he's already above him (jumping or swinging over)
                bool closeAhead = enemy.X - track.RunnerTrackX <= ShieldCloseWarning && enemy.X > track.RunnerTrackX;
                if (!enemy.SensedClose && closeAhead && runner.FeetY > enemy.Y - Enemy.BodyHeight)
                {
                    enemy.SensedClose = true;
                    events.Raise(new SpiderSenseTriggered(tick, ThreatKind.ShieldedSoldier));
                }

                if (!IsInvulnerable && LethalBox(track, runner).Overlaps(enemy.ShieldedBody))
                {
                    Kill(track, runner, RunEndReason.Tackled, enemy.X, enemy.Y - Enemy.BodyHeight / 2);
                    alive = false;
                }
                continue;
            }

            // everyone else goes down on contact: a punch on the roof, an uppercut in the air
            // (there's no attack button). The attack box still counts too, for the attack action
            bool contact = body.Overlaps(enemy.Body)
                           || (runner.IsAttacking && AttackBox(track, runner).Overlaps(enemy.Body));
            if (contact)
            {
                var enemyBody = enemy.Body;
                runner.AutoAttack(ContactAttack(runner.State == RunnerState.Running,
                    runner.FeetY - RunnerHeight / 2, (enemyBody.Top + enemyBody.Bottom) / 2));
                Defeat(enemy, events, tick);
                continue;
            }

        }
    }

    private void Move(Enemy enemy, RooftopTrack track, float dt)
    {
        if (enemy.Kind == EnemyKind.Flyer)
        {
            // shooters hang back to shoot (and hold their spot on screen while aiming, so they
            // can't drift in close and fire point-blank), the rest fly straight at you
            double vx = !enemy.IsShooter ? -enemy.Speed
                : enemy.IsAiming ? track.Speed
                : track.Speed * FlyingShooterKeepUp;
            enemy.X += vx * dt;
            enemy.Y = enemy.HoverY + 8f * MathF.Sin(enemy.Age * 3.1f + enemy.Id);
            return;
        }

        if (!enemy.IsRunning)
            return;

        // run at you, but stop at the edge of the roof (or a pylon) instead of running off it
        double next = enemy.X - enemy.Speed * dt;
        if (track.RoofAt(next - Enemy.BodyHalfWidth) == enemy.Y && !PylonNear(next, Enemy.BodyHalfWidth + Pylon.HalfWidth))
            enemy.X = next;
        else
            enemy.IsRunning = false;
    }

    private bool PylonNear(double x, float distance)
    {
        foreach (var pylon in _pylons)
        {
            if (Math.Abs(pylon.X - x) < distance)
                return true;
        }
        return false;
    }

    private void UpdateShooter(Enemy enemy, RooftopTrack track, Runner runner, EventQueue<GameEvent> events, long tick,
        float dt, bool alive)
    {
        float screenX = (float)(enemy.X - track.ScrollX);
        bool inRange = screenX >= RooftopTrack.RunnerX + MinShootAhead && screenX <= MaxShootX;

        if (!alive || !inRange)
        {
            // too close, gone past, or the run's over: stop aiming (the shot never happens)
            enemy.AimSeconds = -1;
            enemy.AimLocked = false;
            return;
        }

        if (!enemy.IsAiming)
        {
            enemy.Cooldown = MathF.Max(0, enemy.Cooldown - dt);
            // one shooter at a time: nobody starts aiming while someone else is aiming or a bullet
            // is still in the air, so dodging one shot never throws him into the next
            int maxShots = _difficulty < 0.5f ? 1 : 2;
            if (enemy.Cooldown > 0 || enemy.ShotsFired >= maxShots || CountThreats() >= MaxThreats
                || _bullets.Count > 0 || AnotherShooterAiming(enemy) || EdgeComingUp(track, runner)
                || !FarEnoughToShoot(enemy, track, runner))
                return;

            // start aiming: this is the warning
            enemy.AimSeconds = 0;
            enemy.AimLocked = false;
            enemy.AimDuration = Lerp(EasyAimSeconds, HardAimSeconds, _difficulty);
            events.Raise(new SpiderSenseTriggered(tick, ThreatKind.Shooter));
        }

        enemy.AimSeconds += dt;

        // follow him until the last moment, then hold still so he can get out of the way
        if (!enemy.AimLocked)
        {
            Aim(enemy, track, runner);
            if (enemy.AimSeconds >= enemy.AimDuration - AimLockSeconds)
                enemy.AimLocked = true;
        }

        if (enemy.AimSeconds >= enemy.AimDuration)
        {
            if (FarEnoughToShoot(enemy, track, runner))
                Fire(enemy, events, tick);
            else
            {
                // he got too close during the aim: no point-blank shot
                enemy.AimSeconds = -1;
                enemy.AimLocked = false;
                enemy.Cooldown = ShotCooldownSeconds;
            }
        }
    }

    /// <summary>True if he's running and the roof changes (an edge or a gap) within a second or so</summary>
    private bool EdgeComingUp(RooftopTrack track, Runner runner)
    {
        if (!HoldFireBeforeEdges || runner.State != RunnerState.Running)
            return false;
        float? here = track.RoofAt(track.RunnerTrackX);
        double ahead = track.Speed * NoShotBeforeEdgeSeconds;
        for (double x = RooftopTrack.TileSize; x <= ahead; x += RooftopTrack.TileSize)
        {
            if (track.RoofAt(track.RunnerTrackX + x) != here)
                return true;
        }
        return false;
    }

    /// <summary>True if a bullet fired now would take at least <see cref="MinBulletReactionSeconds"/> to reach his chest</summary>
    private bool FarEnoughToShoot(Enemy enemy, RooftopTrack track, Runner runner)
    {
        var muzzle = enemy.Muzzle;
        float dx = (float)(muzzle.X - track.RunnerTrackX);
        float dy = muzzle.Y - (runner.FeetY - RunnerHeight / 2);
        float speed = Lerp(EasyBulletSpeed, HardBulletSpeed, _difficulty);
        return MathF.Sqrt(dx * dx + dy * dy) >= speed * MinBulletReactionSeconds;
    }

    private bool AnotherShooterAiming(Enemy shooter)
    {
        foreach (var enemy in _enemies)
        {
            if (enemy != shooter && enemy.IsAiming)
                return true;
        }
        return false;
    }

    /// <summary>Points the gun at his chest and picks the matching frame</summary>
    private static void Aim(Enemy enemy, RooftopTrack track, Runner runner)
    {
        // pick the pose from the level muzzle, then aim from that pose's muzzle
        enemy.Pose = AimPose.Level;
        var level = enemy.Muzzle;
        double targetX = track.RunnerTrackX;
        float targetY = runner.FeetY - RunnerHeight * 0.5f;
        float degrees = MathF.Atan2(level.Y - targetY, (float)(level.X - targetX)) * 180f / MathF.PI;
        enemy.Pose = degrees > AimPoseDegrees ? AimPose.Up : degrees < -AimPoseDegrees ? AimPose.Down : AimPose.Level;

        var muzzle = enemy.Muzzle;
        float dx = (float)(targetX - muzzle.X), dy = targetY - muzzle.Y;
        float length = MathF.Sqrt(dx * dx + dy * dy);
        if (length < 1)
            return;
        enemy.AimX = dx / length;
        enemy.AimY = dy / length;
    }

    private void Fire(Enemy enemy, EventQueue<GameEvent> events, long tick)
    {
        var muzzle = enemy.Muzzle;
        float speed = Lerp(EasyBulletSpeed, HardBulletSpeed, _difficulty);
        _bullets.Add(new Bullet(muzzle.X, muzzle.Y, enemy.AimX * speed, enemy.AimY * speed));
        events.Raise(new BulletFired(tick, muzzle.X, muzzle.Y));

        enemy.ShotsFired++;
        enemy.AimSeconds = -1;
        enemy.AimLocked = false;
        enemy.Cooldown = ShotCooldownSeconds;
    }

    /// <summary>
    /// He got hit: knock him back in a random direction (away from Spider-Man), count the combo,
    /// and give Spider-Man a moment of invulnerability
    /// </summary>
    private void Defeat(Enemy enemy, EventQueue<GameEvent> events, long tick)
    {
        enemy.IsKnocked = true;
        enemy.KnockedSeconds = 0;
        enemy.AimSeconds = -1;
        enemy.AimLocked = false;

        // somewhere between up-and-back and slightly-down-and-back
        float angle = _random.NextFloat(-75f, 25f) * MathF.PI / 180f;
        float speed = _random.NextFloat(420f, 620f);
        enemy.KnockVX = MathF.Cos(angle) * speed;
        enemy.KnockVY = MathF.Sin(angle) * speed;
        enemy.SpinSpeed = _random.NextFloat(8f, 16f) * (_random.Chance(0.5f) ? 1 : -1);

        Combo = ComboSecondsLeft > 0 ? Combo + 1 : 1;
        ComboSecondsLeft = ComboWindowSeconds;
        Defeated++;
        InvulnerableSeconds = MathF.Max(InvulnerableSeconds, DefeatInvulnerableSeconds);

        // worth more the further into the run, then the combo multiplies it
        int baseEssence = enemy.IsShooter ? ShooterEssence : SoldierEssence;
        int scaled = (int)MathF.Round(baseEssence * Lerp(1f, HardEssenceBonus, _difficulty));
        enemy.EssenceValue = Math.Min(MaxEssencePerSoldier, scaled * ComboMultiplier);

        var center = enemy.Body;
        events.Raise(new EnemyDefeated(tick, enemy.X, (center.Top + center.Bottom) / 2, enemy.Kind, enemy.IsShooter,
            Combo, ComboMultiplier));
    }

    /// <summary>A knocked-back soldier bursts into essence that flies to Spider-Man</summary>
    private void Burst(Enemy enemy, EssenceField essence, EventQueue<GameEvent> events, long tick)
    {
        float y = enemy.Y - Enemy.BodyHeight / 2;
        essence.SpawnBurst(enemy.X, y, enemy.EssenceValue, _random);
        events.Raise(new EnemyExploded(tick, enemy.X, y, enemy.EssenceValue));
    }

    // ================================================================================
    // bullets
    // ================================================================================

    private void UpdateBullets(RooftopTrack track, Runner runner, EssenceField essence, EventQueue<GameEvent> events,
        long tick, float dt, double scrollDelta)
    {
        for (int i = 0; i < _bullets.Count; i++)
        {
            var bullet = _bullets[i];
            bullet.Age += dt;
            bullet.X += bullet.VelocityX * dt + scrollDelta;
            bullet.Y += bullet.VelocityY * dt;

            if (runner.State != RunnerState.Dead)
            {
                // close calls go by his whole body, the hit by the smaller lethal box
                float gap = RunnerBox(track, runner).DistanceTo(bullet.X, bullet.Y) - BulletRadius;
                bullet.Closest = MathF.Min(bullet.Closest, gap);
                bool hit = LethalBox(track, runner).DistanceTo(bullet.X, bullet.Y) <= BulletRadius;
                if (hit && !IsInvulnerable)
                {
                    _bullets.RemoveAt(i--);
                    Kill(track, runner, RunEndReason.Shot, bullet.X, bullet.Y);
                    continue;
                }
                if (hit)
                    bullet.Closest = 0;     // went through him while he was invulnerable: not a close call
            }

            // off screen: it missed
            float screenX = (float)(bullet.X - track.ScrollX);
            if (screenX < -40 || screenX > World.Width + 200 || bullet.Y < -60 || bullet.Y > World.Height + 60)
            {
                _bullets.RemoveAt(i--);
                CloseCall(bullet.Closest, bullet.X, bullet.Y, track, runner, essence, events, tick);
            }
        }
    }

    // ================================================================================
    // pylons
    // ================================================================================

    private void UpdatePylons(RooftopTrack track, Runner runner, EssenceField essence, EventQueue<GameEvent> events,
        long tick, float dt)
    {
        foreach (var pylon in _pylons)
        {
            pylon.Age += dt;
            bool wasFalling = pylon.IsFalling;

            if (pylon.IsFalling)
            {
                // straight down, speeding up a little as it falls, landing right on time
                float t = pylon.FallProgress;
                pylon.Y = pylon.StartY + (pylon.RoofY - pylon.StartY) * (0.4f * t + 0.6f * t * t);
                if (t >= 1)
                {
                    pylon.Y = pylon.RoofY;
                    pylon.IsFalling = false;
                }
            }

            bool landed = wasFalling && !pylon.IsFalling;
            if (landed)
                events.Raise(new PylonLanded(tick, pylon.X, pylon.Y));

            if (runner.State == RunnerState.Dead)
                continue;

            var body = RunnerBox(track, runner);
            float gap = body.DistanceTo(pylon.Box);
            bool touching = LethalBox(track, runner).Overlaps(pylon.Box);

            // the top is solid (he lands and runs on it, see RooftopTrack.Platforms)
            bool onTop = !wasFalling && runner.FeetY <= pylon.Top + Runner.StepTolerance;

            // ran into the side of a landed pylon near its top, or just after landing in front of
            // it with no time to jump: pop him up onto it instead
            if (touching && !wasFalling && !onTop
                && (runner.FeetY <= pylon.Top + VaultReach || _groundedSeconds < VaultAfterLandingSeconds))
            {
                runner.VaultOnto(track, pylon.Top);
                continue;
            }

            if (touching && !onTop && !IsInvulnerable)
            {
                Kill(track, runner, wasFalling ? RunEndReason.Crushed : RunEndReason.HitPylon, pylon.X, pylon.Top);
                continue;
            }

            if (wasFalling)
                pylon.Closest = MathF.Min(pylon.Closest, gap);

            if (landed)
                CloseCall(pylon.Closest, pylon.X, pylon.Top, track, runner, essence, events, tick);
        }
    }

    // ================================================================================
    // pumpkin bombs
    // ================================================================================

    private void UpdatePumpkins(RooftopTrack track, Runner runner, EssenceField essence, EventQueue<GameEvent> events,
        long tick, float dt, double scrollDelta)
    {
        for (int i = 0; i < _pumpkins.Count; i++)
        {
            var bomb = _pumpkins[i];
            bomb.Age += dt;
            if (!bomb.IsLaunched)
            {
                // hanging at the edge of the screen, bobbing, until it launches
                bomb.X += scrollDelta;
                bomb.Y = bomb.BaseY + PumpkinBob * MathF.Sin(bomb.Age * 7f);
                if (bomb.Age >= bomb.WarnSeconds)
                {
                    bomb.Launch();
                    events.Raise(new PumpkinLaunched(tick, bomb.X, bomb.Y));
                    if (runner.State != RunnerState.Dead && InLane(bomb, runner))
                        events.Raise(new SpiderSenseTriggered(tick, ThreatKind.PumpkinBomb));
                }
                continue;
            }

            // launched: dead straight along its line, so where it'll go is never a surprise
            bomb.X += bomb.VelocityX * dt + scrollDelta;
            bomb.Y = bomb.BaseY;

            bool hitRoof = track.RoofAt(bomb.X) is float roof && bomb.Y + PumpkinBomb.Radius > roof;
            bool alive = runner.State != RunnerState.Dead;

            // close calls go by his whole body, getting blown up needs the bomb to touch the
            // smaller lethal box
            float gap = RunnerBox(track, runner).DistanceTo(bomb.X, bomb.Y) - PumpkinBomb.Radius;
            bool hitHim = alive && LethalBox(track, runner).DistanceTo(bomb.X, bomb.Y) <= PumpkinBomb.Radius;
            if (alive)
                bomb.Closest = MathF.Min(bomb.Closest, hitHim ? 0 : gap);

            if (hitHim || hitRoof)
            {
                _pumpkins.RemoveAt(i--);
                events.Raise(new PumpkinExploded(tick, bomb.X, bomb.Y));
                if (hitHim && !IsInvulnerable)
                    Kill(track, runner, RunEndReason.Blasted, bomb.X, bomb.Y);
                continue;
            }

            // gone past him and off the left side
            float screenX = (float)(bomb.X - track.ScrollX);
            if (screenX < -60)
            {
                _pumpkins.RemoveAt(i--);
                CloseCall(bomb.Closest, bomb.X, bomb.Y, track, runner, essence, events, tick);
            }
        }
    }

    // ================================================================================
    // shared
    // ================================================================================

    /// <summary>
    /// A threat went by without hitting him. If it got close (but never touched him), that's a
    /// close call: a little essence (flying to him from where the threat was) and a moment of
    /// invulnerability
    /// </summary>
    /// <param name="closest">How close it got to hitting him (0 or less = it touched him)</param>
    private void CloseCall(float closest, double x, float y, RooftopTrack track, Runner runner, EssenceField essence,
        EventQueue<GameEvent> events, long tick)
    {
        if (runner.State == RunnerState.Dead || closest <= 0 || closest > NearMissDistance)
            return;

        NearMisses++;
        InvulnerableSeconds = MathF.Max(InvulnerableSeconds, CloseCallInvulnerableSeconds);
        int value = NearMissEssence + (int)MathF.Round(NearMissEssence * _difficulty);
        essence.SpawnBurst(x, y, value, _random);
        events.Raise(new NearMiss(tick, track.RunnerTrackX, runner.FeetY - RunnerHeight, value));
    }

    /// <summary>Ends the run and remembers what did it</summary>
    private void Kill(RooftopTrack track, Runner runner, RunEndReason reason, double x, float y)
    {
        if (runner.State == RunnerState.Dead)
            return;
        KilledBy = (x, y);
        runner.Kill(track, reason);
    }

    /// <summary>A shielded soldier tingles the spider sense a second time when he's this close ahead</summary>
    public const float ShieldCloseWarning = 260f;

    /// <summary>A soldier this much higher or lower than him (middle to middle) counts as above or below</summary>
    public const float ContactHeightBand = 16f;

    /// <summary>
    /// Which attack plays when he touches a soldier, so the fights don't all look the same: on a
    /// roof a jab; in the air a low (dive) kick if the soldier's below him, an upward kick if he's
    /// level, and an uppercut if he's above
    /// </summary>
    /// <param name="onGround">True if he's running on a roof</param>
    /// <param name="myMiddleY">Y of the middle of his body</param>
    /// <param name="enemyMiddleY">Y of the middle of the soldier's body</param>
    public static AttackKind ContactAttack(bool onGround, float myMiddleY, float enemyMiddleY)
    {
        if (onGround)
            return AttackKind.Jab;
        float below = enemyMiddleY - myMiddleY;      // positive: the soldier is lower down
        if (below > ContactHeightBand)
            return AttackKind.AirKick;
        if (below < -ContactHeightBand)
            return AttackKind.Uppercut;
        return AttackKind.UpwardKick;
    }

    /// <summary>His whole body this tick (for close calls)</summary>
    public static HitBox RunnerBox(RooftopTrack track, Runner runner)
        => HitBox.Standing(track.RunnerTrackX, runner.FeetY, RunnerHalfWidth, RunnerHeight);

    /// <summary>What has to be touched for him to die: a bit smaller than he looks</summary>
    public static HitBox LethalBox(RooftopTrack track, Runner runner)
        => HitBox.Standing(track.RunnerTrackX, runner.FeetY - LethalFeetInset, LethalHalfWidth, LethalHeight);

    /// <summary>
    /// Where his attack reaches: in front of him, a bit taller than his body. Kicks reach a little
    /// further up and down than the jab
    /// </summary>
    public static HitBox AttackBox(RooftopTrack track, Runner runner)
    {
        double x = track.RunnerTrackX;
        return runner.Attack == AttackKind.Jab
            ? new HitBox(x - 8, runner.FeetY - 66, x + 60, runner.FeetY + 4)
            : new HitBox(x - 18, runner.FeetY - 88, x + 56, runner.FeetY + 18);
    }

    private int CountThreats()
    {
        int count = _bullets.Count;
        foreach (var enemy in _enemies)
        {
            if (enemy.IsAiming)
                count++;
        }
        foreach (var pylon in _pylons)
        {
            if (pylon.IsFalling)
                count++;
        }
        count += _pumpkins.Count;
        return count;
    }

    /// <summary>
    /// Points the spider sense at whatever's going to hit him soonest
    /// </summary>
    private void UpdateSpiderSense(RooftopTrack track, Runner runner, float dt)
    {
        if (runner.State == RunnerState.Dead)
        {
            SpiderSenseActive = false;
            SpiderSenseSeconds = 0;
            return;
        }

        double headX = track.RunnerTrackX;
        float headY = runner.FeetY - RunnerHeight;
        float bestUrgency = -1;
        double bestX = 0;
        float bestY = 0;

        void Consider(double x, float y, float urgency)
        {
            if (urgency > bestUrgency)
            {
                bestUrgency = urgency;
                bestX = x;
                bestY = y;
            }
        }

        foreach (var enemy in _enemies)
        {
            if (enemy.IsAiming)
            {
                var muzzle = enemy.Muzzle;
                Consider(muzzle.X, muzzle.Y, 0.6f * Math.Clamp(enemy.AimSeconds / enemy.AimDuration, 0f, 1f));
            }
        }
        foreach (var enemy in _enemies)
        {
            // shielded soldiers on screen ahead of him, more urgent the closer they get
            if (enemy.IsShielded && !enemy.IsKnocked && enemy.X > headX - 10
                && enemy.X - track.ScrollX <= World.Width - 20)
            {
                float closeness = Math.Clamp(1 - (float)(enemy.X - headX) / 600f, 0f, 1f);
                Consider(enemy.X, enemy.Y - Enemy.BodyHeight / 2, 0.35f + 0.5f * closeness);
            }
        }
        foreach (var bullet in _bullets)
        {
            float distance = (float)Math.Abs(bullet.X - headX) + MathF.Abs(bullet.Y - headY);
            if (bullet.X > headX - 20)
                Consider(bullet.X, bullet.Y, 0.6f + 0.4f * Math.Clamp(1 - distance / 600f, 0f, 1f));
        }
        foreach (var pylon in _pylons)
        {
            if (pylon.IsFalling)
                Consider(pylon.X, pylon.Y - Pylon.Height / 2, pylon.FallProgress);
        }
        foreach (var bomb in _pumpkins)
        {
            // only launched bombs whose line goes through him, coming at him until they've gone past
            if (bomb.IsLaunched && bomb.X > headX - 20 && InLane(bomb, runner))
                Consider(bomb.X, bomb.Y, 0.5f + 0.5f * Math.Clamp(1 - (float)(bomb.X - headX) / 650f, 0f, 1f));
        }

        if (bestUrgency < 0)
        {
            SpiderSenseActive = false;
            SpiderSenseSeconds = 0;
            return;
        }

        SpiderSenseActive = true;
        SpiderSenseSeconds += dt;
        SpiderSenseUrgency = Math.Clamp(bestUrgency, 0f, 1f);

        float dx = (float)(bestX - headX), dy = bestY - headY;
        float length = MathF.Sqrt(dx * dx + dy * dy);
        if (length > 1)
        {
            SpiderSenseDirX = dx / length;
            SpiderSenseDirY = dy / length;
        }
    }

    private void Discard(RooftopTrack track)
    {
        double behind = track.ScrollX - 200;
        _enemies.RemoveAll(e => e.X < behind || e.Y > World.Height + 300);
        _pylons.RemoveAll(p => p.X < behind);
    }
}
