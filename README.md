# Endless Web

A 2D Spider-Man endless runner made for CIS 580 (Game Programming) at Kansas State University.

Spider-Man is on the run across the rooftops of New York: a task force is hunting him as a suspect in
crimes he didn't commit, and he doesn't know why. Run, jump and web-swing as far as you can, take
down The Vigilante Task Force, dodge bullets, pylons and pumpkin bombs, and collect Spider-Essence to spend on
continues, upgrades and new suits in The Closet.

Built with MonoGame 3.8.6 (DesktopGL) and .NET 9, on top of the CIS 580
[game architecture starter project](https://github.com/ksu-cis-580/game-architecture).

## How to play

| | Keyboard | Gamepad |
|---|---|---|
| Jump (hold for a higher jump) | Space or A | A or right trigger |
| Web swing: press again in the air and hold, let go to release | Space or A | A or right trigger |
| Pause | Esc | Start |

- Running, jumping or swinging into a soldier takes him out. Shielded soldiers can't be beaten, so
  go over them.
- Your spider sense flashes before a threat hits. Shots are dodged by changing height, pylons are
  jumped over (or onto), and pumpkin bombs fly along the line you're on, so get off it.
- Falling into a gap or running into a wall ends the run. You can spend Spider-Essence to continue
  (up to 4 times a run).
- Spend Spider-Essence in **The Closet** on new outfits.

## Building and running

```sh
dotnet build SpiderManEndless.slnx                   # build everything, including content
dotnet run --project SpiderManEndless                # run the game
dotnet run --project SpiderManEndless -- --play      # skip the menus and start a run
dotnet test SpiderManEndless.slnx                    # run the tests
```

## Project layout

| Folder | What's in it |
|---|---|
| `SpiderManEndless.Simulation/` | The game rules: the runner and web-swing physics (`Runner.cs`), the rooftop generator (`RooftopTrack.cs`), enemies, hazards and the spawn director (`HazardField.cs`), essence (`EssenceField.cs`) |
| `SpiderManEndless/` | The MonoGame side: drawing (`Views/`), menus and The Closet (`UI/`), audio, input and save data |
| `SpiderManEndless.Content/` | Art, sound, music and fonts, plus the content builder |
| `CIS580/` | The starter project's framework (app flow, input, audio, UI, settings) |
| `Tests/` | Unit tests for the simulation and the framework |
| `Docs/` | The starter project's documentation for the framework |

## Credits

Every asset's source and license is listed in [ASSETS.md](ASSETS.md). Spider-Man and all related
characters belong to Marvel. This is a non-commercial fan project made for a class.
