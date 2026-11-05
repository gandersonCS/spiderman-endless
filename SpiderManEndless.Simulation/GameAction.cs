namespace SpiderManEndless.Simulation;

/// <summary>
/// The actions a player can take in this game, in addition to moving and looking.
/// </summary>
/// <remarks>
/// Each value is a bit index in a <see cref="CIS580.Simulation.ActionSet"/>, so values must be
/// between 0 and 31.
/// Menu actions such as pause are not game actions; they are handled outside the simulation.
/// </remarks>
public enum GameAction
{
    /// <summary>
    /// The only button. Jump (hold to go higher), press again in the air and hold to swing, and
    /// press it after dying to restart.
    /// </summary>
    Jump = 0,

    /// <summary>
    /// Attack (left click). Which attack he does depends on what he's doing: a jab punch on a
    /// roof, an upward kick while rising, an air kick while falling. See <see cref="AttackKind"/>.
    /// </summary>
    Attack = 1,
}
