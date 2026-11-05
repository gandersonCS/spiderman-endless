# Assets & Credits

Everything in this project that I didn't make myself, where it came from, and what license it's under.
Content files live in `SpiderManEndless.Content/Assets/`.

This is a fan project made for a university class (CIS 580, Kansas State University). Spider-Man and
all related characters belong to Marvel. It's not for sale and isn't meant to be published.

## Starter code and tools

- **CIS 580 Game Architecture starter project** - the base this game is built on (the `CIS580/`
  framework, app flow, menus, input, audio, settings, tests, and the docs in `Docs/`) By professor Nathan Bean.
  K-State CIS 580 course staff. <https://github.com/ksu-cis-580/game-architecture>
- **MonoGame 3.8** (DesktopGL) - game framework. <https://monogame.net>, Microsoft Public License (Ms-PL).
- **.NET 9** - Microsoft. <https://dotnet.microsoft.com>
- **AI assistance** - Claude (Anthropic) was used to help write and edit parts of the code and
  comments. <https://claude.ai>

## Art

- `Textures/tileset_city_free.png` - building tileset (16x16 tiles), and
  `Textures/background_tileset_city_free.png` - the city skyline used for parallax.
  From the **Free Retro City asset pack** by MariaParraGames ("FREE TILESET by MP" on the image).
  <https://mariaparragames.itch.io/free-retro-city-assetpack>
  License (from the itch.io page): free for personal and commercial use, can be edited,
  can't be redistributed or resold. Credit is optional, but here it is anyway.

- `Textures/spiderman.png` - Spider-Man sprites from the Game Boy Advance game
  *Spider-Man: The Movie* (Activision, 2002). Sheet ripped by ZiggytheNinja (credit is printed on
  the sheet), downloaded from The Spriters Resource:
  <https://www.spriters-resource.com/game_boy_advance/spidermanthemovie/asset/58021/>
  I removed the green background and the web lines that were baked into the sheet.
  The sprites are Marvel/Activision property and are only used here for a class project - ABANDONWARE
- `Textures/Game Boy Advance - Spider-Man_ The Movie - Playable Characters - Spider-Man.png` -
  the original unedited sheet from the same link. Not loaded by the game.

- `Textures/spiderman_symbiote.png` - Symbiote Suit costume. A recolor of `spiderman.png`
  (generated with a script: red/blue suit colors remapped to black with silver highlights, silver
  lenses, motion trails turned silver, and a small white spider logo hand-placed on the chest of every
  frame the game draws). Same source/license as `spiderman.png`.
- `Textures/suit_classic.png`, `Textures/suit_symbiote.png` - costume card artwork shown in the
  Closet's Outfits screen, cropped and scaled down from reference images I supplied. Source: TODO.

- `Textures/background.png` (submenu background) and `Textures/gradient.png` (message box fill) -
  procedurally generated placeholders that came with the starter project, public domain.

- `Textures/dawn1-7.png`, `Textures/night1-5.png` - parallax city layers (sky first, then
  buildings far to near), used by `Views/CityBackdrop.cs` for the dusk and night skylines.
  `dawn7.png` is a flattened preview of the whole dusk scene and isn't loaded.
  Source: TODO.
- `Textures/day1-6.png` - the daytime skyline. Recolored copies of `dawn1-6.png` (each layer's
  brightness remapped onto a blue day palette), so same source/license as the dawn layers.

- Spider-Essence orbs - drawn in code (`Views/EssenceArt.cs`), no image file.

## Music

Main menu and in-run music is from **Spider-Man Unlimited** (Gameloft, 2014, Android/iOS/Windows
Phone), music composed by Pascal Dion. The tracks are from the game rip on KHInsider:
<https://downloads.khinsider.com/game-soundtracks/album/spider-man-unlimited-gamerip-2014-android-ios-windows>
Gameloft/Marvel own the music; it's only used here for a class project. - ABANDONWARE(game was shut down and made innaccesible)

| File | Track | Used for |
|---|---|---|
| `Audio/Music/Title Menu.mp3` | "Title Menu" | Main menu |
| `Audio/Loops/Action A.wav` | "Action A" | Run music (random pick) |
| `Audio/Loops/Action B.wav` | "Action B" | Run music (random pick) |
| `Audio/Loops/Tutorial.wav` | "Tutorial" | Run music (random pick) |

