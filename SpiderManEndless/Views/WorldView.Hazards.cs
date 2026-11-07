using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using CIS580.UI;
using CIS580.Views;
using SpiderManEndless.Simulation;

namespace SpiderManEndless.Views;

/// <summary>
/// The hazards half of the world view: soldiers, pylons, pumpkin bombs, bullets, the shadows
/// under falling things, essence flying to him, the spider sense over his head, the combo
/// counter, and the little effects (explosions, dust, popups) that the simulation's events set off.
/// </summary>
public partial class WorldView
{
    private static readonly Color BulletCore = new(255, 236, 90);
    private static readonly Color BulletOutline = new(235, 30, 30);
    private static readonly Color AimColor = new(255, 40, 40);
    private static readonly Color ComboColor = new(255, 210, 60);
    private static readonly Color CloseCallColor = new(120, 230, 255);
    private static readonly Color JetColor = new(255, 150, 40);
    private static readonly Color BlastColor = new(255, 140, 30);

    /// <summary>Spider-Man's tint while he's invulnerable (after beating a soldier or a close call)</summary>
    private static readonly Color InvulnerableTint = new(140, 225, 255);
    private static readonly Color InvulnerableGlow = new(110, 210, 255);

    /// <summary>What Spider-Man is drawn tinted with this frame</summary>
    private Color _spideyTint = Color.White;

    // short-lived effects started by events. Times are on the view's clock (_clock)
    private enum EffectKind { HitFlash, MuzzleFlash, EssencePop, PumpkinBlast, PylonDust }
    private readonly record struct HazardEffect(EffectKind Kind, double X, float Y, float Start, float Seconds);
    private readonly List<HazardEffect> _effects = new();

    // text that floats up and fades ("CLOSE CALL! +3", "x3 COMBO!")
    private readonly record struct Popup(string Text, double X, float Y, float Start, Color Color);
    private readonly List<Popup> _popups = new();
    private const float PopupSeconds = 0.9f;
    private const float PopupScale = 0.42f;

    private const float HomingRadius = 3.5f;

    /// <summary>Reacts to the hazard events with effects and popups</summary>
    private void OnHazardEvent(GameEvent gameEvent)
    {
        switch (gameEvent)
        {
            case EnemyDefeated defeated:
                _effects.Add(new HazardEffect(EffectKind.HitFlash, defeated.X, defeated.Y, _clock, 0.16f));
                if (defeated.Multiplier >= 2)
                    _popups.Add(new Popup($"x{defeated.Multiplier} COMBO!", defeated.X, defeated.Y - 46, _clock, ComboColor));
                break;
            case EnemyExploded exploded:
                _effects.Add(new HazardEffect(EffectKind.EssencePop, exploded.X, exploded.Y, _clock, 0.35f));
                break;
            case BulletFired fired:
                _effects.Add(new HazardEffect(EffectKind.MuzzleFlash, fired.X, fired.Y, _clock, 0.09f));
                break;
            case PumpkinExploded blast:
                _effects.Add(new HazardEffect(EffectKind.PumpkinBlast, blast.X, blast.Y, _clock, 0.55f));
                break;
            case PylonLanded landed:
                _effects.Add(new HazardEffect(EffectKind.PylonDust, landed.X, landed.Y, _clock, 0.4f));
                break;
            case RunEnded:
                PickDeathLine();
                break;
            case NearMiss miss:
                _popups.Add(new Popup($"CLOSE CALL! +{miss.Essence}", miss.X, miss.Y - 20, _clock, CloseCallColor));
                break;
        }
    }

    private void UpdateHazardEffects()
    {
        _effects.RemoveAll(e => _clock - e.Start > e.Seconds);
        _popups.RemoveAll(p => _clock - p.Start > PopupSeconds);
    }

    /// <summary>
    /// Shadows on the roofs under falling pylons. They grow and darken as the thing
    /// gets closer, so you can see where it's going to land
    /// </summary>
    private void DrawDropShadows(HazardField hazards, RooftopTrack track)
    {
        if (_essenceArt == null)
            return;

        foreach (var pylon in hazards.Pylons)
        {
            if (pylon.IsFalling)
                DrawShadow(pylon.X - track.ScrollX, pylon.RoofY, pylon.FallProgress, 18, 58);
        }
    }

