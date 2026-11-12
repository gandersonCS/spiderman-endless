# Assets & Credits

Where every asset in this project came from and what license it's under, including the ones I made
myself. Content files live in `SpiderManEndless.Content/Assets/`.

This is a non-commercial fan project made for a university class (CIS 580, Kansas State University).
Spider-Man and all related characters belong to Marvel. Some assets below (the Spider-Man sprites, the
music and the app icon) are owned by Marvel, Activision or Gameloft and are used without permission,
only for this class project. It isn't for sale and isn't meant to be published or distributed.

## Starter code and tools

- **CIS 580 Game Architecture starter project** - the base this game is built on (the `CIS580/`
  framework, app flow, menus, input, audio, settings, tests, and the docs in `Docs/`), by Professor
  Nathan Bean and the K-State CIS 580 course staff. <https://github.com/ksu-cis-580/game-architecture>
- **MonoGame 3.8** (DesktopGL) - game framework. <https://monogame.net>, Microsoft Public License (Ms-PL).
- **.NET 9** - Microsoft. <https://dotnet.microsoft.com>
- **AI assistance** - Claude (Anthropic) was used to help write and edit parts of the code and
  comments, and wrote the sound-synthesis scripts listed under Sound effects. <https://claude.ai>

## Art

### Made by me

- `Textures/task_force_pylon.png` - the task force pylons.
- `Textures/spider_sense.png` - the spider sense warning.
- `Textures/daily_bugle_board.png` - the Daily Bugle news board (3 frames, the lamp glinting).

### Drawn in code (no image files)

- Spider-Essence orbs (`Views/EssenceArt.cs`).
- Bullets, drop shadows, jetpack flames, aim lasers, explosions and dust (`Views/WorldView.Hazards.cs`).

### From other people

- `Textures/dawn1-7.png`, `Textures/night1-5.png` - parallax city layers (sky first, then buildings
  far to near), used by `Views/CityBackdrop.cs` for the dusk and night skylines. `dawn7.png` is a
  flattened preview of the whole dusk scene and isn't loaded.
  From **Free City Backgrounds Pixel Art** by Free Game Assets (CraftPix):
  <https://free-game-assets.itch.io/free-city-backgrounds-pixel-art>. Free download; the store page
  doesn't list license terms, so use is per the license file included with the pack.
- `Textures/day1-6.png` - the daytime skyline. Recolored copies of `dawn1-6.png` (each layer's
  brightness remapped onto a blue day palette), so same source and license as the dawn layers.

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
  Marvel/Activision property, used without permission for this class project only.
- `Textures/Game Boy Advance - Spider-Man_ The Movie - Playable Characters - Spider-Man.png` -
  the original unedited sheet from the same link. Not loaded by the game.
- `Textures/spiderman_symbiote.png` - Symbiote Suit costume. A recolor of `spiderman.png`
  (generated with a script: red/blue suit colors remapped to black with silver highlights, silver
  lenses, motion trails turned silver, and a small white spider logo hand-placed on the chest of every
  frame the game draws). Same source as `spiderman.png`.
- `Textures/spiderman_ironspider.png` - Iron Spider Suit costume. A recolor of `spiderman.png` built
  from my red-and-gold palette and hand-painted example frame by
  `Textures/Originals/recolor_ironspider.py`: one red suit, gold eyes, a gold spider emblem whose top
  legs form the shoulder pads (placed per frame along his torso, on his chest or back depending on
  which side faces us; see `ironspider_emblems.json`), and gold gauntlets and boot tops. Same source
  as `spiderman.png`.
- `Textures/spiderman_ironspider_arms.png` - Iron Spider's gold spider arms, drawn behind him on the
  attack frames (one padded cell per frame, see `Views/SpiderArms.cs`). Made by
  `Textures/Originals/ironspider_arms.py`.