The `Loops/` files are the same songs converted to 16-bit .wav so the game can loop them past the
intro (loop points are in `Audio/GameAudio.cs`).

The other .mp3 files in `Audio/Music/` (Boss A, Mechtopus, Subway, Mission Complete, etc.) are from
the same rip. They aren't in the game yet, they're there for boss fights and other screens later.

`Audio/Music/menu_loop.wav` and `Audio/Music/game_loop.wav` are the starter project's placeholder
loops (procedurally generated, public domain). They still get built but the game doesn't play them anymore.

## Sound effects

- `Audio/Sfx/PunchLight1-4.wav`, `PunchHeavy1-4.wav` - trimmed and normalized from the
  **37 hits/punches** pack by Independent.nu (Ljudbank), OpenGameArt:
  <https://opengameart.org/content/37-hitspunches>. License: TODO, confirm on the page (listed as CC0
  as far as I know). Light = hits 10, 11, 14, 17; heavy = hits 23, 28, 31, 22.
- `Audio/Sfx/EssencePickup.wav` - the main pop of `bubbles-single1.wav`, pitched up a fifth, layered
  with a high-passed `gem-gather-orig.wav`. Sources and licenses: TODO.
- `Audio/Sfx/ui_blip.wav` - trimmed from `beep.wav`. Source and license: TODO.
- `Audio/Sfx/WebWhoosh1-4.wav`, `NimbleJump1-5.wav`, `WebTension.wav` and `WebTension1-3.wav` - synthesized from scratch with
  `Sfx_Originals/build_sfx.py` (filtered noise and sine sweeps), no samples, made for this project.
- `Audio/Sfx/WebThwip1-4.wav` - designed by me in Thwip Lab, a synthesizer page Claude built for this project
  (all synthesized, no samples). The settings are saved in `Audio/Sfx_Originals/thwip-settings.json`.
- `Audio/Sfx/ScreamFall.wav` - `spiderman-screaming-no.mp3` processed with `build_sfx.py` (pitch glide,
  muffling, fade and a synthetic echo tail), so same source as that file.
- The unedited downloads are kept in `Audio/Sfx_Originals/`.

- `Audio/Sfx/ui_move.wav`, `ui_confirm.wav`, `ui_back.wav`, `player_join.wav`, `player_leave.wav` -
  placeholder sounds from the starter project, procedurally generated (synthesized, no samples),
  public domain.

## Fonts

- `Fonts/LiberationSans-Regular.ttf` - Liberation Sans 2.1.5 from the Liberation Fonts project,
  <https://github.com/liberationfonts/liberation-fonts>, SIL Open Font License 1.1
  (see `Fonts/LiberationSans-LICENSE.txt`). Came with the starter project; no longer used by
  the game's fonts.
- `Fonts/Action Comics.ttf`, `Action Comics Black.ttf`, `KOMIKAX_.ttf`, `Super Squad.ttf`,
  `The Amazing Spider-Man.ttf` - comic-style fonts I downloaded for the menus and HUD. Used by
  `menufont.spritefont` (Komika Axis), `gamefont.spritefont` (Super Squad), `buttonfont.spritefont`
  (Action Comics Black) and `titlefont.spritefont` (The Amazing Spider-Man).
  Source and license: https://www.dafont.com/theme.php?cat=102 Free for Personal Use

## Application icon

- `SpiderManEndless/spideyicon.ico` - the game's window and .exe icon. Made from the cover of
  *The Amazing Spider-Man* (Vol. 5, 2018 series) #54, "Last Remains" Part Five, Marvel Comics,
  published December 9, 2020. Cover art by Patrick Gleason, colors by Edgar Delgado.
  <https://www.marvel.com/comics/issue/77122/the_amazing_spider-man_2018_54>.
  © Marvel. Used without permission as a fan/educational placeholder; not for distribution.
- `SpiderManEndless/Icon.ico` and `SpiderManEndless/Icon.bmp` - the default icons from the MonoGame
  project templates (<https://monogame.net>), Microsoft Public License (Ms-PL).