    private void DrawShadow(double screenX, float roofY, float progress, float smallWidth, float bigWidth)
    {
        float width = MathHelper.Lerp(smallWidth, bigWidth, progress);
        float height = width * 0.3f;
        float alpha = MathHelper.Lerp(0.18f, 0.6f, progress);
        var texture = _essenceArt.Disc;
        var origin = new Vector2(texture.Width / 2f, texture.Height / 2f);
        var scale = new Vector2(width / texture.Width, height / texture.Height);
        SpriteBatch.Draw(texture, new Vector2((float)screenX, roofY + 1), null, Color.Black * alpha, 0, origin, scale,
            SpriteEffects.None, 0);
    }

    /// <summary>Pylons, pumpkin bombs and soldiers (pixel art batch)</summary>
    private void DrawHazards(HazardField hazards, RooftopTrack track, float left, float right)
    {
        if (_hazardSprites == null)
            return;

        foreach (var pylon in hazards.Pylons)
        {
            float x = (float)(pylon.X - track.ScrollX);
            if (x < left - 40 || x > right + 40)
                continue;

            // the light pulses; faster while it's falling at you
            float rate = pylon.IsFalling ? 20 : 7;
            int frame = (int)(pylon.Age * rate + pylon.X * 0.01) % HazardSprites.PylonFrames;
            SpriteBatch.Draw(_hazardSprites.Pylon, new Vector2(MathF.Round(x), MathF.Round(pylon.Y)),
                HazardSprites.PylonFrame(frame), Color.White, 0, HazardSprites.PylonBottom, HazardSprites.PylonScale,
                SpriteEffects.None, 0);
        }

        foreach (var bomb in hazards.Pumpkins)
        {
            float x = (float)(bomb.X - track.ScrollX);
            if (x < left - 30 || x > right + 30)
                continue;

            // tumbles toward you
            float rotation = -bomb.Age * 9;
            SpriteBatch.Draw(_hazardSprites.Pumpkin, new Vector2(x, bomb.Y), null, Color.White, rotation,
                HazardSprites.PumpkinCenter, HazardSprites.PumpkinScale, SpriteEffects.None, 0);
        }

        foreach (var enemy in hazards.Enemies)
        {
            float x = (float)(enemy.X - track.ScrollX);
            if (x < left - 60 || x > right + 60)
                continue;
            DrawEnemy(enemy, x);
        }
    }

    private void DrawEnemy(Enemy enemy, float x)
    {
        var texture = _hazardSprites.Soldier;

        if (enemy.IsKnocked)
        {
            // spinning away from the hit, flashing red
            var middle = new Vector2(x, enemy.Y - (HazardSprites.SoldierFeet.Y - HazardSprites.SoldierMiddle.Y) * HazardSprites.SoldierScale);
            var tint = (int)(enemy.KnockedSeconds * 30) % 2 == 0 ? Color.White : new Color(255, 120, 120);
            SpriteBatch.Draw(texture, middle, HazardSprites.SoldierFrame(HazardSprites.HurtFrame), tint, enemy.Spin,
                HazardSprites.SoldierMiddle, HazardSprites.SoldierScale, SpriteEffects.None, 0);
            return;
        }

        int frame;
        if (enemy.IsAiming)
        {
            frame = enemy.Pose switch
            {
                AimPose.Up => HazardSprites.AimUpFrame,
                AimPose.Down => HazardSprites.AimDownFrame,
                _ => HazardSprites.StandFrame,
            };
        }
        else if (enemy.Kind == EnemyKind.Flyer)
            frame = HazardSprites.JumpFrame;
        else if (enemy.IsRunning)
            frame = (int)(enemy.Age * 12) % HazardSprites.RunFrames;
        else
            frame = HazardSprites.StandFrame;

        var feet = new Vector2(MathF.Round(x), MathF.Round(enemy.Y));
        SpriteBatch.Draw(texture, feet, HazardSprites.SoldierFrame(frame), Color.White, 0, HazardSprites.SoldierFeet,
            HazardSprites.SoldierScale, SpriteEffects.None, 0);
    }

