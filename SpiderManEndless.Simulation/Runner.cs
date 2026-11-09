using System;
using CIS580.Simulation;

namespace SpiderManEndless.Simulation;

public enum RunnerState
{
    /// <summary>On a roof, running</summary>
    Running,
    /// <summary>In the air - jumped, ran off a ledge, or let go of a web</summary>
    Airborne,
    /// <summary>Hanging off a web</summary>
    Swinging,
    /// <summary>Game over</summary>
    Dead,
}

/// <summary>
/// Which attack he's doing. They all do the same damage, they just look (and are timed) differently.
/// Picked when the attack starts, from what he's doing at that moment.
/// </summary>
public enum AttackKind
{
    /// <summary>Not attacking</summary>
    None,
    /// <summary>On a roof: jab punch</summary>
    Jab,
    /// <summary>In the air, moving up: upward kick</summary>
    UpwardKick,
    /// <summary>In the air, falling: air kick</summary>
    AirKick,
    /// <summary>
    /// Only from running into a soldier in the air (or just after landing): he uppercuts him
    /// automatically. Not something the attack button does
    /// </summary>
    Uppercut,
}

/// <summary>
/// Input for the runner this tick, merged from all players. Jump button: press on a roof to jump,
/// press again in the air (like a double jump) and hold to swing. Attack button (click): attack.
/// </summary>
public readonly record struct RunnerInput(bool JumpPressed, bool JumpDown, bool AttackPressed = false);

/// <summary>
/// Spider-Man in the simulation. We only track him vertically (feet height + state) since he
/// never actually moves sideways - the <see cref="RooftopTrack"/> scrolls under him instead.
/// </summary>
/// <remarks>
/// <para>
/// Controls: press on a roof to jump, hold for a higher jump, release early for a short hop.
/// I added coyote time and a jump buffer so edge jumps don't feel unfair.
/// </para>
/// <para>
/// Pressing the button again while in the air (a second press, like a double jump) and holding it
/// once he starts falling shoots a web at a spot up and ahead of him and he swings from it. The
/// press you jumped with never turns into a swing, so holding jump for a high jump is safe.
/// No fixed anchor points, you can swing from anywhere.
/// </para>
/// <para>
/// The swing is an actual pendulum sim. He keeps whatever speed he came in with, picks up speed
/// at the bottom, and keeps it when he lets go (the city slows back down to cruising speed over
/// time). Because he's fixed on screen, the swing moves the track instead of him. Rope length
/// scales with speed so swings take roughly the same time no matter how fast you're going.
/// Releasing, or swinging up to the same angle you started at, drops you. Hitting a wall or
/// falling in a gap ends the run.
/// </para>
/// <para>
/// Attacking: one button, three looks. On a roof it's a jab, in the air (jumping or swinging)
/// it's an upward kick if he's going up and an air kick if he's coming down. Attacks don't
/// change his movement. A jab gets cut off if he leaves the roof, and a kick if he lands.
/// Pressing attack during an attack queues the next one if it's close to the end (attack buffer).
/// </para>
/// </remarks>
public sealed class Runner
{
    // tuning values (world units / seconds)
    public const float Gravity = 1900f;
    public const float JumpSpeed = 720f;
    /// <summary>How much upward speed you keep if you let go of jump early</summary>
    public const float JumpCut = 0.45f;
    /// <summary>Distance from hands down to feet while hanging on the web</summary>
    public const float HandHeight = 60f;
    /// <summary>How far below a roof he can be and still step up onto it instead of hitting the wall</summary>
    public const float StepTolerance = 28f;
    public const float CoyoteSeconds = 0.09f;
    public const float JumpBufferSeconds = 0.10f;
    // web anchor goes up and ahead of his hands. "ahead" = how far he'd travel in WebAheadSeconds,
    // which keeps swing timing about the same at any speed. bump WebAheadSeconds up for bigger swings.
    // WebAboveRatio = how much higher the anchor is than it is far ahead
    public const float WebAheadSeconds = 0.4f;
    public const float WebAboveRatio = 1.4f;
    public const float MinWebAhead = 120f;

