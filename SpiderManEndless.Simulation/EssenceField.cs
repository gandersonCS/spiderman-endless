using System;
using System.Collections.Generic;
using CIS580.Simulation;

namespace SpiderManEndless.Simulation;

/// <summary>
/// A single Spider-Essence orb in track space (X = distance along the track, Y = from the top
/// of the screen)
/// </summary>
/// <param name="Pack">Index of the pack it came from (starts at 0)</param>
public readonly record struct EssenceOrb(double X, float Y, int Pack);

/// <summary>
/// Spider-Essence from a beaten soldier (or a close call) flying to Spider-Man on its own.
/// Track space, like the orbs.
/// </summary>
public sealed class HomingEssence
{
    internal HomingEssence(double x, float y, float vx, float vy, int pack)
    {
        X = x;
        Y = y;
        VelocityX = vx;
        VelocityY = vy;
        Pack = pack;
    }

    public double X { get; internal set; }
    public float Y { get; internal set; }

    /// <summary>Velocity relative to the screen (units/sec), so the view can stretch it into a streak</summary>
    public float VelocityX { get; internal set; }
    public float VelocityY { get; internal set; }

    /// <summary>Seconds since it burst out</summary>
    public float Age { get; internal set; }

    /// <summary>Which burst it's from (negative, so it never matches an orb pack)</summary>
    public int Pack { get; }
}

/// <summary>
/// How a pack of orbs is arranged
/// </summary>
public enum EssencePackShape { Line, Circle }

/// <summary>
/// Spider-Essence (the currency). Spawns in packs of 3-7 orbs, either in a line or a small circle,
/// ahead of the runner. A pack either sits on a roof or floats in the air where you can jump or
/// swing through it.
/// </summary>
/// <remarks>
/// Works like <see cref="RooftopTrack"/>: it only changes in <see cref="Update"/> (once a tick,
/// after the runner moves) and has its own seeded RNG. I gave it a separate RNG so adding orbs
/// didn't change what city a seed generates. Collected orbs get removed and fire an
/// <see cref="EssenceCollected"/> event so the view/audio can react.
/// </remarks>
public sealed class EssenceField
{
    public const int MinPackSize = 3;
    /// <summary>Biggest pack at the start of a run...</summary>
    public const int EasyMaxPackSize = 7;
    /// <summary>...and at full difficulty (packs get bigger and closer together further into a run)</summary>
    public const int MaxPackSize = 10;

    /// <summary>Spacing between packs is multiplied by this at full difficulty</summary>
    public const float HardSpacingScale = 0.7f;

    /// <summary>Track x where the first pack can show up (just past the right edge of the screen)</summary>
    public const double FirstPackX = 900;

    // spacing between packs in world units
    public const float MinPackSpacing = 450f;
    public const float MaxPackSpacing = 950f;

    /// <summary>Chance a pack tries to go on a roof (if there's no long enough flat roof it floats instead)</summary>
    public const float RoofChance = 0.5f;

    /// <summary>Chance of a circle instead of a line</summary>
    public const float CircleChance = 0.45f;

    // layout sizes (world units)
    public const float LineSpacing = 34f;
    public const float CircleSpacing = 28f;
    public const float MinCircleRadius = 24f;

    /// <summary>Height of a line of orbs above the roof - low enough to grab while running</summary>
    public const float LineAboveRoof = 30f;

    /// <summary>
    /// Height of a circle's center above the roof. Bottom orbs you can run through, top ones you
    /// have to jump for
    /// </summary>
    public const float CircleAboveRoof = 75f;

    // floating packs sit this far above the tallest nearby roof so a jump or swing can reach them
    public const float MinAirAboveRoof = 100f;
    public const float MaxAirAboveRoof = 175f;

    /// <summary>Max slope for a floating line of orbs</summary>
    public const float MaxAirSlope = 0.35f;

    /// <summary>Highest an orb can be placed (keeps them on screen)</summary>
    public const float MinOrbY = 40f;

    /// <summary>Pickup radius around Spider-Man's body (feet to hands)</summary>
    public const float CollectRadius = 24f;

    // drop orbs once they're this far off the left side
    private const double DiscardMargin = 200;

    private static readonly double MaxPackLength = (MaxPackSize - 1) * LineSpacing;

    // homing essence: bursts out, then curves in to him faster and faster
    public const float HomingBurstMinSpeed = 160f;
    public const float HomingBurstMaxSpeed = 320f;
    public const float HomingTopSpeed = 1500f;
    /// <summary>How long it takes to get up to top speed</summary>
    public const float HomingRampSeconds = 0.45f;
    /// <summary>How quickly it turns toward him (higher = tighter)</summary>
    public const float HomingSteer = 9f;
    /// <summary>Picked up once it's this close to his chest</summary>
    public const float HomingCollectRadius = 22f;
    /// <summary>Picked up anyway after this long, so none get lost</summary>
    public const float HomingMaxSeconds = 1.4f;

