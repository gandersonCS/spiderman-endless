using System;
using System.Collections.Generic;
using System.Linq;
using CIS580.Simulation;
using SpiderManEndless.Simulation;

namespace SpiderManEndless.Tests;

public class HazardFieldTests
{
    private static readonly PlayerId One = new(1);

    private static (GameSession session, SessionRunner runner) Start(Func<long, ActionSet> actionsOnTick, ulong seed = 1)
    {
        var session = new GameSession(seed);
        var runner = new SessionRunner(session);
        runner.AddPlayer(One, new DelegateCommandSource(tick => new PlayerCommand { Actions = actionsOnTick(tick) }));
        return (session, runner);
    }

    private static ActionSet Holding(params GameAction[] actions)
    {
        var set = default(ActionSet);
        foreach (var action in actions)
            set = set.With(action);
        return set;
    }

    private static void Steps(SessionRunner runner, int count)
    {
        for (int i = 0; i < count; i++)
            runner.Step();
    }

    /// <summary>A soldier standing on the roof this far ahead of him</summary>
    private static Enemy SoldierAhead(GameSession session, float ahead, bool shooter = false)
    {
        var world = session.World;
        double x = world.Track.RunnerTrackX + ahead;
        var enemy = new Enemy(world.Hazards.NextId(), EnemyKind.Rooftop, shooter, x, world.Track.RoofAt(x).Value, speed: 0);
        world.Hazards.Add(enemy);
        return enemy;
    }

    [Fact]
    public void Difficulty_StartsAtZero_AndReachesOne()
    {
        Assert.Equal(0f, HazardField.DifficultyAt(0));
        Assert.Equal(0f, HazardField.DifficultyAt(HazardField.GraceMeters));
        Assert.Equal(1f, HazardField.DifficultyAt(HazardField.GraceMeters + HazardField.RampMeters));
        Assert.Equal(1f, HazardField.DifficultyAt(1_000_000));
        Assert.True(HazardField.DifficultyAt(500) < HazardField.DifficultyAt(800));
    }

    [Fact]
    public void NothingSpawns_DuringTheGraceDistance()
    {
        var (session, runner) = Start(_ => default);
        var world = session.World;
        for (int i = 0; i < 60 * 20 && world.Runner.State != RunnerState.Dead; i++)
        {
            runner.Step();
            if (world.Track.DistanceMeters < HazardField.GraceMeters)
            {
                Assert.Empty(world.Hazards.Enemies);
                Assert.Empty(world.Hazards.Pylons);
                Assert.Empty(world.Hazards.Pumpkins);
                Assert.Empty(world.Hazards.Bullets);
            }
        }
    }

    [Fact]
    public void AttackingASoldier_KnocksHimAway_AndHisEssenceFliesToYou()
    {
        var (session, runner) = Start(tick => tick >= 5 ? Holding(GameAction.Attack) : default);
        Steps(runner, 3);
        var enemy = SoldierAhead(session, 45);

        Steps(runner, 4);
        Assert.True(enemy.IsKnocked);
        Assert.NotEqual(RunnerState.Dead, session.World.Runner.State);
        Assert.Contains(session.Events.Pending, e => e is EnemyDefeated { Combo: 1 });

        Steps(runner, 150);
        Assert.Contains(session.Events.Pending, e => e is EnemyExploded);
        Assert.DoesNotContain(session.World.Hazards.Enemies, e => e == enemy);

        // all of it reached him (burst essence has a negative pack number)
        int fromSoldier = session.Events.Pending.OfType<EssenceCollected>().Count(e => e.Pack < 0);
        Assert.Equal(HazardField.SoldierEssence, fromSoldier);
        Assert.Empty(session.World.Essence.Homing);
    }

    [Fact]
    public void RunningIntoASoldier_PunchesHimAutomatically()
    {
        var (session, runner) = Start(_ => default);
        Steps(runner, 3);
        var enemy = SoldierAhead(session, 60);

        Steps(runner, 30);
        Assert.True(enemy.IsKnocked);
        Assert.NotEqual(RunnerState.Dead, session.World.Runner.State);
        Assert.Contains(session.Events.Pending, e => e is RunnerAttacked { Kind: AttackKind.Jab });
    }

    [Fact]
    public void RunningIntoAShieldedSoldier_EndsTheRun()
    {
        var (session, runner) = Start(_ => default);
        Steps(runner, 3);
        var world = session.World;
        double x = world.Track.RunnerTrackX + 60;
        var enemy = new Enemy(world.Hazards.NextId(), EnemyKind.Rooftop, false, x, world.Track.RoofAt(x).Value, 0)
        {
            IsShielded = true,
        };
        world.Hazards.Add(enemy);

        Steps(runner, 30);
        Assert.Equal(RunEndReason.Tackled, world.Runner.EndReason);
        Assert.False(enemy.IsKnocked);
    }