    /// <summary>
    /// Gravity used during the swing. Turning it up makes the pendulum snappier and gives more of a
    /// speed boost at the bottom.
    /// </summary>
    public const float SwingGravity = 1500f;

    /// <summary>Small forward push during the swing so you gain speed over multiple swings</summary>
    public const float SwingDrive = 150f;

    /// <summary>Cap on how much faster than your current speed a swing can launch you</summary>
    public const float MaxEntryBoost = 1.5f;

    /// <summary>
    /// Chained swings can't push him faster than this many times the cruising speed. Without a
    /// cap each swing launched off the last one's speed, so a long chain kept speeding up until
    /// things came across the screen too fast to react to. Raise it for more speed, lower it for
    /// more control
    /// </summary>
    public const float MaxSwingSpeedRatio = 1.7f;

    /// <summary>
    /// A swing's lowest point stays at least this far above the roofs it passes over, so a swing
    /// never drags him into the side of the next building
    /// </summary>
    public const float SwingRoofClearance = 12f;

    /// <summary>
    /// Ledge grab: hitting the side of a roof with his hands still above its edge (his feet up to
    /// this far below the top) pulls him up onto it instead of ending the run
    /// </summary>
    public const float MantleReach = HandHeight + 16f;

    /// <summary>Fraction of upward speed kept on release, otherwise he rockets off the top of the screen</summary>
    public const float ReleaseUpFraction = 0.4f;

    /// <summary>
    /// Sky limit: letting go of a web can't fling his feet higher than this (y goes down, so
    /// smaller = higher). Without it each chained swing gained a little height (you attach at the
    /// top of the last arc), so you could climb off the top of the screen and stay up there, out
    /// of reach of everything. About the peak of a full jump from the tallest roof, so normal
    /// play doesn't feel it
    /// </summary>
    public const float SkyLimitFeetY = 60f;

    /// <summary>How long after dying before a button press counts as "continue"</summary>
    public const float ContinueDelaySeconds = 0.8f;

    // how long each attack lasts (also how long its animation plays)
    public const float JabSeconds = 0.32f;
    public const float UpwardKickSeconds = 0.35f;
    public const float AirKickSeconds = 0.30f;
    public const float UppercutSeconds = 0.32f;
    /// <summary>An attack press this close to the end of the current attack starts the next one right after</summary>
    public const float AttackBufferSeconds = 0.12f;

    // spots on his body we check for roofs - slightly behind and slightly in front of center
    private const float FootBack = 10f;
    private const float FootFront = 20f;

    private EventQueue<GameEvent> _events;
    private long _tick;
    private float _jumpBuffer;
    private float _coyoteSeconds;
    private bool _canCoyote;
    private bool _jumpHeld;
    private bool _webLocked;
    private float _deadSeconds;
    private float _attackBuffer;

    // pendulum state. theta = rope angle from straight down (negative means he's behind the anchor),
    // omega = how fast it's changing
    private float _theta;
    private float _omega;
    private float _endTheta;

    /// <param name="track">Track to start on - he spawns on the first roof</param>
    public Runner(RooftopTrack track)
    {
        FeetY = track.GroundYAt(track.RunnerTrackX);
    }

    public RunnerState State { get; private set; } = RunnerState.Running;

    /// <summary>Y of his feet</summary>
    public float FeetY { get; private set; }

    /// <summary>Vertical speed in units/sec, negative = up</summary>
    public float VelocityY { get; private set; }

    /// <summary>Time since he left the ground, used by the animations</summary>
    public float AirSeconds { get; private set; }

    /// <summary>Current web anchor, null if not swinging</summary>
    public WebAnchor? Anchor { get; private set; }

    public float RopeLength { get; private set; }

    /// <summary>Time since the web was shot, so the view can animate it flying out</summary>
    public float SwingSeconds { get; private set; }

    /// <summary>Y of his hands (where the web comes out)</summary>
    public float HandY => FeetY - HandHeight;

    public RunEndReason? EndReason { get; private set; }