    /// <summary>
    /// Glowy bits that go over the sprites (additive): jetpack flames, a red visor glow on the
    /// shooters (so you can tell them apart before they aim), aim lasers, bomb fuses, homing essence
    /// </summary>
    private void DrawHazardGlows(Camera2D camera, HazardField hazards, RooftopTrack track, float left, float right)
    {
        if (_essenceArt == null)
            return;

        Rebegin(camera, BlendState.Additive, SamplerState.LinearClamp);
        var glow = _essenceArt.Glow;
        var glowOrigin = new Vector2(glow.Width / 2f, glow.Height / 2f);

        void Glow(Vector2 at, float radius, Color color)
            => SpriteBatch.Draw(glow, at, null, color, 0, glowOrigin, radius / (glow.Width / 2f), SpriteEffects.None, 0);

        foreach (var enemy in hazards.Enemies)
        {
            if (enemy.IsKnocked)
                continue;
            float x = (float)(enemy.X - track.ScrollX);
            if (x < left - 60 || x > right + 60)
                continue;

            if (enemy.Kind == EnemyKind.Flyer)
            {
                // jetpack: two flickering flames under his feet
                float flicker = 0.75f + 0.25f * MathF.Sin(_clock * 45 + enemy.Id);
                Glow(new Vector2(x + 5, enemy.Y + 4), 13 * flicker, JetColor * 0.9f);
                Glow(new Vector2(x + 5, enemy.Y + 12), 7 * flicker, Color.White * 0.6f);
            }

            if (enemy.IsShooter)
            {
                // red visor glow: this one shoots
                var head = new Vector2(x + 2, enemy.Y - 58);
                Glow(head, 9 + 2 * MathF.Sin(_clock * 6 + enemy.Id), AimColor * 0.55f);
            }

            if (enemy.IsAiming)
            {
                // laser sight from the gun toward him. fades in during the aim; once it locks it
                // flashes, which is the last chance to get out of the way
                var muzzle = enemy.Muzzle;
                var from = new Vector2((float)(muzzle.X - track.ScrollX), muzzle.Y);
                var to = from + new Vector2(enemy.AimX, enemy.AimY) * 900;
                float progress = MathHelper.Clamp(enemy.AimSeconds / enemy.AimDuration, 0, 1);
                float alpha = enemy.AimLocked ? ((int)(_clock * 30) % 2 == 0 ? 0.95f : 0.35f) : 0.15f + 0.45f * progress;
                DrawLine(from, to, AimColor * alpha, enemy.AimLocked ? 2.5f : 1.5f);
                Glow(from, 6 + 6 * progress, AimColor * alpha);
            }
        }

        foreach (var bomb in hazards.Pumpkins)
        {
            // lit fuse
            float x = (float)(bomb.X - track.ScrollX);
            float rotation = -bomb.Age * 9;
            var fuse = new Vector2(x, bomb.Y) + Vector2.Transform(HazardSprites.PumpkinFuse * HazardSprites.PumpkinScale,
                Matrix.CreateRotationZ(rotation));
            float flicker = 0.6f + 0.4f * MathF.Sin(_clock * 50 + (float)bomb.X);
            Glow(fuse, 7 * flicker, new Color(255, 230, 120));
        }

        DrawHomingEssence(hazards, track, glowOrigin);

        Rebegin(camera, BlendState.AlphaBlend, SamplerState.PointClamp);
    }

    /// <summary>Essence flying to him: a glowing streak per spark, then the red/blue cores</summary>
    private void DrawHomingEssence(HazardField hazards, RooftopTrack track, Vector2 glowOrigin)
    {
        var homing = _session.World.Essence.Homing;
        if (homing.Count == 0)
            return;

        var glow = _essenceArt.Glow;
        int count = 0;
        foreach (var spark in homing)
        {
            var at = new Vector2((float)(spark.X - track.ScrollX), spark.Y);
            var color = count++ % 2 == 0 ? EssenceArt.Red : EssenceArt.Blue;
            var velocity = new Vector2(spark.VelocityX, spark.VelocityY);
            for (int i = 0; i < 3; i++)
            {
                var trail = at - velocity * (0.012f * i);
                float radius = HomingRadius * (2.6f - 0.5f * i);
                SpriteBatch.Draw(glow, trail, null, color * (0.8f - 0.22f * i), 0, glowOrigin,
                    radius / (glow.Width / 2f), SpriteEffects.None, 0);
            }
        }
    }