    [Fact]
    public void JumpingOverAShieldedSoldier_IsSafe()
    {
        var (session, runner) = Start(tick => tick >= 3 ? Holding(GameAction.Jump) : default);
        Steps(runner, 3);
        var world = session.World;
        double x = world.Track.RunnerTrackX + 150;
        world.Hazards.Add(new Enemy(world.Hazards.NextId(), EnemyKind.Rooftop, false, x, world.Track.RoofAt(x).Value, 0)
        {
            IsShielded = true,
        });

        Steps(runner, 50);
        Assert.NotEqual(RunnerState.Dead, world.Runner.State);
        Assert.True(world.Track.RunnerTrackX > x + 40);
    }

    [Fact]
    public void BeatingSoldiersInARow_BuildsACombo_ThatMultipliesEssence()
    {
        var (session, runner) = Start(tick => tick >= 5 ? Holding(GameAction.Attack) : default);
        Steps(runner, 3);
        SoldierAhead(session, 45);
        SoldierAhead(session, 100);

        Steps(runner, 20);
        var defeats = session.Events.Pending.OfType<EnemyDefeated>().ToList();
        Assert.Equal(2, defeats.Count);
        Assert.Equal(2, session.World.Hazards.Combo);
        Assert.Equal(2, defeats[1].Multiplier);

        Steps(runner, 150);
        int fromSoldiers = session.Events.Pending.OfType<EssenceCollected>().Count(e => e.Pack < 0);
        Assert.Equal(HazardField.SoldierEssence * 1 + HazardField.SoldierEssence * 2, fromSoldiers);
    }

    [Fact]
    public void ABullet_EndsTheRun()
    {
        var (session, runner) = Start(_ => default);
        Steps(runner, 3);
        var world = session.World;
        world.Hazards.Add(new Bullet(world.Track.RunnerTrackX + 120, world.Runner.FeetY - 30, -400, 0));

        Steps(runner, 30);
        Assert.Equal(RunEndReason.Shot, world.Runner.EndReason);
    }

    [Fact]
    public void ABulletThatJustMisses_IsACloseCall_WorthEssence()
    {
        var (session, runner) = Start(_ => default);
        Steps(runner, 3);
        var world = session.World;
        float overHisHead = world.Runner.FeetY - HazardField.RunnerHeight - 30;
        world.Hazards.Add(new Bullet(world.Track.RunnerTrackX + 120, overHisHead, -400, 0));

        for (int i = 0; i < 120 && !session.Events.Pending.Any(e => e is NearMiss); i++)
            runner.Step();
        Assert.Contains(session.Events.Pending, e => e is NearMiss);
        Assert.True(world.Hazards.IsInvulnerable, "A close call should make him invulnerable for a moment");

        Steps(runner, 100);
        Assert.Equal(1, world.Hazards.NearMisses);
        int fromCloseCall = session.Events.Pending.OfType<EssenceCollected>().Count(e => e.Pack < 0);
        Assert.Equal(HazardField.NearMissEssence, fromCloseCall);
    }

    [Fact]
    public void ACloseCall_AddsToTheCombo()
    {
        var (session, runner) = Start(_ => default);
        Steps(runner, 3);
        var world = session.World;
        float overHisHead = world.Runner.FeetY - HazardField.RunnerHeight - 30;
        world.Hazards.Add(new Bullet(world.Track.RunnerTrackX + 120, overHisHead, -400, 0));

        for (int i = 0; i < 120 && !session.Events.Pending.Any(e => e is NearMiss); i++)
            runner.Step();
        Assert.Equal(1, world.Hazards.Combo);
        Assert.True(world.Hazards.ComboSecondsLeft > 0);
    }

    [Fact]
    public void TheComboTimer_GrowsExponentiallyWithEachStage_UpToACap()
    {
        Assert.Equal(HazardField.ComboWindowSeconds, HazardField.ComboWindowFor(1), 3);
        float previous = HazardField.ComboWindowFor(1);
        for (int stage = 2; stage <= 4; stage++)
        {
            float window = HazardField.ComboWindowFor(stage);
            Assert.Equal(previous * HazardField.ComboWindowGrowth, window, 3);
            previous = window;
        }
        Assert.Equal(HazardField.MaxComboWindowSeconds, HazardField.ComboWindowFor(20), 3);
    }

    [Fact]
    public void ClearingAShieldedSoldierCloseUp_IsACloseCall()
    {
        // jump at him from different distances: a late jump that only just clears him should count
        bool found = false;
        for (int lead = 30; lead <= 200 && !found; lead += 10)
        {
            var (session, runner) = Start(tick => tick >= 3 ? Holding(GameAction.Jump) : default);
            Steps(runner, 3);
            var world = session.World;
            double x = world.Track.RunnerTrackX + lead;
            float? roof = world.Track.RoofAt(x);
            if (roof == null)
                continue;
            world.Hazards.Add(new Enemy(world.Hazards.NextId(), EnemyKind.Rooftop, false, x, roof.Value, 0)
            {
                IsShielded = true,
            });

            Steps(runner, 60);
            found = world.Runner.State != RunnerState.Dead && session.Events.Pending.Any(e => e is NearMiss);
        }
        Assert.True(found, "Only just clearing a shielded soldier should be a close call");
    }