    /// <summary>Seconds since he died (0 while he's alive)</summary>
    public float DeadSeconds => State == RunnerState.Dead ? _deadSeconds : 0;

    /// <summary>True once he's been dead long enough that a jump press continues</summary>
    public bool CanContinueNow => State == RunnerState.Dead && _deadSeconds >= ContinueDelaySeconds;

    /// <summary>The attack he's doing right now (None if he isn't)</summary>
    public AttackKind Attack { get; private set; }

    /// <summary>Time since the current attack started, for the animation (and hit timing later)</summary>
    public float AttackSeconds { get; private set; }

    /// <summary>True while an attack is happening</summary>
    public bool IsAttacking => Attack != AttackKind.None;

    /// <summary>How long an attack lasts, in seconds</summary>
    public static float DurationOf(AttackKind kind) => kind switch
    {
        AttackKind.Jab => JabSeconds,
        AttackKind.UpwardKick => UpwardKickSeconds,
        AttackKind.AirKick => AirKickSeconds,
        AttackKind.Uppercut => UppercutSeconds,
        _ => 0f,
    };

    /// <summary>
    /// Rope angle from straight up in degrees. Positive if the anchor is in front of him.
    /// </summary>
    public float RopeAngleDegrees(RooftopTrack track)
    {
        if (Anchor is not WebAnchor anchor)
            return 0;
        double ahead = anchor.X - track.RunnerTrackX;
        return MathF.Atan2((float)ahead, HandY - anchor.Y) * 180f / MathF.PI;
    }

    /// <summary>
    /// Steps the runner forward one tick. Returns true when he's dead and the player pressed the
    /// button to continue (the world decides whether they're allowed to).
    /// </summary>
    public bool Update(in RunnerInput input, RooftopTrack track, float dt, EventQueue<GameEvent> events, long tick)
    {
        _events = events;
        _tick = tick;

        _jumpBuffer = input.JumpPressed ? JumpBufferSeconds : MathF.Max(0, _jumpBuffer - dt);
        _attackBuffer = input.AttackPressed ? AttackBufferSeconds : MathF.Max(0, _attackBuffer - dt);
        if (!input.JumpDown)
            _webLocked = false;

        if (State == RunnerState.Dead)
        {
            Attack = AttackKind.None;
            _attackBuffer = 0;

            // fall off screen, then wait for a button press to continue
            _deadSeconds += dt;
            VelocityY += Gravity * dt;
            FeetY += VelocityY * dt;
            return input.JumpPressed && _deadSeconds >= ContinueDelaySeconds;
        }

        switch (State)
        {
            case RunnerState.Running:
                UpdateRunning(input, track, dt);
                break;
            case RunnerState.Airborne:
                UpdateAirborne(input, track, dt);
                break;
            case RunnerState.Swinging:
                UpdateSwinging(input, track, dt);
                break;
        }

        // after moving, so a jump and a click on the same tick gives a kick, not a jab
        UpdateAttack(dt);
        return false;
    }

    /// <summary>
    /// Runs the current attack's timer, cuts it off if he's no longer where it started
    /// (ground vs air), and starts a new one if attack was pressed
    /// </summary>
    private void UpdateAttack(float dt)
    {
        if (State == RunnerState.Dead)
        {
            Attack = AttackKind.None;
            return;
        }

        if (Attack != AttackKind.None)
        {
            AttackSeconds += dt;
            bool onGround = State == RunnerState.Running;
            // a jab only on a roof, kicks only in the air (the uppercut plays out wherever it started)
            bool wrongPlace = Attack != AttackKind.Uppercut && (Attack == AttackKind.Jab) != onGround;
            if (wrongPlace || AttackSeconds >= DurationOf(Attack))
                Attack = AttackKind.None;
        }

        if (Attack == AttackKind.None && _attackBuffer > 0)
            StartAttack();
    }

