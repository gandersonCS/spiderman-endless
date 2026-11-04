# SpiderManEndless

A starter project for MonoGame games, written for CIS580. It provides the parts every game needs:
menus, rebindable input (including gamepads and shared keyboards), a local multiplayer lobby,
pausing, saved settings, music and sound effects, game-feel effects, and a deterministic
fixed-timestep simulation. The gameplay itself is left for you to write.

Built on MonoGame 3.8.6 (DesktopGL) and .NET 9.

## Quick start

```sh
dotnet build SpiderManEndless.slnx                   # build everything, including content
dotnet run --project SpiderManEndless                # run the game
dotnet run --project SpiderManEndless -- --play      # skip the menus and start playing
dotnet test SpiderManEndless.slnx                    # run the tests
```

## Documentation

Start with the [documentation overview](Docs/README.md), then try the
[walkthrough](Docs/walkthrough.md), which adds a small feature end to end.

| | |
|---|---|
| [Getting Started](docs/getting-started.md) | Building, running, default controls, troubleshooting |
| [Simulation](docs/simulation.md) | Ticks, commands, events, determinism |
| [Input](docs/input.md) | Bindings, control schemes, rebinding, local players |
| [App Flow](docs/flow.md) | Main menu, lobby, loading, playing, paused |
| [User Interface](docs/ui.md) | Layers, menus, prompts, transitions, themes |
| [Views & Cameras](docs/views.md) | Drawing the world, cameras, split screen |
| [Audio](docs/audio.md) | Buses, sound cues, music |
| [Game Feel](docs/feedback.md) | Screen shake, rumble, hit-stop |
| [Settings](docs/settings.md) | Saving player settings and controls |
| [Content](docs/content.md) | The content builder and adding assets |
| [Testing](docs/testing.md) | The test projects and how to test your code |

## Assets

Every asset's source and license is listed in [ASSETS.md](ASSETS.md).