- `Textures/suit_classic.png`, `Textures/suit_symbiote.png` and `Textures/suit_ironspider.png` - costume card artwork shown in the
  Closet's Outfits screen, cropped and scaled down from fan art:
  - `suit_classic.png` by Giuliano224, based on the *Amazing Spider-Man* (Vol. 6) #42 Marvel
    Masterpieces III variant cover by Greg Hildebrandt and Tim Hildebrandt:
    <https://www.reddit.com/r/Spiderman/comments/1mzz90i/ive_turned_this_cover_art_into_raimis_spiderman/>
  - `suit_symbiote.png` by justartsketches: <https://www.instagram.com/p/CtCMsJsSWEL/>
  - `suit_ironspider.png` - Iron Spider fan art (artist's signature in the bottom-left corner of the
    original). Source: TODO.
  Used without permission for this class project only.

- `Textures/task_force_soldier.png` - the task force soldiers. Cut out of **Open Gunner Hero
  Version 2** by Master484 (credit and license are printed on the sheet), OpenGameArt:
  <https://opengameart.org/content/open-gunner-starter-kit>. License: **CC-BY 3.0**,
  so the credit has to stay. I removed the purple background and packed just the left-facing
  frames I use (run cycle, stand, duck, jump, hurt, aim up/down) into a 64x64 grid,
  then darkened them (`Textures/Originals/recolor_soldier.py`) so they still read red and blue like
  Spider-Man, but murkier and more military.
  The original sheet is kept unedited in `Textures/Originals/open_gunner_hero_v2.png` (not loaded).

- `Textures/pumpkin_bomb.png` - **Mini Pumpkin 16x (Minecraft style)** by JoeEnderman, OpenGameArt:
  <https://opengameart.org/content/mini-pumpkin-16x-minecraft-style>. License: **CC0** (public domain).

- `Textures/background.png` (submenu background) and `Textures/gradient.png` (message box fill) -
  procedurally generated placeholders that came with the starter project, public domain.

## Music

Main menu and in-run music is from **Spider-Man Unlimited** (Gameloft, 2014, Android/iOS/Windows
Phone), composed by Pascal Dion. The tracks are from the game rip on KHInsider:
<https://downloads.khinsider.com/game-soundtracks/album/spider-man-unlimited-gamerip-2014-android-ios-windows>
Gameloft/Marvel own the music. The game was shut down in 2019 and can't be downloaded from the offical source or played anymore;
it's used here without permission for this class project only.

| File | Track | Used for |
|---|---|---|
| `Audio/Music/Title Loading.mp3` | "Title Loading" | Title screen ("press any button") |
| `Audio/Music/Title Menu.mp3` | "Title Menu" | Main menu |
| `Audio/Music/Title Submenu.mp3` | "Title Submenu" | Options and pause menus |
| `Audio/Music/Levelup.mp3` | "Levelup" | Jingle for a new best distance |
| `Audio/Loops/Action A.wav` | "Action A" | Run music (random pick) |
| `Audio/Loops/Action B.wav` | "Action B" | Run music (random pick) |
| `Audio/Loops/Tutorial.wav` | "Tutorial" | Run music (random pick) |

The `Loops/` files are the same songs converted to 16-bit .wav so the game can loop them past the
intro (loop points are in `Audio/GameAudio.cs`).

The other .mp3 files in `Audio/Music/` (Boss A, Mechtopus, Subway, Mission Complete, etc.) are from
the same rip. They aren't built into the game (`Builder.cs` only builds the tracks above); they're
saved for boss fights and other screens later.

`Audio/Music/menu_loop.wav` and `Audio/Music/game_loop.wav` are the starter project's placeholder
loops (procedurally generated, public domain). They still get built but the game doesn't play them.

## Sound effects

### Made by me

- `Audio/Sfx/WebThwip1-4.wav` - web shooting. Designed by me in Thwip Lab, a synthesizer page Claude
  built for this project (all synthesized, no samples). The settings are saved in
  `Audio/Sfx_Originals/thwip-settings.json`.