    private void StartAttack()
    {
        // ground = jab, going up = upward kick, coming down = air kick
        Attack = State == RunnerState.Running ? AttackKind.Jab
            : VelocityY < 0 ? AttackKind.UpwardKick
            : AttackKind.AirKick;
        AttackSeconds = 0;
        _attackBuffer = 0;
        _events.Raise(new RunnerAttacked(_tick, Attack));
    }

    private void UpdateRunning(in RunnerInput input, RooftopTrack track, float dt)
    {
        float? roof = RoofUnderFoot(track);
        if (roof is null)
        {
            // ran off the edge - still give them a few frames to jump (coyote time)
            State = RunnerState.Airborne;
            VelocityY = 0;
            AirSeconds = 0;
            _coyoteSeconds = 0;
            _canCoyote = true;
            UpdateAirborne(input, track, dt);
            return;
        }

        // ease up/down to the roof height instead of snapping
        float ease = 1f - MathF.Exp(-18f * dt);
        FeetY += (roof.Value - FeetY) * ease;

        if (_jumpBuffer > 0)
            Jump();
    }

    private void UpdateAirborne(in RunnerInput input, RooftopTrack track, float dt)
    {
        AirSeconds += dt;

        if (_canCoyote)
        {
            _coyoteSeconds += dt;
            if (_coyoteSeconds > CoyoteSeconds)
                _canCoyote = false;
            else if (_jumpBuffer > 0)
                Jump();
        }

        // short hop if they let go early
        if (_jumpHeld && !input.JumpDown && AirSeconds > 0.06f)
        {
            _jumpHeld = false;
            if (VelocityY < 0)
                VelocityY *= JumpCut;
        }

        // pressed again mid-air (not the press he jumped with) and holding when he starts falling -> web swing.
        // _webLocked is set by Jump/Land/ReleaseWeb and only clears when the button is let go
        if (input.JumpDown && !_webLocked && VelocityY >= 0)
        {
            AttachWeb(track);
            return;
        }

        VelocityY += Gravity * dt;
        FeetY += VelocityY * dt;
        Resolve(track);
    }

    private void UpdateSwinging(in RunnerInput input, RooftopTrack track, float dt)
    {
        AirSeconds += dt;
        SwingSeconds += dt;
        var anchor = Anchor.Value;

        if (!input.JumpDown)
        {
            ReleaseWeb(track);
            return;
        }

        // pendulum update: gravity pulls him back toward hanging straight down, plus a small push
        // while moving forward. split into substeps so it doesn't drift
        const int steps = 4;
        float h = dt / steps;
        for (int i = 0; i < steps; i++)
        {
            float alpha = -(SwingGravity / RopeLength) * MathF.Sin(_theta);
            if (_omega > 0)
                alpha += SwingDrive / RopeLength;
            _omega += alpha * h;
            _theta += _omega * h;
        }

        // get his position/velocity from the pendulum
        float sin = MathF.Sin(_theta), cos = MathF.Cos(_theta);
        double handX = anchor.X + RopeLength * sin;
        float handY = anchor.Y + RopeLength * cos;
        float speedX = RopeLength * _omega * cos;
        VelocityY = -RopeLength * _omega * sin;
        FeetY = handY + HandHeight;

        // he doesn't move on screen, so scroll the city to match the swing
        track.DriveScroll(handX - RooftopTrack.RunnerX, speedX);

        Resolve(track);
        if (State != RunnerState.Swinging)
            return;

        // done once he's swung round to the mirror of his starting angle, or he's basically stopped
        if (_theta >= _endTheta || _omega <= 0.05f)
            ReleaseWeb(track);
    }

    /// <summary>
    /// Brings him back after a death: standing on the roof at his spot on the track (the track
    /// has just moved to a safe one, see <see cref="RooftopTrack.Resume"/>)
    /// </summary>
    internal void Revive(RooftopTrack track)
    {
        State = RunnerState.Running;
        EndReason = null;
        Anchor = null;
        FeetY = track.GroundYAt(track.RunnerTrackX);
        VelocityY = 0;
        AirSeconds = 0;
        SwingSeconds = 0;
        _deadSeconds = 0;
        _jumpBuffer = 0;        // the press that continued shouldn't also make him jump
        _coyoteSeconds = 0;
        _canCoyote = false;
        _jumpHeld = false;
        _webLocked = true;      // ...or swing, if they're still holding the button
        Attack = AttackKind.None;
        AttackSeconds = 0;
        _attackBuffer = 0;      // ...or attack, if they clicked the Continue button
    }

