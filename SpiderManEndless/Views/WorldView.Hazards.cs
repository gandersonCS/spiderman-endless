using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using CIS580.UI;
using CIS580.Views;
using SpiderManEndless.Simulation;
using SpiderManEndless.Feedback;

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

    /// <summary>The outline around pylons and pumpkin bombs: red means don't touch, you can't punch it</summary>
    private static readonly Color DangerOutline = new(255, 45, 45);

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

    // the "task force adapting" callout
    private string _adaptText;
    private float _adaptStart = -10;
    private const float AdaptSeconds = 2.2f;
    private static readonly Color AdaptColor = new(255, 90, 70);

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
            case TaskForceAdapted adapted:
                string more = adapted.Level > 1 ? "MORE " : "";
                _adaptText = adapted.Focus == TaskForceFocus.Air
                    ? $"TASK FORCE: {more}AIR UNITS INBOUND"
                    : $"TASK FORCE: {more}GROUND UNITS INBOUND";
                _adaptStart = _clock;
                break;
            case NewsBoardRaised raised:
                PostBoard(raised.Board);
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
            if (!pylon.IsFalling)
                continue;

            // a red warning column from the top of the screen down to where it'll land, so it's
            // obvious where it's coming down from the moment it starts
            float x = (float)(pylon.X - track.ScrollX);
            float pulse = 0.5f + 0.5f * MathF.Sin(_clock * 14);
            float alpha = (0.12f + 0.1f * pulse) * (1 - 0.5f * pylon.FallProgress);
            Fill(new RectangleF(x - Pylon.HalfWidth, 0, Pylon.HalfWidth * 2, pylon.RoofY), AimColor * alpha);
            Fill(new RectangleF(x - Pylon.HalfWidth, 0, 2, pylon.RoofY), AimColor * (alpha * 2));
            Fill(new RectangleF(x + Pylon.HalfWidth - 2, 0, 2, pylon.RoofY), AimColor * (alpha * 2));

            DrawShadow(pylon.X - track.ScrollX, pylon.RoofY, pylon.FallProgress, 30, 60);
        }
    }

    private void DrawShadow(double screenX, float roofY, float progress, float smallWidth, float bigWidth)
    {
        float width = MathHelper.Lerp(smallWidth, bigWidth, progress);
        float height = width * 0.3f;
        float alpha = MathHelper.Lerp(0.4f, 0.75f, progress);
        var texture = _essenceArt.Disc;
        var origin = new Vector2(texture.Width / 2f, texture.Height / 2f);
        var scale = new Vector2(width / texture.Width, height / texture.Height);
        SpriteBatch.Draw(texture, new Vector2((float)screenX, roofY + 1), null, Color.Black * alpha, 0, origin, scale,
            SpriteEffects.None, 0);
    }

    /// <summary>Pylons, pumpkin bombs and soldiers (pixel art batch)</summary>
    private void DrawHazards(Camera2D camera, HazardField hazards, RooftopTrack track, float left, float right)
    {
        if (_hazardSprites == null)
            return;

        DrawDangerOutlines(camera, hazards, track, left, right);

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

    /// <summary>
    /// A soft red glow traced around pylons and pumpkin bombs, so you can tell at a glance they
    /// can't be punched (soldiers can). Their white silhouettes are added a couple of pixels off
    /// in each direction, under the sprites, which then cover the middle
    /// </summary>
    private void DrawDangerOutlines(Camera2D camera, HazardField hazards, RooftopTrack track, float left, float right)
    {
        if (hazards.Pylons.Count == 0 && hazards.Pumpkins.Count == 0)
            return;

        Rebegin(camera, BlendState.Additive, SamplerState.PointClamp);
        float pulse = 0.85f + 0.15f * MathF.Sin(_clock * 5);
        var inner = DangerOutline * (0.55f * pulse);
        var outer = DangerOutline * (0.18f * pulse);

        void Outline(Texture2D texture, Vector2 at, Rectangle? source, float rotation, Vector2 origin, float scale)
        {
            for (int i = 0; i < 8; i++)
            {
                float angle = i * MathF.PI / 4;
                var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                SpriteBatch.Draw(texture, at + direction * 4, source, outer, rotation, origin, scale, SpriteEffects.None, 0);
                if (i % 2 == 0)
                    SpriteBatch.Draw(texture, at + direction * 2, source, inner, rotation, origin, scale, SpriteEffects.None, 0);
            }
        }

        foreach (var pylon in hazards.Pylons)
        {
            float x = (float)(pylon.X - track.ScrollX);
            if (x < left - 40 || x > right + 40)
                continue;
            float rate = pylon.IsFalling ? 20 : 7;
            int frame = (int)(pylon.Age * rate + pylon.X * 0.01) % HazardSprites.PylonFrames;
            Outline(_hazardSprites.PylonSilhouette, new Vector2(MathF.Round(x), MathF.Round(pylon.Y)),
                HazardSprites.PylonFrame(frame), 0, HazardSprites.PylonBottom, HazardSprites.PylonScale);
        }

        foreach (var bomb in hazards.Pumpkins)
        {
            float x = (float)(bomb.X - track.ScrollX);
            if (x < left - 30 || x > right + 30)
                continue;
            Outline(_hazardSprites.PumpkinSilhouette, new Vector2(x, bomb.Y), null, -bomb.Age * 9,
                HazardSprites.PumpkinCenter, HazardSprites.PumpkinScale);
        }

        Rebegin(camera, BlendState.AlphaBlend, SamplerState.PointClamp);
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

        if (enemy.IsShielded)
            DrawShield(feet);
    }

    private static readonly Color ShieldColor = new(95, 40, 48);
    private static readonly Color ShieldEdge = new(255, 70, 60);

    /// <summary>
    /// A riot shield held out in front of him (drawn in code): a tall glassy panel with a bright
    /// edge and a "can't touch this" stripe, so you can spot shielded soldiers from across the screen
    /// </summary>
    private void DrawShield(Vector2 feet)
    {
        var panel = new RectangleF(feet.X - 30, feet.Y - Enemy.BodyHeight + 2, 14, Enemy.BodyHeight - 4);
        float pulse = 0.5f + 0.5f * MathF.Sin(_clock * 5);
        Fill(new RectangleF(panel.X - 2, panel.Y - 2, panel.Width + 4, panel.Height + 4), Color.Black * 0.6f);
        Fill(panel, ShieldColor * 0.9f);
        Fill(new RectangleF(panel.X, panel.Y, 2, panel.Height), ShieldEdge * (0.6f + 0.4f * pulse));
        Fill(new RectangleF(panel.X + panel.Width - 2, panel.Y, 2, panel.Height), ShieldEdge * 0.5f);
        Fill(new RectangleF(panel.X, panel.Y, panel.Width, 2), ShieldEdge);
        Fill(new RectangleF(panel.X + 3, panel.Y + 8, panel.Width - 6, 4), Color.White * 0.35f);       // glare
        Fill(new RectangleF(panel.X, panel.Y + panel.Height * 0.55f, panel.Width, 5), new Color(255, 200, 40));  // stripe
        Fill(new RectangleF(panel.X, panel.Y + panel.Height * 0.55f + 5, panel.Width, 3), Color.Black * 0.7f);
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

            if (enemy.IsShielded)
            {
                // a pulsing red glow around the shield: don't touch
                float pulse = 0.5f + 0.5f * MathF.Sin(_clock * 6 + enemy.Id);
                var shield = new Vector2(x - 23, enemy.Y - Enemy.BodyHeight / 2);
                Glow(shield, 34 + 6 * pulse, AimColor * (0.45f + 0.3f * pulse));
                Glow(shield + new Vector2(0, -18), 18, AimColor * 0.35f);
                Glow(shield + new Vector2(0, 18), 18, AimColor * 0.35f);
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
            // once launched, its line stays drawn ahead of it until it's gone by, so you never
            // lose track of where it's going
            if (bomb.IsLaunched)
            {
                float lx0 = (float)(bomb.X - track.ScrollX) - 20;
                for (float lx = lx0; lx > -20; lx -= 26)
                    DrawLine(new Vector2(lx, bomb.BaseY), new Vector2(lx - 14, bomb.BaseY), BlastColor * 0.45f, 2.5f);
            }

            // while it hangs at the edge: its flight line across the screen, pulsing faster as
            // the launch gets close
            if (!bomb.IsLaunched)
            {
                float progress = MathHelper.Clamp(bomb.Age / bomb.WarnSeconds, 0, 1);
                float blink = 0.5f + 0.5f * MathF.Sin(_clock * (10 + 30 * progress));
                float bx = (float)(bomb.X - track.ScrollX);
                for (float lx = bx - 20; lx > -20; lx -= 26)
                    DrawLine(new Vector2(lx, bomb.BaseY), new Vector2(lx - 14, bomb.BaseY), BlastColor * (0.25f + 0.45f * blink), 2.5f);
                Glow(new Vector2(bx, bomb.Y), 22 + 10 * progress, BlastColor * (0.35f + 0.4f * blink));
            }

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
                // kept small and faint: a big combo bursts into a lot of these at once, and a
                // bright streak next to him reads like something dangerous
                var trail = at - velocity * (0.008f * i);
                float radius = HomingRadius * (1.8f - 0.4f * i);
                SpriteBatch.Draw(glow, trail, null, color * (0.5f - 0.15f * i), 0, glowOrigin,
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

            // a streak behind it so you can see which way it's going (and spot it among the essence)
            var direction = new Vector2(bullet.VelocityX, bullet.VelocityY);
            if (direction.LengthSquared() > 1)
                direction.Normalize();
            DrawLine(at - direction * 34, at, Color.Black * 0.45f, 6f);
            DrawLine(at - direction * 30, at, BulletCore * 0.85f, 2.5f);

            // dark backing, red outline (pulsing), then the yellow pixel
            Fill(new RectangleF(at.X - 9, at.Y - 9, 18, 18), Color.Black * 0.55f);
            float outline = pulse > 0.5f ? 14 : 12;
            Fill(new RectangleF(at.X - outline / 2, at.Y - outline / 2, outline, outline), BulletOutline * (0.7f + 0.3f * pulse));
            Fill(new RectangleF(at.X - 3, at.Y - 3, 6, 6), BulletCore);
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

        float pop = 1 + 0.15f * MathF.Max(0, 1 - (hazards.CurrentComboWindow - hazards.ComboSecondsLeft) / 0.2f);
        var position = new Vector2(16, 88);
        DrawOutlined($"x{hazards.ComboMultiplier} COMBO", position, Vector2.Zero, ComboColor, 0.5f * pop);

        float left = hazards.ComboSecondsLeft / hazards.CurrentComboWindow;
        var bar = new RectangleF(16, position.Y + _font.LineSpacing * 0.5f + 2, 110, 5);
        Fill(bar, Color.Black * 0.6f);
        Fill(new RectangleF(bar.X + 1, bar.Y + 1, (bar.Width - 2) * left, bar.Height - 2), ComboColor);
    }

    /// <summary>
    /// "TASK FORCE: AIR UNITS INBOUND" across the top when the task force adapts to how he plays
    /// (text batch)
    /// </summary>
    private void DrawAdaptCallout()
    {
        float t = _clock - _adaptStart;
        if (_adaptText == null || t > AdaptSeconds)
            return;

        float alpha = t < 0.15f ? t / 0.15f : t > AdaptSeconds - 0.4f ? (AdaptSeconds - t) / 0.4f : 1f;
        bool flash = t < 0.6f && (int)(t * 12) % 2 == 0;
        // under the Daily Bugle board while one is up, so it doesn't cover it
        var at = new Vector2(World.Width / 2, BoardShowing(_session.World.Track) ? BelowBoards : 112);
        Fill(new RectangleF(0, at.Y - 16, World.Width, 32), Color.Black * (0.45f * alpha));
        DrawCentered(_adaptText, at, (flash ? Color.White : AdaptColor) * alpha, 0.5f);
    }

    /// <summary>
    /// When he's above the top of the screen, an arrow at the top edge shows where he is (red
    /// while his spider sense is going)
    /// </summary>
    private void DrawOffscreenMarker(HazardField hazards, Runner runner)
    {
        if (runner.State == RunnerState.Dead || runner.FeetY - 60 > 0)
            return;

        var color = hazards.SpiderSenseActive ? AimColor : Color.White;
        float x = RooftopTrack.RunnerX;
        var tip = new Vector2(x, 4);
        DrawLine(new Vector2(x - 10, 20), tip, Color.Black * 0.6f, 5f);
        DrawLine(new Vector2(x + 10, 20), tip, Color.Black * 0.6f, 5f);
        DrawLine(new Vector2(x - 10, 20), tip, color, 3f);
        DrawLine(new Vector2(x + 10, 20), tip, color, 3f);
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

    // ---- what got him ----

    /// <summary>
    /// After he dies, a pulsing red ring around whatever did it, so you can see why before the
    /// end screen comes up (and it stays, faintly, behind the end screen)
    /// </summary>
    private void DrawWhatGotHim(Camera2D camera, HazardField hazards, RooftopTrack track, Runner runner)
    {
        if (runner.State != RunnerState.Dead || hazards.KilledBy is not { } killer || _essenceArt == null)
            return;

        float t = runner.DeadSeconds;
        float strength = t < EndScreenDelay ? 1f : 0.45f;
        float pulse = 0.5f + 0.5f * MathF.Sin(t * 16);
        var at = new Vector2((float)(killer.X - track.ScrollX), killer.Y);

        Rebegin(camera, BlendState.Additive, SamplerState.LinearClamp);
        var glow = _essenceArt.Glow;
        SpriteBatch.Draw(glow, at, null, AimColor * (0.6f * strength), 0, new Vector2(glow.Width / 2f, glow.Height / 2f),
            (34 + 8 * pulse) / (glow.Width / 2f), SpriteEffects.None, 0);
        Rebegin(camera, BlendState.AlphaBlend, SamplerState.PointClamp);

        // the ring shrinks in onto it, then pulses
        float radius = MathHelper.Lerp(80, 30, MathF.Min(1, t / 0.25f)) + 4 * pulse;
        const int segments = 20;
        for (int i = 0; i < segments; i++)
        {
            float a0 = i * MathHelper.TwoPi / segments, a1 = (i + 1) * MathHelper.TwoPi / segments;
            var p0 = at + new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * radius;
            var p1 = at + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * radius;
            DrawLine(p0, p1, Color.Black * (0.5f * strength), 5f);
            DrawLine(p0, p1, AimColor * strength, 3f);
        }
    }

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
        "Not so amazing now...",
        "Your spider sense tried to warn you",
        "With great power... comes a great fall",
        "Does anything a spider can't"
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