    /// <summary>
    /// Bullets: a yellow glowing pixel with a red outline that pulses. The glow goes on with an
    /// additive batch, then the hard pixels on top
    /// </summary>
    private void DrawBullets(Camera2D camera, HazardField hazards, RooftopTrack track)
    {
        if (hazards.Bullets.Count == 0 || _essenceArt == null)
            return;

        var glow = _essenceArt.Glow;
        var glowOrigin = new Vector2(glow.Width / 2f, glow.Height / 2f);

        Rebegin(camera, BlendState.Additive, SamplerState.LinearClamp);
        foreach (var bullet in hazards.Bullets)
        {
            var at = new Vector2((float)(bullet.X - track.ScrollX), bullet.Y);
            float pulse = 0.5f + 0.5f * MathF.Sin(_clock * 28 + bullet.Age * 10);
            SpriteBatch.Draw(glow, at, null, BulletOutline * (0.35f + 0.35f * pulse), 0, glowOrigin,
                (12 + 4 * pulse) / (glow.Width / 2f), SpriteEffects.None, 0);
            SpriteBatch.Draw(glow, at, null, BulletCore * 0.8f, 0, glowOrigin, 7 / (glow.Width / 2f), SpriteEffects.None, 0);
        }

        Rebegin(camera, BlendState.AlphaBlend, SamplerState.PointClamp);
        foreach (var bullet in hazards.Bullets)
        {
            var at = new Vector2(MathF.Round((float)(bullet.X - track.ScrollX)), MathF.Round(bullet.Y));
            float pulse = 0.5f + 0.5f * MathF.Sin(_clock * 28 + bullet.Age * 10);

            // red outline (1 art pixel = 2 units thick, growing a little with the pulse), then the yellow pixel
            float outline = pulse > 0.5f ? 10 : 8;
            Fill(new RectangleF(at.X - outline / 2, at.Y - outline / 2, outline, outline), BulletOutline * (0.6f + 0.4f * pulse));
            Fill(new RectangleF(at.X - 2, at.Y - 2, 4, 4), BulletCore);
        }
    }

    /// <summary>Hit flashes, essence pops, explosions and dust (additive, over everything in the world)</summary>
    private void DrawEffects(Camera2D camera, RooftopTrack track)
    {
        if (_effects.Count == 0 || _essenceArt == null)
            return;

        var glow = _essenceArt.Glow;
        var disc = _essenceArt.Disc;
        var glowOrigin = new Vector2(glow.Width / 2f, glow.Height / 2f);
        var discOrigin = new Vector2(disc.Width / 2f, disc.Height / 2f);

        // dust first, with normal blending (it's darker than what's behind it)
        foreach (var effect in _effects)
        {
            if (effect.Kind != EffectKind.PylonDust && effect.Kind != EffectKind.PumpkinBlast)
                continue;
            float t = (_clock - effect.Start) / effect.Seconds;
            var at = new Vector2((float)(effect.X - track.ScrollX), effect.Y);
            var smoke = effect.Kind == EffectKind.PylonDust ? new Color(170, 165, 160) : new Color(60, 55, 55);
            for (int i = -2; i <= 2; i++)
            {
                var puff = at + new Vector2(i * 14 * (0.4f + t), -6 - 10 * t - (effect.Kind == EffectKind.PumpkinBlast ? 12 * t : 0));
                float radius = (effect.Kind == EffectKind.PylonDust ? 9 : 13) * (0.5f + t);
                SpriteBatch.Draw(disc, puff, null, smoke * (0.55f * (1 - t)), 0, discOrigin, radius / (disc.Width / 2f),
                    SpriteEffects.None, 0);
            }
        }

        Rebegin(camera, BlendState.Additive, SamplerState.LinearClamp);
        foreach (var effect in _effects)
        {
            float t = MathHelper.Clamp((_clock - effect.Start) / effect.Seconds, 0, 1);
            var at = new Vector2((float)(effect.X - track.ScrollX), effect.Y);
            switch (effect.Kind)
            {
                case EffectKind.HitFlash:
                    SpriteBatch.Draw(glow, at, null, Color.White * (1 - t), 0, glowOrigin, (20 + 30 * t) / (glow.Width / 2f),
                        SpriteEffects.None, 0);
                    break;
                case EffectKind.MuzzleFlash:
                    SpriteBatch.Draw(glow, at, null, BulletCore * (1 - t), 0, glowOrigin, 12 / (glow.Width / 2f),
                        SpriteEffects.None, 0);
                    break;
                case EffectKind.EssencePop:
                    _essenceArt.DrawGlow(SpriteBatch, at, 6 * (1 + 3 * t), _clock * 10, alpha: 1 - t);
                    break;
                case EffectKind.PumpkinBlast:
                    SpriteBatch.Draw(glow, at, null, BlastColor * (1 - t), 0, glowOrigin,
                        (PumpkinBomb.BlastRadius * (0.6f + 0.9f * t)) / (glow.Width / 2f), SpriteEffects.None, 0);
                    SpriteBatch.Draw(glow, at, null, new Color(255, 245, 200) * (1 - t * 2f), 0, glowOrigin,
                        (PumpkinBomb.BlastRadius * 0.6f) / (glow.Width / 2f), SpriteEffects.None, 0);
                    break;
            }
        }
        Rebegin(camera, BlendState.AlphaBlend, SamplerState.PointClamp);
    }