    private void Jump()
    {
        State = RunnerState.Airborne;
        VelocityY = -JumpSpeed;
        AirSeconds = 0;
        _jumpBuffer = 0;
        _canCoyote = false;
        _jumpHeld = true;
        _webLocked = true;      // the jump press can't also swing - let go and press again in the air
        _events.Raise(new RunnerJumped(_tick));
    }

    private void AttachWeb(RooftopTrack track)
    {
        // chained swings build on the speed he already has, up to a cap
        float speedCap = track.CruiseSpeed * MaxSwingSpeedRatio;
        float speedX = MathF.Min(track.Speed, speedCap);

        // stick the web up and ahead of him
        float ahead = MathF.Max(MinWebAhead, speedX * WebAheadSeconds);
        float above = ahead * WebAboveRatio;

        // the swing dips (rope - above) below where it starts. If that would drag him into a roof
        // he's swinging over, hang the web higher (a steeper rope dips less)
        float dip = MathF.Sqrt(ahead * ahead + above * above) - above;
        float allowed = HighestRoofBetween(track, track.RunnerTrackX, track.RunnerTrackX + 2 * ahead)
                        - SwingRoofClearance - FeetY;
        if (allowed < dip)
        {
            float d = MathF.Max(allowed, 6f);
            above = MathF.Min(MathF.Max(above, (ahead * ahead - d * d) / (2 * d)), MaxWebAbove);
        }

        Anchor = new WebAnchor(track.RunnerTrackX + ahead, HandY - above);
        RopeLength = MathF.Sqrt(ahead * ahead + above * above);

        // he starts behind the anchor so theta is negative, and the swing ends at the same angle
        // on the other side
        _theta = MathF.Atan2(-ahead, above);
        _endTheta = -_theta;

        // turn his current speed into swing speed without losing forward momentum. without this
        // it feels like slamming into a rope
        float cos = MathF.Cos(_theta);
        float speed = MathF.Sqrt(speedX * speedX + VelocityY * VelocityY);
        float tangentSpeed = MathF.Min(MathF.Max(speed, speedX / cos), MaxEntryBoost * speedX / cos);
        tangentSpeed = MathF.Min(tangentSpeed, speedCap / cos);
        _omega = tangentSpeed / RopeLength;

        State = RunnerState.Swinging;
        SwingSeconds = 0;
        track.ScrollDrivenByRunner = true;
        _canCoyote = false;
        _jumpHeld = false;
        _events.Raise(new WebAttached(_tick));
    }

    /// <summary>Longest a web can hang above his hands (when it's hung high to clear the roofs)</summary>
    private const float MaxWebAbove = 1400f;

    /// <summary>The highest roof top (smallest y) between two track x's, or the bottom of the screen if it's all gap</summary>
    private static float HighestRoofBetween(RooftopTrack track, double from, double to)
    {
        float highest = RooftopTrack.Rows * RooftopTrack.TileSize;
        for (double x = from; x <= to; x += RooftopTrack.TileSize / 2.0)
        {
            if (track.RoofAt(x) is float roof)
                highest = MathF.Min(highest, roof);
        }
        return highest;
    }

    private void ReleaseWeb(RooftopTrack track)
    {
        State = RunnerState.Airborne;
        Anchor = null;
        track.ScrollDrivenByRunner = false;     // city keeps the swing's speed
        _canCoyote = false;
        _webLocked = true;       // have to release and press again to swing again
        if (VelocityY < 0)
        {
            VelocityY *= ReleaseUpFraction;

            // cap the upward speed so the top of the arc is at the sky limit (v = sqrt(2 g h))
            float room = MathF.Max(0f, FeetY - SkyLimitFeetY);
            VelocityY = MathF.Max(VelocityY, -MathF.Sqrt(2f * Gravity * room));
        }
        _events.Raise(new WebReleased(_tick));
    }