- `Audio/Sfx/AirDash.wav` (close-call whoosh), `LaserBurst.wav` (task force gunshots) and
  `Pickup4.wav` (a soldier bursting into essence) - made by me in **bfxr**
  (<https://www.bfxr.net>). `Hit.wav` and `Jump.wav` are older bfxr sounds that aren't played anymore.
  Originals are in `Audio/Sfx_Originals/`.

### Synthesized by scripts Claude wrote (no samples)

- `Audio/Sfx/WebWhoosh1-4.wav` (swing swoosh), `NimbleJump1-5.wav` (jump) and `WebTension.wav` /
  `WebTension1-3.wav` (web pulled taut) - `Audio/Sfx_Originals/build_sfx.py` (filtered noise and sine
  sweeps).
- `Audio/Sfx/SpiderSense.wav`, `PumpkinBlast.wav` (pumpkin bomb explosion) and `PylonSlam.wav` (pylon
  landing) - `Audio/Sfx_Originals/synth_hazard_sfx.py` (sine partials and filtered noise).

### From other people, processed by `build_sfx.py`

- `Audio/Sfx/PunchLight1-4.wav`, `PunchHeavy1-4.wav` - trimmed and normalized from the
  **37 hits/punches** pack by Independent.nu (Ljudbank), OpenGameArt:
  <https://opengameart.org/content/37-hitspunches>. License: **CC0**.
  Light = hits 10, 11, 14, 17; heavy = hits 23, 28, 31, 22.
- `Audio/Sfx/EssencePickup.wav` - the main pop of `bubbles-single1.wav`, pitched up a fifth, layered
  with a high-passed `gem-gather-orig.wav` (the layering was done by `build_sfx.py`).
  - `bubbles-single1.wav` from **Bubble Sound Effects** by BMacZero (Brian MacIntosh), OpenGameArt:
    <https://opengameart.org/node/118339>. License: **CC0**.
  - `gem-gather-orig.wav` from **gem collect sfx** by Bobjt, OpenGameArt:
    <https://opengameart.org/content/gem-collect-sfx>. License: **CC0**.
- `Audio/Sfx/ui_blip.wav` - trimmed from `beep.wav`. Original download: SOURCE NEEDED.
- `Audio/Sfx/spiderman-screaming-no.mp3` - Spider-Man's "no!" scream, from *Spider-Man: The
  Animated Series* (1994, Marvel Films / Saban). Downloaded from Myinstants:
  <https://www.myinstants.com/en/instant/spiderman-screaming-no-38073/>.
  Marvel property, used without permission for this class project only.
  `Audio/Sfx/ScreamFall.wav` is that clip processed by `build_sfx.py` (pitch glide, muffling, fade and
  a synthetic echo tail).
- The unedited downloads are kept in `Audio/Sfx_Originals/`.

### From the starter project

- `Audio/Sfx/ui_move.wav`, `ui_confirm.wav`, `ui_back.wav`, `player_join.wav`, `player_leave.wav` -
  placeholder sounds, procedurally generated (synthesized, no samples), public domain.

## Fonts

All from DaFont, free for personal use: <https://www.dafont.com/theme.php?cat=102>

- `Fonts/KOMIKAX_.ttf` (Komika Axis) - used by `menufont.spritefont`.
- `Fonts/Super Squad.ttf` - used by `gamefont.spritefont`.
- `Fonts/Action Comics Black.ttf` - used by `buttonfont.spritefont`. (`Action Comics.ttf` is the
  regular weight, not used.)
- `Fonts/The Amazing Spider-Man.ttf` - used by `titlefont.spritefont`.

`Fonts/LiberationSans-Regular.ttf` - Liberation Sans 2.1.5 from the Liberation Fonts project,
<https://github.com/liberationfonts/liberation-fonts>, SIL Open Font License 1.1
(see `Fonts/LiberationSans-LICENSE.txt`). Came with the starter project; not used anymore.

## Application icon

- `SpiderManEndless/spideyicon.ico` - the game's window and .exe icon. Made from the cover of
  *The Amazing Spider-Man* (Vol. 5, 2018 series) #54, "Last Remains" Part Five, Marvel Comics,
  published December 9, 2020. Cover art by Patrick Gleason, colors by Edgar Delgado.
  <https://www.marvel.com/comics/issue/77122/the_amazing_spider-man_2018_54>.
  © Marvel. Used without permission as a fan/educational placeholder; not for distribution.
- `SpiderManEndless/Icon.ico` and `SpiderManEndless/Icon.bmp` - the default icons from the MonoGame
  project templates (<https://monogame.net>), Microsoft Public License (Ms-PL).
