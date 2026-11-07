using Microsoft.Xna.Framework;
using CIS580.UI;

namespace SpiderManEndless.UI;

/// <summary>
/// "Continue for 50 Spider-Essence?" - asked on the death screen before a continue spends anything.
/// Confirm continues, cancel goes back to the death screen.
/// </summary>
public sealed class ContinuePromptLayer : PromptLayer
{
    private bool _ready;

    /// <param name="cost">What this continue costs</param>
    /// <param name="have">How much Spider-Essence they have (null if there's no save to charge)</param>
    public ContinuePromptLayer(int cost, int? have)
        : base(have is int total
            ? $"Continue for {cost} Spider-Essence?\n(you have {total})"
            : $"Continue for {cost} Spider-Essence?")
    {
    }

    public override void HandleInput(GameTime gameTime)
    {
        // skip the frame it opened on, so the press that opened it doesn't also answer it
        if (!_ready)
        {
            _ready = true;
            return;
        }
        base.HandleInput(gameTime);
    }
}