    private readonly DeterministicRandom _random;
    private readonly List<EssenceOrb> _orbs = new();
    private readonly List<HomingEssence> _homing = new();
    private int _burstCount;
    private double _nextPackX = FirstPackX;
    private int _packCount;

    // last tick's runner position so fast swings don't skip over orbs
    private bool _hasLast;
    private double _lastRunnerX;
    private float _lastFeetY;

    public EssenceField(DeterministicRandom random)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
    }

    /// <summary>Orbs that haven't been picked up yet</summary>
    public IReadOnlyList<EssenceOrb> Orbs => _orbs;

    /// <summary>Essence flying to him (from beaten soldiers and close calls)</summary>
    public IReadOnlyList<HomingEssence> Homing => _homing;

    /// <summary>Orbs collected this run</summary>
    public int CollectedThisRun { get; private set; }

    /// <summary>Number of packs spawned so far</summary>
    public int PackCount => _packCount;

    /// <summary>
    /// Spawns new packs, picks up any the runner touches, and cleans up old ones. Call once per
    /// tick after the runner updates.
    /// </summary>
    public void Update(RooftopTrack track, Runner runner, EventQueue<GameEvent> events, long tick, float dt = 1f / 60f)
    {
        Generate(track);
        if (runner.State != RunnerState.Dead)
            Collect(track, runner, events, tick);
        UpdateHoming(track, runner, events, tick, dt);
        Discard(track);
    }

    /// <summary>
    /// Bursts <paramref name="count"/> essence out of a spot (a beaten soldier, a close call).
    /// It flies out in all directions, then homes in on him and counts when it gets there.
    /// </summary>
    /// <param name="random">Randomness for the burst directions (the caller's, so this field's own
    /// sequence - and so the orb layout - doesn't depend on how many soldiers got beaten)</param>
    public void SpawnBurst(double x, float y, int count, DeterministicRandom random)
    {
        int pack = -1 - _burstCount++;
        for (int i = 0; i < count; i++)
        {
            float angle = (i + random.NextFloat(-0.35f, 0.35f)) * 2 * MathF.PI / count;
            float speed = random.NextFloat(HomingBurstMinSpeed, HomingBurstMaxSpeed);
            _homing.Add(new HomingEssence(x, y, MathF.Cos(angle) * speed, MathF.Sin(angle) * speed, pack));
        }
    }

    private void UpdateHoming(RooftopTrack track, Runner runner, EventQueue<GameEvent> events, long tick, float dt)
    {
        if (_homing.Count == 0)
            return;

        double targetX = track.RunnerTrackX;
        float targetY = runner.FeetY - Runner.HandHeight / 2;
        bool dead = runner.State == RunnerState.Dead;

        for (int i = 0; i < _homing.Count; i++)
        {
            var spark = _homing[i];
            spark.Age += dt;

            float dx = (float)(targetX - spark.X), dy = targetY - spark.Y;
            float distance = MathF.Sqrt(dx * dx + dy * dy);

            // the run's over (so it gets saved with the run), it's reached him, or it's taken too long
            if (dead || distance <= HomingCollectRadius || spark.Age >= HomingMaxSeconds)
            {
                _homing.RemoveAt(i--);
                CollectedThisRun++;
                events.Raise(new EssenceCollected(tick, spark.X, spark.Y, CollectedThisRun, spark.Pack));
                continue;
            }

            // steer toward him, faster the longer it's been flying. velocity is relative to the
            // screen (he never moves on screen), so the scroll gets added on top
            float speed = HomingTopSpeed * MathF.Min(1f, spark.Age / HomingRampSeconds);
            float wantX = dx / distance * speed, wantY = dy / distance * speed;
            float steer = 1f - MathF.Exp(-HomingSteer * dt);
            spark.VelocityX += (wantX - spark.VelocityX) * steer;
            spark.VelocityY += (wantY - spark.VelocityY) * steer;

            spark.X += (spark.VelocityX + track.Speed) * dt;
            spark.Y += spark.VelocityY * dt;
        }
    }

    /// <summary>
    /// A continue moves the runner to a new spot, so forget where he was last tick. Otherwise the
    /// pickup box would stretch across the jump and grab orbs he never went near.
    /// </summary>
    internal void Resume() => _hasLast = false;

    private void Generate(RooftopTrack track)
    {
        // wait until the buildings under the whole pack have been generated
        double coveredTo = track.Buildings[^1].EndColumn * (double)RooftopTrack.TileSize;
        while (_nextPackX + MaxPackLength < coveredTo)
            AddPack(track);
    }

    private void AddPack(RooftopTrack track)
    {
        // further along = bigger packs, closer together (a tile is a meter)
        float d = HazardField.DifficultyAt(_nextPackX / RooftopTrack.TileSize);
        int maxSize = (int)MathF.Round(EasyMaxPackSize + (MaxPackSize - EasyMaxPackSize) * d);
        int count = _random.NextInt(MinPackSize, maxSize + 1);
        var shape = _random.Chance(CircleChance) ? EssencePackShape.Circle : EssencePackShape.Line;

        // circle just big enough that the orbs don't overlap
        float radius = MathF.Max(MinCircleRadius, count * CircleSpacing / (2 * MathF.PI));
        double start = _nextPackX;
        double length = shape == EssencePackShape.Line ? (count - 1) * LineSpacing : 2 * radius;
        double end = start + length;

        bool wantsRoof = _random.Chance(RoofChance);
        float airAbove = _random.NextFloat(MinAirAboveRoof, MaxAirAboveRoof);
        float slope = _random.NextFloat(-MaxAirSlope, MaxAirSlope);

        int pack = _packCount++;
        if (wantsRoof && FlatRoof(track, start - CollectRadius, end + CollectRadius) is float roof)
        {
            // on the roof
            if (shape == EssencePackShape.Line)
                AddLine(pack, count, start, roof - LineAboveRoof, 0);
            else
                AddCircle(pack, count, start + radius, roof - CircleAboveRoof, radius);
        }
        else
        {
            // in the air, above the tallest roof nearby (could be over a gap)
            float highestRoof = HighestRoof(track, start - 200, end);
            float centerY = highestRoof - airAbove;

            if (shape == EssencePackShape.Line)
            {
                float halfRise = MathF.Abs(slope) * (float)length / 2;
                centerY = MathF.Max(centerY, MinOrbY + halfRise);
                AddLine(pack, count, start, centerY - slope * (float)length / 2, slope);
            }
            else
            {
                centerY = MathF.Max(centerY, MinOrbY + radius);
                AddCircle(pack, count, start + radius, centerY, radius);
            }
        }

        float spacingScale = 1f + (HardSpacingScale - 1f) * d;
        _nextPackX = end + _random.NextFloat(MinPackSpacing, MaxPackSpacing) * spacingScale;
    }

    private void AddLine(int pack, int count, double startX, float startY, float slope)
    {
        for (int i = 0; i < count; i++)
        {
            float along = i * LineSpacing;
            _orbs.Add(new EssenceOrb(startX + along, startY + slope * along, pack));
        }
    }

    private void AddCircle(int pack, int count, double centerX, float centerY, float radius)
    {
        // start at the top, go around
        for (int i = 0; i < count; i++)
        {
            float angle = -MathF.PI / 2 + i * 2 * MathF.PI / count;
            _orbs.Add(new EssenceOrb(centerX + radius * MathF.Cos(angle), centerY + radius * MathF.Sin(angle), pack));
        }
    }

    /// <summary>
    /// Returns the roof y if a single flat roof covers the whole range (no gaps or steps), else null
    /// </summary>
    private static float? FlatRoof(RooftopTrack track, double from, double to)
    {
        float? first = track.RoofAt(from);
        if (first is null)
            return null;

        for (double x = from; x < to; x += RooftopTrack.TileSize / 2)
        {
            if (track.RoofAt(x) != first)
                return null;
        }
        return track.RoofAt(to) == first ? first : null;
    }

    /// <summary>
    /// Y of the tallest roof in a range (smallest y), or the lowest possible roof if it's all gap
    /// </summary>
    private static float HighestRoof(RooftopTrack track, double from, double to)
    {
        float highest = RooftopTrack.MaxTopRow * RooftopTrack.TileSize;
        for (double x = from; x <= to; x += RooftopTrack.TileSize / 2)
        {
            if (track.RoofAt(x) is float roof)
                highest = MathF.Min(highest, roof);
        }
        return highest;
    }

    private void Collect(RooftopTrack track, Runner runner, EventQueue<GameEvent> events, long tick)
    {
        double x = track.RunnerTrackX;
        float feetY = runner.FeetY;
        if (!_hasLast)
        {
            _lastRunnerX = x;
            _lastFeetY = feetY;
            _hasLast = true;
        }

        // box his body swept through since last tick, padded by the pickup radius
        double left = Math.Min(_lastRunnerX, x) - CollectRadius;
        double right = Math.Max(_lastRunnerX, x) + CollectRadius;
        float top = MathF.Min(_lastFeetY, feetY) - Runner.HandHeight - CollectRadius;
        float bottom = MathF.Max(_lastFeetY, feetY) + CollectRadius;

        for (int i = 0; i < _orbs.Count; i++)
        {
            var orb = _orbs[i];
            if (orb.X < left || orb.X > right || orb.Y < top || orb.Y > bottom)
                continue;

            _orbs.RemoveAt(i--);
            CollectedThisRun++;
            events.Raise(new EssenceCollected(tick, orb.X, orb.Y, CollectedThisRun, orb.Pack));
        }

        _lastRunnerX = x;
        _lastFeetY = feetY;
    }

    private void Discard(RooftopTrack track)
    {
        double behind = track.ScrollX - DiscardMargin;
        _orbs.RemoveAll(orb => orb.X < behind);
    }
}