    /// <summary>
    /// Whichever roof is higher between the spot behind his feet and the spot in front. Null if both are gaps.
    /// Counts the tops of things he can stand on (landed pylons) if he's above them.
    /// </summary>
    private float? RoofUnderFoot(RooftopTrack track)
    {
        double x = track.RunnerTrackX;
        float? back = track.SurfaceAt(x - FootBack, FeetY);
        float? front = track.SurfaceAt(x + FootFront, FeetY);
        if (back is null) return front;
        if (front is null) return back;
        return MathF.Min(back.Value, front.Value);
    }

    /// <summary>
    /// Handles landing on a roof, crashing into a wall, or falling off the bottom
    /// </summary>
    private void Resolve(RooftopTrack track)
    {
        float? roof = RoofUnderFoot(track);
        if (roof is float surface && FeetY > surface)
        {
            float depth = FeetY - surface;
            if (depth > MantleReach)
            {
                Die(track, RunEndReason.HitWall);
                return;
            }
            if (depth > StepTolerance)
            {
                // caught the edge with his hands: pull up onto the roof
                Land(track, surface);
                return;
            }
            if (VelocityY >= 0)
            {
                Land(track, surface);
                return;
            }
        }

        if (FeetY > RooftopTrack.Rows * RooftopTrack.TileSize + 140)
            Die(track, RunEndReason.Fell);
    }

    private void Land(RooftopTrack track, float surface)
    {
        float fallSpeed = VelocityY;
        State = RunnerState.Running;
        FeetY = surface;
        VelocityY = 0;
        AirSeconds = 0;
        Anchor = null;
        track.ScrollDrivenByRunner = false;
        _webLocked = true;       // don't auto-swing if they land still holding the button
        _events.Raise(new RunnerLanded(_tick, fallSpeed));
    }

    /// <summary>
    /// He touched a soldier: plays the attack that fits (attacks are automatic, see
    /// <see cref="HazardField.ContactAttack"/>)
    /// </summary>
    internal void AutoAttack(AttackKind kind)
    {
        if (State == RunnerState.Dead || kind == AttackKind.None)
            return;
        Attack = kind;
        AttackSeconds = 0;
        _attackBuffer = 0;
        _events.Raise(new RunnerAttacked(_tick, Attack));
    }

    /// <summary>
    /// Puts him on top of something (a pylon he ran into near its top, or just after landing),
    /// running on, instead of letting it kill him
    /// </summary>
    internal void VaultOnto(RooftopTrack track, float top)
    {
        if (State == RunnerState.Dead)
            return;
        if (State == RunnerState.Running)
        {
            FeetY = top;
            VelocityY = 0;
        }
        else
        {
            Land(track, top);
        }
    }

    /// <summary>
    /// Ends the run because something got him (a bullet, a pylon, a pumpkin bomb, a soldier).
    /// Everything is one hit. Does nothing if he's already dead.
    /// </summary>
    internal void Kill(RooftopTrack track, RunEndReason reason)
    {
        if (State == RunnerState.Dead)
            return;
        Die(track, reason);
    }

    private void Die(RooftopTrack track, RunEndReason reason)
    {
        State = RunnerState.Dead;
        EndReason = reason;
        Anchor = null;
        Attack = AttackKind.None;
        track.ScrollDrivenByRunner = false;
        _deadSeconds = 0;
        track.IsScrolling = false;
        if (reason == RunEndReason.HitWall)
            VelocityY = -250f;       // little bounce off the wall
        else if (reason is RunEndReason.Shot or RunEndReason.Blasted or RunEndReason.Tackled or RunEndReason.HitPylon)
            VelocityY = -380f;       // knocked up into the air before he falls
        else if (reason == RunEndReason.Crushed)
            VelocityY = 0f;
        _events.Raise(new RunEnded(_tick, track.DistanceMeters, reason));
    }
}