    /// <summary>
    /// The spider sense: the flashing warning over his head, plus a little arrow pointing at
    /// whatever's coming. Flashes faster the closer it is to hitting him
    /// </summary>
    private void DrawSpiderSense(HazardField hazards, Runner runner)
    {
        if (!hazards.SpiderSenseActive || _hazardSprites == null || runner.State == RunnerState.Dead)
            return;

        // his head: above his hands when swinging (he hangs from them), above his feet otherwise
        float headY = runner.State == RunnerState.Swinging ? runner.HandY - 12 : runner.FeetY - 72;
        float urgency = hazards.SpiderSenseUrgency;
        float seconds = hazards.SpiderSenseSeconds;

        // pops in, then pulses
        float popIn = MathF.Min(1, seconds / 0.08f);
        float pulse = 1 + 0.12f * MathF.Sin(seconds * (14 + 16 * urgency));
        float scale = 1.25f * popIn * pulse;
        int frame = (int)(seconds * (8 + 14 * urgency)) % HazardSprites.SenseFrames;
        var center = new Vector2(RooftopTrack.RunnerX, headY - 16);

        SpriteBatch.Draw(_hazardSprites.SpiderSense, center, HazardSprites.SenseFrame(frame), Color.White, 0,
            HazardSprites.SenseCenter, scale, SpriteEffects.None, 0);

        // arrow toward the threat
        var direction = new Vector2(hazards.SpiderSenseDirX, hazards.SpiderSenseDirY);
        if (direction.LengthSquared() < 0.5f)
            return;
        direction.Normalize();
        var tip = center + direction * (30 + 3 * pulse);
        var side = new Vector2(-direction.Y, direction.X);
        var back = tip - direction * 10;
        var arrowColor = urgency > 0.7f && (int)(seconds * 16) % 2 == 0 ? Color.White : AimColor;
        DrawLine(back + side * 7, tip, Color.Black * 0.6f, 4f);
        DrawLine(back - side * 7, tip, Color.Black * 0.6f, 4f);
        DrawLine(back + side * 7, tip, arrowColor, 2f);
        DrawLine(back - side * 7, tip, arrowColor, 2f);
    }

    /// <summary>
    /// The combo counter under the essence count, with a bar showing how long is left to keep it
    /// going. Drawn in the text batch (linear sampling)
    /// </summary>
    private void DrawCombo(HazardField hazards)
    {
        if (hazards.ComboMultiplier < 2 || hazards.ComboSecondsLeft <= 0)
            return;

        float pop = 1 + 0.15f * MathF.Max(0, 1 - (HazardField.ComboWindowSeconds - hazards.ComboSecondsLeft) / 0.2f);
        var position = new Vector2(16, 88);
        DrawOutlined($"x{hazards.ComboMultiplier} COMBO", position, Vector2.Zero, ComboColor, 0.5f * pop);

        float left = hazards.ComboSecondsLeft / HazardField.ComboWindowSeconds;
        var bar = new RectangleF(16, position.Y + _font.LineSpacing * 0.5f + 2, 110, 5);
        Fill(bar, Color.Black * 0.6f);
        Fill(new RectangleF(bar.X + 1, bar.Y + 1, (bar.Width - 2) * left, bar.Height - 2), ComboColor);
    }

    /// <summary>Floating text popups (text batch)</summary>
    private void DrawPopups(RooftopTrack track)
    {
        foreach (var popup in _popups)
        {
            float t = (_clock - popup.Start) / PopupSeconds;
            var at = new Vector2((float)(popup.X - track.ScrollX), popup.Y - 30 * t);
            float alpha = t < 0.7f ? 1 : 1 - (t - 0.7f) / 0.3f;
            float scale = PopupScale * (t < 0.1f ? 0.7f + 3 * t : 1);
            DrawCentered(popup.Text, at, popup.Color * alpha, scale);
        }
    }

    /// <summary>
    /// Light blue while he's invulnerable, flickering back to normal in the last moment so you can
    /// see it running out
    /// </summary>
    private Color SpideyTint(HazardField hazards)
    {
        if (!hazards.IsInvulnerable)
            return Color.White;
        bool endingSoon = hazards.InvulnerableSeconds < 0.2f;
        if (endingSoon && (int)(_clock * 20) % 2 == 0)
            return Color.White;
        return InvulnerableTint;
    }