    [Fact]
    public void RunningIntoAPylon_EndsTheRun()
    {
        var (session, runner) = Start(_ => default);
        Steps(runner, 3);
        var world = session.World;
        double x = world.Track.RunnerTrackX + 150;
        float roof = world.Track.RoofAt(x).Value;
        world.Hazards.Add(new Pylon(x, roof, roof, fallSeconds: 0));

        Steps(runner, 40);
        Assert.Equal(RunEndReason.HitPylon, world.Runner.EndReason);
    }

    [Fact]
    public void JumpingOverAPylon_IsSafe()
    {
        var (session, runner) = Start(tick => tick >= 3 ? Holding(GameAction.Jump) : default);
        Steps(runner, 3);
        var world = session.World;
        double x = world.Track.RunnerTrackX + 150;
        float roof = world.Track.RoofAt(x).Value;
        var pylon = new Pylon(x, roof, roof, fallSeconds: 0);
        world.Hazards.Add(pylon);

        Steps(runner, 50);
        Assert.NotEqual(RunnerState.Dead, world.Runner.State);
        Assert.True(world.Track.RunnerTrackX > x + 40, "He should be past the pylon");
    }

    [Fact]
    public void AFallingPylon_LandsOnTheRoof_AndStaysThere()
    {
        var (session, runner) = Start(_ => default);
        Steps(runner, 3);
        var world = session.World;
        double x = world.Track.RunnerTrackX + 400;
        float roof = world.Track.RoofAt(x).Value;
        var pylon = new Pylon(x, HazardField.DropStartY, roof, fallSeconds: 0.5f);
        world.Hazards.Add(pylon);

        Steps(runner, 10);
        Assert.True(pylon.IsFalling);
        Assert.True(world.Hazards.SpiderSenseActive);

        Steps(runner, 30);
        Assert.False(pylon.IsFalling);
        Assert.Equal(roof, pylon.Y);
        Assert.Contains(session.Events.Pending, e => e is PylonLanded);
    }

    [Fact]
    public void AShooterTakingAim_SetsOffTheSpiderSense_ThenFires()
    {
        var (session, runner) = Start(_ => default);
        Steps(runner, 3);
        var world = session.World;

        // a flying shooter, so he stays on screen long enough to shoot
        double x = world.Track.ScrollX + 700;
        float y = world.Track.RoofAt(x).Value - 130;
        var flyer = new Enemy(world.Hazards.NextId(), EnemyKind.Flyer, isShooter: true, x, y, speed: 60);
        world.Hazards.Add(flyer);
        world.Hazards.HoldFireBeforeEdges = false;     // he's not jumping, so there's always an edge coming

        for (int i = 0; i < 60 && !flyer.IsAiming; i++)
            runner.Step();
        Assert.True(flyer.IsAiming);
        Assert.True(world.Hazards.SpiderSenseActive);
        Assert.Contains(session.Events.Pending, e => e is SpiderSenseTriggered { Kind: ThreatKind.Shooter });

        for (int i = 0; i < 90 && !session.Events.Pending.Any(e => e is BulletFired); i++)
            runner.Step();
        Assert.Contains(session.Events.Pending, e => e is BulletFired);
    }

    [Fact]
    public void ASoldierThatDoesntShoot_DoesntSetOffTheSpiderSense()
    {
        var (session, runner) = Start(_ => default);
        Steps(runner, 3);
        SoldierAhead(session, 400);

        Steps(runner, 20);
        Assert.False(session.World.Hazards.SpiderSenseActive);
    }

    [Fact]
    public void APumpkinBombFromTheRight_BlowsUpOnYou()
    {
        var (session, runner) = Start(_ => default);
        Steps(runner, 3);
        var world = session.World;
        world.Hazards.Add(new PumpkinBomb(world.Track.RunnerTrackX + 200, world.Runner.FeetY - 25, vx: -400));

        Steps(runner, 40);
        Assert.Equal(RunEndReason.Blasted, world.Runner.EndReason);
        Assert.Contains(session.Events.Pending, e => e is PumpkinExploded);
    }

