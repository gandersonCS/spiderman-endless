using System;

namespace SpiderManEndless.Simulation;

/// <summary>
/// An axis-aligned box in track space (X = distance along the track, Y = from the top of the
/// screen), used for all the hazard collisions
/// </summary>
public readonly record struct HitBox(double Left, float Top, double Right, float Bottom)
{
    /// <summary>A box centered on x, with its bottom at <paramref name="bottom"/></summary>
    public static HitBox Standing(double x, float bottom, float halfWidth, float height)
        => new(x - halfWidth, bottom - height, x + halfWidth, bottom);

    /// <summary>A box centered on a point</summary>
    public static HitBox Around(double x, float y, float halfWidth, float halfHeight)
        => new(x - halfWidth, y - halfHeight, x + halfWidth, y + halfHeight);

    public bool Overlaps(in HitBox other)
        => Left < other.Right && other.Left < Right && Top < other.Bottom && other.Top < Bottom;

    /// <summary>Distance from a point to the closest point of the box (0 if it's inside)</summary>
    public float DistanceTo(double x, float y)
    {
        double dx = Math.Max(Math.Max(Left - x, 0), x - Right);
        float dy = MathF.Max(MathF.Max(Top - y, 0), y - Bottom);
        return (float)Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>Gap between two boxes (0 if they touch or overlap)</summary>
    public float DistanceTo(in HitBox other)
    {
        double dx = Math.Max(Math.Max(Left - other.Right, 0), other.Left - Right);
        float dy = MathF.Max(MathF.Max(Top - other.Bottom, 0), other.Top - Bottom);
        return (float)Math.Sqrt(dx * dx + dy * dy);
    }
}

/// <summary>Where a task force soldier is</summary>
public enum EnemyKind
{
    /// <summary>Stands (or runs at you) on a roof</summary>
    Rooftop,
    /// <summary>Flies at you on a jetpack</summary>
    Flyer,
}

/// <summary>Which way a shooter is pointing its gun. The view picks the matching frame.</summary>
public enum AimPose { Level, Up, Down }

/// <summary>What set off the spider sense</summary>
public enum ThreatKind
{
    /// <summary>A soldier taking aim</summary>
    Shooter,
    /// <summary>A task force pylon dropping out of the sky</summary>
    FallingPylon,
    /// <summary>A shielded soldier on the roof ahead (jump or swing over him)</summary>
    ShieldedSoldier,
    /// <summary>A pumpkin bomb flying in from the right</summary>
    PumpkinBomb,
}

/// <summary>
/// A task force soldier. Some shoot at you (spider sense goes off while they aim), some don't
/// (safe to punch). Any of them can be beaten with an attack, and running into one without
/// attacking ends the run.
/// </summary>
/// <remarks>Position is the bottom center of the sprite (his feet).</remarks>
public sealed class Enemy
{
    /// <summary>Half the width of his hurt box (world units)</summary>
    public const float BodyHalfWidth = 15f;
    /// <summary>Height of his hurt box (world units). The sprite is drawn at 1.5x, about 69 tall.</summary>
    public const float BodyHeight = 64f;

    internal Enemy(int id, EnemyKind kind, bool isShooter, double x, float y, float speed)
    {
        Id = id;
        Kind = kind;
        IsShooter = isShooter;
        X = x;
        Y = y;
        HoverY = y;
        Speed = speed;
    }

    public int Id { get; }
    public EnemyKind Kind { get; }

    /// <summary>True if this one shoots at you. Shooters are dangerous to go near.</summary>
    public bool IsShooter { get; }

    /// <summary>
    /// A rooftop soldier behind a riot shield. Can't be beaten and never shoots: he's a wall
    /// on the roof. Jump or swing over him (touching him ends the run)
    /// </summary>
    public bool IsShielded { get; internal init; }

    /// <summary>True once a shielded soldier has set off the spider sense (it only tingles once each)</summary>
    internal bool Sensed { get; set; }

    /// <summary>True once a shielded soldier has tingled a second time, when he got close</summary>
    internal bool SensedClose { get; set; }

    /// <summary>True once he's gone by under you (a shielded soldier only counts as a close call once)</summary>
    internal bool Passed { get; set; }

    /// <summary>Closest a shielded soldier got to him, for close calls</summary>
    internal float Closest { get; set; } = float.MaxValue;

    /// <summary>What touching a shielded soldier means touching: him and the shield held out in front</summary>
    public HitBox ShieldedBody => new(X - 30, Y - BodyHeight + 2, X + BodyHalfWidth - 5, Y);

    /// <summary>Track x of his center</summary>
    public double X { get; internal set; }

    /// <summary>Y of his feet</summary>
    public float Y { get; internal set; }

    /// <summary>Flyers bob around this height</summary>
    internal float HoverY { get; }

    /// <summary>
    /// Track-space speed. Rooftop runners move left (toward you) at this, flyers fly at you at
    /// this; shooters use it differently (see <see cref="HazardField"/>)
    /// </summary>
    internal float Speed { get; }

    /// <summary>Seconds since he spawned, for animations</summary>
    public float Age { get; internal set; }

    /// <summary>True while running toward you on a roof (the view plays the run cycle)</summary>
    public bool IsRunning { get; internal set; }

    // ---- shooting ----

    /// <summary>Seconds into the current aim, or -1 if he isn't aiming</summary>
    public float AimSeconds { get; internal set; } = -1;

    /// <summary>How long this aim takes before the shot (gets shorter as the run goes on)</summary>
    public float AimDuration { get; internal set; }

    /// <summary>True while he's lining up a shot (spider sense is going off)</summary>
    public bool IsAiming => AimSeconds >= 0;

    /// <summary>Aim direction, a unit vector in screen space (x is negative: he aims left at you)</summary>
    public float AimX { get; internal set; } = -1;
    public float AimY { get; internal set; }

    /// <summary>True for the last moment of the aim, when the aim stops following you (dodge now!)</summary>
    public bool AimLocked { get; internal set; }

    /// <summary>Which aim frame to draw</summary>
    public AimPose Pose { get; internal set; }

    internal int ShotsFired { get; set; }
    internal float Cooldown { get; set; }

    // ---- getting hit ----

    /// <summary>True once he's been hit and is flying away (he can't hurt you anymore)</summary>
    public bool IsKnocked { get; internal set; }

    /// <summary>Seconds since he was hit</summary>
    public float KnockedSeconds { get; internal set; }

    /// <summary>Rotation while flying away (radians)</summary>
    public float Spin { get; internal set; }

    internal float KnockVX { get; set; }
    internal float KnockVY { get; set; }
    internal float SpinSpeed { get; set; }

    /// <summary>Spider-Essence he bursts into</summary>
    internal int EssenceValue { get; set; }

    /// <summary>His hurt box (what Spider-Man's attacks have to reach)</summary>
    public HitBox Body => HitBox.Standing(X, Y, BodyHalfWidth, BodyHeight);

    /// <summary>
    /// What Spider-Man has to touch to get tackled: a bit smaller than he looks, so brushing
    /// past him doesn't count
    /// </summary>
    public HitBox ContactBody => HitBox.Standing(X, Y - 4, BodyHalfWidth - 5, BodyHeight - 12);

    /// <summary>Where his bullets come out (track space), for the current pose</summary>
    public (double X, float Y) Muzzle
    {
        get
        {
            var (dx, dy) = Pose switch
            {
                AimPose.Up => HazardField.MuzzleUp,
                AimPose.Down => HazardField.MuzzleDown,
                _ => HazardField.MuzzleLevel,
            };
            return (X + dx, Y + dy);
        }
    }
}

/// <summary>A bullet: drawn in code as a glowing yellow pixel with a pulsing red outline</summary>
public sealed class Bullet
{
    internal Bullet(double x, float y, float vx, float vy)
    {
        X = x;
        Y = y;
        VelocityX = vx;
        VelocityY = vy;
    }

    /// <summary>Track x</summary>
    public double X { get; internal set; }
    public float Y { get; internal set; }

    /// <summary>Velocity on screen (units/sec). Bullets move relative to the screen, not the track.</summary>
    public float VelocityX { get; }
    public float VelocityY { get; }

    public float Age { get; internal set; }

    /// <summary>Closest it got to him, for close calls</summary>
    internal float Closest { get; set; } = float.MaxValue;
}

/// <summary>
/// A task force pylon. Either sits on a roof or drops straight down out of the sky onto one (it
/// never aims at you). Either way it ends up near the middle of a roof, away from where you'd land.
/// Running into its side ends the run; its top is solid, like a roof, so you can land and run on it.
/// </summary>
/// <remarks>Position is the bottom center (where it touches the roof).</remarks>
public sealed class Pylon
{
    /// <summary>Half the width of the hit box (the pylon's body; the flared base is a bit wider)</summary>
    public const float HalfWidth = 17f;
    /// <summary>Height of the pylon (32px sprite at 2x)</summary>
    public const float Height = 62f;

    internal Pylon(double x, float y, float roofY, float fallSeconds)
    {
        X = x;
        Y = y;
        RoofY = roofY;
        FallSeconds = fallSeconds;
        IsFalling = fallSeconds > 0;
        StartY = y;
    }

    /// <summary>Track x of its center</summary>
    public double X { get; }

    /// <summary>Y of its bottom</summary>
    public float Y { get; internal set; }

    /// <summary>Y of the roof it's on or will land on</summary>
    public float RoofY { get; }

    /// <summary>True while it's still dropping</summary>
    public bool IsFalling { get; internal set; }

    /// <summary>How long the drop takes in total (0 for one that started on the roof)</summary>
    public float FallSeconds { get; }

    /// <summary>Seconds since it spawned</summary>
    public float Age { get; internal set; }

    /// <summary>0 when it starts falling, 1 when it lands (always 1 once it's on the roof)</summary>
    public float FallProgress => IsFalling ? Math.Clamp(Age / FallSeconds, 0f, 1f) : 1f;

    internal float StartY { get; }
    internal float Closest { get; set; } = float.MaxValue;

    public HitBox Box => HitBox.Standing(X, Y, HalfWidth, Height);

    /// <summary>Y of its top (what he stands on)</summary>
    public float Top => Y - Height;
}

/// <summary>
/// A pumpkin bomb. Appears at the right edge of the screen at the height of his chest, hangs there
/// with its fuse lit and its flight line shown for a moment, then launches straight left along that
/// line (it doesn't follow him), fast. Blows up if he touches it. Blows up on whatever it hits (including you).
/// </summary>
public sealed class PumpkinBomb
{
    /// <summary>Radius of the bomb itself</summary>
    public const float Radius = 11f;

    /// <summary>How far the explosion reaches (from the bomb's center)</summary>
    public const float BlastRadius = 40f;

    internal PumpkinBomb(double x, float y, float vx)
    {
        X = x;
        Y = y;
        BaseY = y;
        VelocityX = vx;
    }

    /// <summary>The height of its flight line</summary>
    public float BaseY { get; }

    /// <summary>A bomb that hangs at the edge for <paramref name="warnSeconds"/>, then launches at <paramref name="speed"/></summary>
    internal static PumpkinBomb Hovering(double x, float y, float speed, float warnSeconds)
        => new(x, y, 0) { WarnSeconds = warnSeconds, LaunchSpeed = speed, IsLaunched = false };

    /// <summary>How long it hangs before launching</summary>
    public float WarnSeconds { get; private init; }

    /// <summary>False while it's hanging at the edge of the screen (the warning)</summary>
    public bool IsLaunched { get; private set; } = true;

    private float LaunchSpeed { get; init; }

    internal void Launch()
    {
        IsLaunched = true;
        VelocityX = -LaunchSpeed;
    }

    /// <summary>Track x of its center</summary>
    public double X { get; internal set; }
    public float Y { get; internal set; }

    /// <summary>Speed on screen (negative = moving left, toward him; 0 while it hangs at the edge)</summary>
    public float VelocityX { get; private set; }

    public float Age { get; internal set; }

    /// <summary>Closest it got to blowing him up (distance minus the blast radius), for close calls</summary>
    internal float Closest { get; set; } = float.MaxValue;
}
