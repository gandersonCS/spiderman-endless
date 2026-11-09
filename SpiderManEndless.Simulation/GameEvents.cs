using CIS580.Simulation;

namespace SpiderManEndless.Simulation;

/// <summary>
/// Base type for the events the simulation raises for the rest of the game to react to
/// (e.g. by playing a sound). Add your own events by deriving from this record.
/// </summary>
/// <param name="Tick">The tick on which the event happened</param>
public abstract record GameEvent(long Tick);

/// <summary>
/// A player joined the game
/// </summary>
public sealed record PlayerJoined(long Tick, PlayerId Player) : GameEvent(Tick);

/// <summary>
/// A player left the game
/// </summary>
public sealed record PlayerLeft(long Tick, PlayerId Player) : GameEvent(Tick);

/// <summary>
/// Fired when Spider-Man jumps
/// </summary>
public sealed record RunnerJumped(long Tick) : GameEvent(Tick);

/// <summary>
/// Fired when he lands on a roof
/// </summary>
/// <param name="FallSpeed">Fall speed when he hit (units/sec), used to make hard landings feel heavier</param>
public sealed record RunnerLanded(long Tick, float FallSpeed) : GameEvent(Tick);

/// <summary>
/// Fired when his web sticks
/// </summary>
public sealed record WebAttached(long Tick) : GameEvent(Tick);

/// <summary>
/// Fired when he lets go of the web
/// </summary>
public sealed record WebReleased(long Tick) : GameEvent(Tick);

/// <summary>
/// Fired when he starts an attack (doesn't mean it hit anything)
/// </summary>
/// <param name="Kind">Which attack - they all do the same damage, they just look different</param>
public sealed record RunnerAttacked(long Tick, AttackKind Kind) : GameEvent(Tick);

/// <summary>
/// How the run ended
/// </summary>
public enum RunEndReason
{
    /// <summary>Fell down a gap</summary>
    Fell,
    /// <summary>Ran or swung into the side of a building</summary>
    HitWall,
    /// <summary>A task force soldier's bullet got him</summary>
    Shot,
    /// <summary>A falling pylon landed on him</summary>
    Crushed,
    /// <summary>Ran into a pylon on a roof</summary>
    HitPylon,
    /// <summary>A pumpkin bomb blew up on (or next to) him</summary>
    Blasted,
    /// <summary>Ran into a task force soldier without attacking</summary>
    Tackled,
}

/// <summary>
/// Run's over. The game saves the distance when it sees this, along with the essence collected
/// (<see cref="EssenceField.CollectedThisRun"/>).
/// </summary>
public sealed record RunEnded(long Tick, double DistanceMeters, RunEndReason Reason) : GameEvent(Tick);

/// <summary>
/// The player continued after dying and picked the run back up. The game takes <paramref name="Cost"/>
/// out of the player's total Spider-Essence when it sees this.
/// </summary>
/// <param name="ContinuesUsed">How many continues this run has used, including this one</param>
/// <param name="Cost">Spider-Essence this continue costs</param>
/// <param name="DistanceMeters">Distance when he died (it carries on from here)</param>
public sealed record RunContinued(long Tick, int ContinuesUsed, int Cost, double DistanceMeters) : GameEvent(Tick);

/// <summary>
/// Fired when he grabs a Spider-Essence orb
/// </summary>
/// <param name="X">Orb's x along the track</param>
/// <param name="Y">Orb's y from the top of the screen</param>
/// <param name="CollectedThisRun">Total collected this run, including this one</param>
/// <param name="Pack">Which pack the orb came from (see <see cref="EssenceOrb.Pack"/>), so audio can tell a
/// streak through one pack from a new pack</param>
public sealed record EssenceCollected(long Tick, double X, float Y, int CollectedThisRun, int Pack) : GameEvent(Tick);

/// <summary>
/// Something dangerous just started coming at him: a soldier started aiming, a pylon or pumpkin
/// bomb started falling, or a bomb was thrown in from the side. The spider sense shows over his
/// head while any threat is active (see <see cref="HazardField.SpiderSenseActive"/>); this event is
/// for the tingle sound.
/// </summary>
public sealed record SpiderSenseTriggered(long Tick, ThreatKind Kind) : GameEvent(Tick);

/// <summary>A soldier fired a bullet from (X, Y) in track space</summary>
public sealed record BulletFired(long Tick, double X, float Y) : GameEvent(Tick);

/// <summary>
/// He hit a soldier (punch sound, screen shake, hit-stop). The soldier flies off and bursts into
/// essence a moment later (<see cref="EnemyExploded"/>).
/// </summary>
/// <param name="X">Soldier's track x</param>
/// <param name="Y">Middle of the soldier's body</param>
/// <param name="Combo">Soldiers beaten in a row, including this one</param>
/// <param name="Multiplier">What the combo multiplies this soldier's essence by</param>
public sealed record EnemyDefeated(long Tick, double X, float Y, EnemyKind Kind, bool WasShooter, int Combo, int Multiplier)
    : GameEvent(Tick);

/// <summary>A beaten soldier burst into <paramref name="Essence"/> Spider-Essence, which flies to Spider-Man</summary>
public sealed record EnemyExploded(long Tick, double X, float Y, int Essence) : GameEvent(Tick);

/// <summary>A falling pylon hit the roof at (X, Y) (its bottom center)</summary>
public sealed record PylonLanded(long Tick, double X, float Y) : GameEvent(Tick);

/// <summary>A pumpkin bomb went off at (X, Y)</summary>
public sealed record PumpkinExploded(long Tick, double X, float Y) : GameEvent(Tick);

/// <summary>
/// A close call: a spider-sense threat went by without hitting him. Worth a little essence.
/// (X, Y) is above his head, where the view shows the popup.
/// </summary>
public sealed record NearMiss(long Tick, double X, float Y, int Essence) : GameEvent(Tick);

/// <summary>What the task force is set up to deal with, from how he's been playing</summary>
public enum TaskForceFocus
{
    /// <summary>The start of a run: a bit of everything</summary>
    Mixed,
    /// <summary>He's been swinging a lot: more flyers and pumpkin bombs, at the height he swings</summary>
    Air,
    /// <summary>He's been on the roofs: more rooftop soldiers and pylons</summary>
    Ground,
}

/// <summary>
/// The task force switched focus to match how he's playing, or (if <paramref name="Level"/> is
/// above 1) is doubling down because he's still playing the same way. The view shows a callout
/// </summary>
public sealed record TaskForceAdapted(long Tick, TaskForceFocus Focus, int Level = 1) : GameEvent(Tick);

/// <summary>The Daily Bugle boards that teach the game</summary>
public enum NewsBoard
{
    /// <summary>The controls: jump, hold for higher, press again in the air to swing. At the very start</summary>
    Controls,
    /// <summary>Trust your spider sense. Early in the run, before anything dangerous shows up</summary>
    SpiderSense,
    /// <summary>Touch soldiers to take them down, dodge their shots. Right before the first normal soldier</summary>
    Soldiers,
}

/// <summary>Time to put up a Daily Bugle board (the view decides where and how it's drawn)</summary>
public sealed record NewsBoardRaised(long Tick, NewsBoard Board) : GameEvent(Tick);

/// <summary>A pumpkin bomb finished its warning and launched along its line</summary>
public sealed record PumpkinLaunched(long Tick, double X, float Y) : GameEvent(Tick);