    [Fact]
    public void GroundSpots_KeepAwayFromRoofEdges()
    {
        const int size = RooftopTrack.TileSize;
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var track = new RooftopTrack(new DeterministicRandom(seed));
            var hazards = new HazardField(new DeterministicRandom(seed));
            for (double from = 300; from < 1300; from += 37)
            {
                if (!hazards.FindGroundSpot(track, from, out double x, out float roof))
                    continue;

                var building = track.Buildings.Single(b => x >= b.StartColumn * size && x < b.EndColumn * size);
                Assert.Equal(building.TopRow * size, roof);
                Assert.True(x - building.StartColumn * size >= HazardField.MinTilesFromRoofStart * size);
                Assert.True(building.EndColumn * size - x >= HazardField.MinTilesFromRoofEnd * size);
            }
        }
    }

    [Fact]
    public void SameSeedAndCommands_SpawnTheSameHazards()
    {
        // jump now and then, attack now and then: enough to live a while and hit things
        static ActionSet Script(long tick)
        {
            var set = default(ActionSet);
            if (tick % 50 < 20)
                set = set.With(GameAction.Jump);
            if (tick % 23 == 0)
                set = set.With(GameAction.Attack);
            return set;
        }

        var (a, runnerA) = Start(Script, seed: 77);
        var (b, runnerB) = Start(Script, seed: 77);
        a.World.Hazards.StartMeters = 0;
        b.World.Hazards.StartMeters = 0;

        for (int i = 0; i < 60 * 15; i++)
        {
            runnerA.Step();
            runnerB.Step();
        }

        var ha = a.World.Hazards;
        var hb = b.World.Hazards;
        Assert.Equal(ha.Enemies.Select(e => (e.X, e.Y, e.IsShooter)), hb.Enemies.Select(e => (e.X, e.Y, e.IsShooter)));
        Assert.Equal(ha.Pylons.Select(p => (p.X, p.Y)), hb.Pylons.Select(p => (p.X, p.Y)));
        Assert.Equal(ha.Pumpkins.Select(p => (p.X, p.Y)), hb.Pumpkins.Select(p => (p.X, p.Y)));
        Assert.Equal(ha.Bullets.Select(p => (p.X, p.Y)), hb.Bullets.Select(p => (p.X, p.Y)));
        Assert.Equal(ha.Defeated, hb.Defeated);
        Assert.Equal(a.World.Essence.CollectedThisRun, b.World.Essence.CollectedThisRun);
        Assert.Equal(a.World.Runner.FeetY, b.World.Runner.FeetY);
        Assert.Equal(a.Events.Pending.Count, b.Events.Pending.Count);
    }

    [Fact]
    public void AfterBeatingASoldier_HazardsCantHurtYouForAMoment()
    {
        var (session, runner) = Start(tick => tick >= 5 ? Holding(GameAction.Attack) : default);
        Steps(runner, 3);
        SoldierAhead(session, 45);
        Steps(runner, 4);
        var world = session.World;
        Assert.True(world.Hazards.IsInvulnerable);

        // a bullet right on him
        world.Hazards.Add(new Bullet(world.Track.RunnerTrackX + 20, world.Runner.FeetY - 30, -400, 0));
        Steps(runner, 10);
        Assert.NotEqual(RunnerState.Dead, world.Runner.State);

        // and it wears off
        Steps(runner, 30);
        Assert.False(world.Hazards.IsInvulnerable);
    }

    [Fact]
    public void YouCanLandAndRunOnTopOfAPylon()
    {
        var (session, runner) = Start(tick => tick >= 3 ? Holding(GameAction.Jump) : default);
        Steps(runner, 3);
        var world = session.World;
        double x = world.Track.RunnerTrackX + 230;
        float roof = world.Track.RoofAt(x).Value;
        var pylon = new Pylon(x, roof, roof, fallSeconds: 0);
        world.Hazards.Add(pylon);
        runner.Step();      // the platform shows up after the hazards update

        bool stoodOnIt = false;
        for (int i = 0; i < 70; i++)
        {
            runner.Step();
            if (world.Runner.State == RunnerState.Running && Math.Abs(world.Runner.FeetY - pylon.Top) < 0.5f)
                stoodOnIt = true;
        }

        Assert.True(stoodOnIt, "He should have landed on top of the pylon");
        Assert.NotEqual(RunnerState.Dead, world.Runner.State);
    }

    [Fact]
    public void PylonSpots_AreNearTheMiddleOfWideRoofs()
    {
        const int size = RooftopTrack.TileSize;
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var track = new RooftopTrack(new DeterministicRandom(seed));
            var hazards = new HazardField(new DeterministicRandom(seed));
            for (double from = 300; from < 1300; from += 53)
            {
                if (!hazards.FindPylonSpot(track, from, double.MaxValue, out double x, out float roof))
                    continue;

                var building = track.Buildings.Single(b => x >= b.StartColumn * size && x < b.EndColumn * size);
                Assert.True(building.WidthTiles >= HazardField.MinPylonRoofTiles);
                Assert.Equal(building.TopRow * size, roof);
                Assert.True(x >= from);
                Assert.True(x - building.StartColumn * size >= HazardField.MinTilesFromRoofStart * size);
                Assert.True(building.EndColumn * size - x >= HazardField.MinTilesFromRoofEnd * size);

                double middle = (building.StartColumn + building.EndColumn) * size / 2.0;
                Assert.True(Math.Abs(x - middle) <= HazardField.PylonCenterJitter * building.WidthTiles * size + 0.01);
            }
        }
    }

    [Theory]
    [InlineData(TaskForceFocus.Mixed, 0.5)]
    [InlineData(TaskForceFocus.Air, 0.7)]
    [InlineData(TaskForceFocus.Ground, 0.3)]
    public void TheFocus_SplitsAirAndGroundUnits_AndEveryKindStillShowsUp(TaskForceFocus focus, double expectedAir)
    {
        var hazards = new HazardField(new DeterministicRandom(5));
        hazards.SetFocus(focus);
        var counts = new Dictionary<HazardField.SpawnKind, int>();
        const int picks = 4000;
        for (int i = 0; i < picks; i++)
        {
            var kind = hazards.PickSpawn(0.5f);
            counts[kind] = counts.GetValueOrDefault(kind) + 1;
        }

        double air = counts.Where(c => HazardField.IsAirUnit(c.Key)).Sum(c => c.Value) / (double)picks;
        Assert.InRange(air, expectedAir - 0.04, expectedAir + 0.04);
        foreach (var kind in Enum.GetValues<HazardField.SpawnKind>())
            Assert.True(counts.GetValueOrDefault(kind) > picks * 0.04, $"{kind}: {counts.GetValueOrDefault(kind)} of {picks}");
    }

    [Fact]
    public void TheTaskForce_StaysMixed_ForTheFirst500Meters()
    {
        var track = new RooftopTrack(new DeterministicRandom(4));
        var hazards = new HazardField(new DeterministicRandom(5)) { StartMeters = 0 };
        var events = new EventQueue<GameEvent>();
        long tick = 0;
        while (track.DistanceMeters < HazardField.MixedUntilMeters - 1)
        {
            track.Advance(1 / 60f);
            hazards.SetAirShare(1f);
            hazards.UpdateFocus(track, events, tick++);
            Assert.Equal(TaskForceFocus.Mixed, hazards.Focus);
        }
        while (track.DistanceMeters < HazardField.MixedUntilMeters + 2)
        {
            track.Advance(1 / 60f);
            hazards.SetAirShare(1f);
            hazards.UpdateFocus(track, events, tick++);
        }
        Assert.Equal(TaskForceFocus.Air, hazards.Focus);
    }

    [Fact]
    public void BigGaps_GetMoreCommonFurtherIntoTheRun()
    {
        // the first 80 m are easy, the end of the ramp is hard
        int BigGaps(int fromColumn, int toColumn)
        {
            int count = 0;
            for (ulong seed = 1; seed <= 40; seed++)
            {
                var track = new RooftopTrack(new DeterministicRandom(seed));
                var previous = track.Buildings[0];
                while (previous.EndColumn < toColumn)
                {
                    track.Advance(0.25f);
                    foreach (var building in track.Buildings)
                    {
                        if (building.StartColumn <= previous.StartColumn)
                            continue;
                        int gap = building.StartColumn - previous.EndColumn;
                        if (building.StartColumn >= fromColumn && gap >= RooftopTrack.BigGapMin)
                            count++;
                        previous = building;
                    }
                }
            }
            return count;
        }

        int early = BigGaps(0, 400);
        int late = BigGaps(1600, 2000);
        Assert.True(late > early, $"{late} big gaps late vs {early} early");
    }

    [Fact]
    public void APylonDroppedRightOnYou_Crushes()
    {
        // the director never drops one right on him (see the next test), but if one does land on
        // him, that's still a crush
        var (session, runner) = Start(_ => default);
        Steps(runner, 3);
        var world = session.World;
        float seconds = HazardField.EasyPylonFallSeconds;
        double x = world.Track.RunnerTrackX + world.Track.Speed * seconds;
        float roof = world.Track.RoofAt(x).Value;
        world.Hazards.Add(new Pylon(x, HazardField.DropStartY, roof, seconds));

        Steps(runner, (int)(seconds * 60) + 10);
        Assert.Equal(RunEndReason.Crushed, world.Runner.EndReason);
    }

    [Fact]
    public void ADroppedPylon_LandsJustInFrontOfYou_NotOnYou()
    {
        // where the director drops one: just ahead of where he'd be when it lands. Keep running
        // without jumping and he runs into its side (not crushed)
        var (session, runner) = Start(_ => default);
        Steps(runner, 3);
        var world = session.World;
        float seconds = 0.8f;     // short enough that it lands on the first roof
        double x = world.Track.RunnerTrackX + world.Track.Speed * (seconds + HazardField.PylonLandsAheadSeconds);
        float roof = world.Track.RoofAt(x).Value;
        var pylon = new Pylon(x, HazardField.DropStartY, roof, seconds);
        world.Hazards.Add(pylon);

        Steps(runner, (int)(seconds * 60) + 2);
        Assert.False(pylon.IsFalling);
        Assert.NotEqual(RunnerState.Dead, world.Runner.State);

        Steps(runner, 40);
        Assert.Equal(RunEndReason.HitPylon, world.Runner.EndReason);
    }

    [Fact]
    public void AFallingPylon_IsOnScreenForTheWholeDrop()
    {
        var pylon = new Pylon(500, HazardField.DropStartY, 288, 1f);
        Assert.True(pylon.Top < 0 && pylon.Y > 0, "Its bottom should show at the top of the screen from the start");
    }

    [Fact]
    public void RunningIntoAPylonNearItsTop_PopsYouOntoIt()
    {
        var (session, runner) = Start(_ => default);
        Steps(runner, 3);
        var world = session.World;
        double x = world.Track.RunnerTrackX + 120;
        float roof = world.Track.RoofAt(x).Value;

        // a pylon sunk into the roof so its top is just above his feet
        float bottom = roof + Pylon.Height - 20;
        var pylon = new Pylon(x, bottom, bottom, fallSeconds: 0);
        world.Hazards.Add(pylon);

        Steps(runner, 40);
        Assert.NotEqual(RunnerState.Dead, world.Runner.State);
    }

    [Fact]
    public void ABulletThatOnlyGrazesYou_IsACloseCallNotADeath()
    {
        var (session, runner) = Start(_ => default);
        Steps(runner, 3);
        var world = session.World;

        // through the top couple of pixels of his full body: inside how he looks, outside the lethal box
        float graze = world.Runner.FeetY - HazardField.RunnerHeight + 2;
        world.Hazards.Add(new Bullet(world.Track.RunnerTrackX + 120, graze, -400, 0));

        Steps(runner, 60);
        Assert.NotEqual(RunnerState.Dead, world.Runner.State);
    }

    [Fact]
    public void WhenAHazardEndsTheRun_ItRemembersWhere()
    {
        var (session, runner) = Start(_ => default);
        Steps(runner, 3);
        var world = session.World;
        double x = world.Track.RunnerTrackX + 120;
        world.Hazards.Add(new Bullet(x, world.Runner.FeetY - 30, -400, 0));

        Steps(runner, 30);
        Assert.Equal(RunEndReason.Shot, world.Runner.EndReason);
        Assert.NotNull(world.Hazards.KilledBy);
    }

    [Fact]
    public void JumpingIntoASoldier_BeatsHim()
    {
        // in the air, touching a soldier counts as a kick
        var (session, runner) = Start(tick => tick >= 3 && tick < 30 ? Holding(GameAction.Jump) : default);
        Steps(runner, 3);
        var enemy = SoldierAhead(session, 45);

        Steps(runner, 12);
        Assert.True(enemy.IsKnocked);
        Assert.NotEqual(RunnerState.Dead, session.World.Runner.State);
        Assert.Contains(session.Events.Pending, e => e is RunnerAttacked { Kind: not AttackKind.Jab });
    }

    [Fact]
    public void ChainedSwings_DontSpeedUpForever()
    {
        // hold jump the whole time (jump, then swing again and again), re-pressing so each swing chains
        var (session, runner) = Start(tick => tick % 40 < 36 ? Holding(GameAction.Jump) : default);
        float fastest = 0;
        for (int i = 0; i < 60 * 8 && session.World.Runner.State != RunnerState.Dead; i++)
        {
            runner.Step();
            fastest = MathF.Max(fastest, session.World.Track.Speed);
        }

        // the bottom of a swing can go a bit over the cap, but not run away
        Assert.True(fastest < session.World.Track.CruiseSpeed * Runner.MaxSwingSpeedRatio * 1.5f,
            $"fastest {fastest}");
    }

    [Fact]
    public void APumpkinBomb_LinesUpWithYourChest_AndOnlyWarnsAtLaunch()
    {
        var (session, runner) = Start(_ => default);
        Steps(runner, 3);
        var world = session.World;
        world.Hazards.StartMeters = double.MaxValue;
        Assert.True(world.Hazards.TrySpawnPumpkin(world.Track, world.Runner, 0.5f, session.Events, 3));

        var bomb = world.Hazards.Pumpkins[^1];
        Assert.Equal(world.Runner.FeetY - HazardField.RunnerHeight / 2, bomb.BaseY, 1);
        Assert.False(bomb.IsLaunched);

        // no warning while it hangs...
        runner.Step();
        Assert.DoesNotContain(session.Events.Pending, e => e is SpiderSenseTriggered { Kind: ThreatKind.PumpkinBomb });

        // ...then it launches, and he's still on its line
        for (int i = 0; i < 90 && !bomb.IsLaunched; i++)
            runner.Step();
        Assert.True(bomb.IsLaunched);
        Assert.Contains(session.Events.Pending, e => e is SpiderSenseTriggered { Kind: ThreatKind.PumpkinBomb });
    }

    [Fact]
    public void ShootersDontFirePointBlank()
    {
        var (session, runner) = Start(_ => default);
        Steps(runner, 3);
        var world = session.World;
        world.Hazards.StartMeters = double.MaxValue;
        world.Hazards.HoldFireBeforeEdges = false;

        // a flying shooter close in front of him
        double x = world.Track.RunnerTrackX + 160;
        float y = world.Runner.FeetY - 60;
        world.Hazards.Add(new Enemy(world.Hazards.NextId(), EnemyKind.Flyer, isShooter: true, x, y, speed: 60));

        Steps(runner, 60);
        Assert.DoesNotContain(session.Events.Pending, e => e is BulletFired);
    }

    [Fact]
    public void OnlyOneShooterAimsAtATime()
    {
        var (session, runner) = Start(_ => default);
        Steps(runner, 3);
        var world = session.World;
        world.Hazards.StartMeters = double.MaxValue;     // just these two

        // two flying shooters on screen together
        for (int i = 0; i < 2; i++)
        {
            double x = world.Track.ScrollX + 640 + i * 60;
            float y = world.Track.RoofAt(world.Track.RunnerTrackX).Value - 140 - i * 40;
            world.Hazards.Add(new Enemy(world.Hazards.NextId(), EnemyKind.Flyer, isShooter: true, x, y, speed: 60));
        }

        for (int i = 0; i < 120; i++)
        {
            runner.Step();
            Assert.True(world.Hazards.Enemies.Count(e => e.IsAiming) <= 1);
        }
    }

    [Fact]
    public void APumpkinBomb_HangsAtTheEdgeFirst_ThenLaunches()
    {
        var (session, runner) = Start(_ => default);
        Steps(runner, 3);
        var world = session.World;
        float y = world.Runner.FeetY - 200;
        var bomb = PumpkinBomb.Hovering(world.Track.ScrollX + World.Width - 30, y, 800, 0.5f);
        world.Hazards.Add(bomb);

        Steps(runner, 20);
        Assert.False(bomb.IsLaunched);
        Assert.Equal(World.Width - 30, (float)(bomb.X - world.Track.ScrollX), 1);     // stays put on screen

        Steps(runner, 15);
        Assert.True(bomb.IsLaunched);
        Assert.Contains(session.Events.Pending, e => e is PumpkinLaunched);
    }

    [Fact]
    public void ABombWhoseLineMissesYou_DoesntSetOffTheSpiderSense()
    {
        var (session, runner) = Start(_ => default);
        Steps(runner, 3);
        var world = session.World;
        world.Hazards.StartMeters = double.MaxValue;
        float wayAbove = world.Runner.FeetY - HazardField.RunnerHeight / 2 - HazardField.PumpkinLaneWarnDistance - 60;
        world.Hazards.Add(new PumpkinBomb(world.Track.RunnerTrackX + 400, wayAbove, vx: -500));

        Steps(runner, 5);
        Assert.False(world.Hazards.SpiderSenseActive);
    }

    [Fact]
    public void SwingingALot_SwitchesTheTaskForceToAirUnits()
    {
        var (session, runner) = Start(_ => default);
        Steps(runner, 3);
        var world = session.World;
        world.Hazards.StartMeters = 0;
        world.Hazards.FocusStartMeters = 0;
        world.Hazards.SetAirShare(1f);

        runner.Step();
        Assert.Equal(TaskForceFocus.Air, world.Hazards.Focus);
        Assert.Contains(session.Events.Pending, e => e is TaskForceAdapted { Focus: TaskForceFocus.Air });
    }

    [Fact]
    public void EvenInTheAir_RooftopSoldiersStillShowUp()
    {
        var hazards = new HazardField(new DeterministicRandom(9));
        hazards.SetFocus(TaskForceFocus.Air);
        int rooftop = 0;
        for (int i = 0; i < 2000; i++)
        {
            if (hazards.PickSpawn(0.5f) == HazardField.SpawnKind.RooftopSoldier)
                rooftop++;
        }
        Assert.True(rooftop > 2000 * 0.15, $"{rooftop} of 2000");
    }

    [Fact]
    public void TheContactAttack_DependsOnWhereTheSoldierIs()
    {
        Assert.Equal(AttackKind.Jab, HazardField.ContactAttack(onGround: true, 200, 150));
        Assert.Equal(AttackKind.AirKick, HazardField.ContactAttack(onGround: false, 200, 260));     // below
        Assert.Equal(AttackKind.UpwardKick, HazardField.ContactAttack(onGround: false, 200, 205));  // level
        Assert.Equal(AttackKind.Uppercut, HazardField.ContactAttack(onGround: false, 200, 140));    // above
    }

    [Fact]
    public void AShieldedSoldierAhead_SetsOffTheSpiderSense()
    {
        var (session, runner) = Start(_ => default);
        Steps(runner, 3);
        var world = session.World;
        world.Hazards.StartMeters = double.MaxValue;
        double x = world.Track.RunnerTrackX + 400;
        world.Hazards.Add(new Enemy(world.Hazards.NextId(), EnemyKind.Rooftop, false, x, world.Track.RoofAt(x).Value, 0)
        {
            IsShielded = true,
        });

        Steps(runner, 2);
        Assert.True(world.Hazards.SpiderSenseActive);
        Assert.Single(session.Events.Pending.OfType<SpiderSenseTriggered>(), e => e.Kind == ThreatKind.ShieldedSoldier);
    }

    // ---- Daily Bugle boards and the task force doubling down ----

    [Fact]
    public void TheControlsBoard_GoesUpRightAway()
    {
        var (session, runner) = Start(_ => default);
        runner.Step();
        Assert.Contains(session.Events.Pending, e => e is NewsBoardRaised { Board: NewsBoard.Controls });
    }

    /// <summary>
    /// Runs the hazards on their own against a scrolling track (he stands still and never falls),
    /// for at most this many ticks or until <paramref name="stop"/> says so
    /// </summary>
    private static (HazardField hazards, EventQueue<GameEvent> events, Runner runner, RooftopTrack track) RunHazards(
        ulong seed, int maxTicks, Func<HazardField, EventQueue<GameEvent>, bool> stop, double startMeters = HazardField.GraceMeters)
    {
        var track = new RooftopTrack(new DeterministicRandom(seed));
        var runner = new Runner(track);
        var essence = new EssenceField(new DeterministicRandom(seed + 100));
        var hazards = new HazardField(new DeterministicRandom(seed + 200)) { StartMeters = startMeters };
        var events = new EventQueue<GameEvent>();
        long tick = 0;
        for (; tick < maxTicks && runner.State != RunnerState.Dead && !stop(hazards, events); tick++)
        {
            track.Advance(1 / 60f);
            hazards.Update(track, runner, essence, events, tick, 1 / 60f);
        }
        return (hazards, events, runner, track);
    }

    [Fact]
    public void TheSpiderSenseBoard_GoesUpBeforeTheGraceDistanceEnds()
    {
        var (_, events, _, _) = RunHazards(3, 60 * 30,
            (_, ev) => ev.Pending.Any(e => e is NewsBoardRaised { Board: NewsBoard.SpiderSense }), startMeters: double.MaxValue);
        Assert.Contains(events.Pending, e => e is NewsBoardRaised { Board: NewsBoard.SpiderSense });
        Assert.True(HazardField.SpiderSenseBoardMeters < HazardField.GraceMeters);
    }

    [Fact]
    public void TheBoards_ComeOneEvery100Meters_ThenTheFirstSpawnIsANormalSoldier()
    {
        for (ulong seed = 1; seed <= 10; seed++)
        {
            var (hazards, events, _, track) = RunHazards(seed, 60 * 120,
                (h, _) => h.Enemies.Count > 0 || h.Pylons.Count > 0 || h.Pumpkins.Count > 0);

            var boards = events.Pending.OfType<NewsBoardRaised>().Select(e => e.Board).ToArray();
            Assert.Equal(new[] { NewsBoard.Controls, NewsBoard.SpiderSense, NewsBoard.Soldiers }, boards);

            // nothing before the grace distance, and the first thing is a normal soldier
            Assert.True(track.DistanceMeters >= HazardField.GraceMeters, $"seed {seed}: spawned at {track.DistanceMeters:0} m");
            Assert.Empty(hazards.Pylons);
            Assert.Empty(hazards.Pumpkins);
            var first = Assert.Single(hazards.Enemies);
            Assert.Equal(EnemyKind.Rooftop, first.Kind);
            Assert.False(first.IsShielded, $"seed {seed}: the first soldier was shielded");
        }
    }

    [Fact]
    public void PlayingTheSameWay_MakesTheTaskForceDoubleDown_AndItSwitchesBack()
    {
        var track = new RooftopTrack(new DeterministicRandom(4));
        var hazards = new HazardField(new DeterministicRandom(5)) { StartMeters = 0, FocusStartMeters = 0 };
        var events = new EventQueue<GameEvent>();
        long tick = 0;
        void RunMeters(double meters, float airShare)
        {
            double end = track.DistanceMeters + meters;
            while (track.DistanceMeters < end)
            {
                track.Advance(1 / 60f);
                hazards.SetAirShare(airShare);
                hazards.UpdateFocus(track, events, tick++);
            }
        }

        // swinging the whole time: announced, then doubled down once he's kept at it long enough
        RunMeters(1, 1f);
        RunMeters(HazardField.FocusRepeatMeters + 5, 1f);
        var air = events.Pending.OfType<TaskForceAdapted>().ToList();
        Assert.Equal(new[] { 1, 2 }, air.Select(a => a.Level));
        Assert.All(air, a => Assert.Equal(TaskForceFocus.Air, a.Focus));

        // back on the roofs: it switches to ground units, starting over at level 1
        RunMeters(1, 0f);
        Assert.Equal(TaskForceFocus.Ground, hazards.Focus);
        var last = events.Pending.OfType<TaskForceAdapted>().Last();
        Assert.Equal((TaskForceFocus.Ground, 1), (last.Focus, last.Level));
    }
}