    /// <summary>The frames Spider-Man was drawn with this frame (for tracing the invulnerable glow)</summary>
    private readonly List<(SpiderFrames.Frame Frame, Vector2 Position, float Rotation)> _drawnSpidey = new();

    /// <summary>
    /// While he's invulnerable he glows light blue so it really stands out: a big soft halo, a
    /// bright outline traced around his sprite, and his sprite itself lit up. It pulses, and
    /// flickers as it runs out
    /// </summary>
    private void DrawInvulnerableGlow(Camera2D camera, HazardField hazards, Runner runner)
    {
        if (!hazards.IsInvulnerable || _essenceArt == null || runner.State == RunnerState.Dead)
            return;

        float fade = MathF.Min(1f, hazards.InvulnerableSeconds / 0.2f);
        if (hazards.InvulnerableSeconds < 0.2f && (int)(_clock * 20) % 2 == 0)
            fade *= 0.3f;
        float pulse = 0.75f + 0.25f * MathF.Sin(_clock * 18);

        Rebegin(camera, BlendState.Additive, SamplerState.LinearClamp);

        // the halo
        var glow = _essenceArt.Glow;
        var origin = new Vector2(glow.Width / 2f, glow.Height / 2f);
        var middle = new Vector2(RooftopTrack.RunnerX, runner.FeetY - HazardField.RunnerHeight / 2);
        SpriteBatch.Draw(glow, middle, null, InvulnerableGlow * (0.75f * fade * pulse), 0, origin,
            (64 + 10 * pulse) / (glow.Width / 2f), SpriteEffects.None, 0);
        SpriteBatch.Draw(glow, middle, null, Color.White * (0.25f * fade), 0, origin, 30 / (glow.Width / 2f),
            SpriteEffects.None, 0);

        // an outline: his sprite added a few pixels off in each direction, then over himself
        Rebegin(camera, BlendState.Additive, SamplerState.PointClamp);
        var outline = InvulnerableGlow * (0.9f * fade * pulse);
        foreach (var (frame, position, rotation) in _drawnSpidey)
        {
            foreach (var offset in OutlineOffsets)
            {
                SpriteBatch.Draw(_spidey, position + offset, frame.Source, outline, rotation, frame.Origin,
                    PixelScale, SpriteEffects.None, 0);
            }
            SpriteBatch.Draw(_spidey, position, frame.Source, InvulnerableGlow * (0.45f * fade), rotation, frame.Origin,
                PixelScale, SpriteEffects.None, 0);
        }

        Rebegin(camera, BlendState.AlphaBlend, SamplerState.PointClamp);
    }

    private static readonly Vector2[] OutlineOffsets =
    {
        new(-3, 0), new(3, 0), new(0, -3), new(0, 3),
    };

    // ---- death lines ----

    /// <summary>
    /// What the end screen says. Picked at random each death (every line comes up before any
    /// repeats, never the same one twice in a row), whatever got him: snarky Spider-Man one-liners,
    /// with a hint of the Peter Parker who's behind it all
    /// </summary>
    private static readonly string[] DeathLines =
    {
        "You got squashed!",
        "That was great power?",
        "Now your costume's ruined",
        "Tell MJ you'll be late",
        "Tell Uncle Ben I said hi",
        "Not so amazing now, huh?",
        "Your spider sense tried to warn you",
        "With great power... comes a great fall",
    };

    private readonly Random _deathLineRandom = new();
    private readonly List<string> _deathLineBag = new();
    private string _deathLine;

    /// <summary>Picks the next death line from a shuffled bag of all of them</summary>
    private void PickDeathLine()
    {
        if (_deathLineBag.Count == 0)
        {
            _deathLineBag.AddRange(DeathLines);
            for (int i = _deathLineBag.Count - 1; i > 0; i--)
            {
                int j = _deathLineRandom.Next(i + 1);
                (_deathLineBag[i], _deathLineBag[j]) = (_deathLineBag[j], _deathLineBag[i]);
            }
            if (_deathLineBag.Count > 1 && _deathLineBag[0] == _deathLine)
                (_deathLineBag[0], _deathLineBag[^1]) = (_deathLineBag[^1], _deathLineBag[0]);
        }

        _deathLine = _deathLineBag[0];
        _deathLineBag.RemoveAt(0);
    }
}
