# Audio

Sources and licenses for everything in here are in the top-level `ASSETS.md`.

| File | Use |
|---|---|
| `Music/Title Loading.mp3` | Title screen ("press any button") music (Spider-Man Unlimited) |
| `Music/Title Menu.mp3` | Main menu music (Spider-Man Unlimited) |
| `Music/Title Submenu.mp3` | Options menu and pause menu music; the run's music is held and resumes where it left off (Spider-Man Unlimited) |
| `Music/Levelup.mp3` | Plays once over the music (which ducks under it) when a run beats your best distance (Spider-Man Unlimited) |
| `Loops/Action A.wav` | Run music, intro + loop (Spider-Man Unlimited) |
| `Loops/Action B.wav` | Run music, intro + loop (Spider-Man Unlimited) |
| `Loops/Tutorial.wav` | Run music, intro + loop (Spider-Man Unlimited) |
| `Sfx/ui_blip.wav` | Menu selection moved |
| `Sfx/ui_move.wav` | Old menu-move placeholder from the starter, not played anymore |
| `Sfx/EssencePickup.wav` | Spider-Essence pickup (bubble bloop + gem sparkle), pitch climbs per streak |
| `Sfx/WebThwip1-4.wav` | Web shot, four variations designed in Thwip Lab (click, spray, zip, splat); settings in `Sfx_Originals/thwip-settings.json` |
| `Sfx/WebTension1-3.wav` | Seamless loops of the web pulled taut (silky fiber shimmer + soft strain + rare tiny creak), one picked per swing, plays only while the web is attached. `WebTension.wav` is the older rope version, no longer played |
| `Sfx/ScreamFall.wav` | The "no!" scream processed to fall away (pitch drops, muffles, echoes out); the original is `spiderman-screaming-no.mp3` |
| `Sfx/NimbleJump1-5.wav` | Jump: low push-off thump + shoe scuff + cloth flick (synthesized; replaced `Jump.wav`) |
| `Sfx/WebWhoosh1-4.wav` | Soft swoosh of the swing, starts as the web connects, louder the longer and faster he swings (synthesized; replaced the louder `AirDash.wav`) |
| `Sfx/PunchLight1-4.wav` | Combat: an attack connecting |
| `Sfx/PunchHeavy1-4.wav` | Combat: getting hit / big hits; also the death hit |
| `Sfx_Originals/` | Unprocessed versions of the sounds above, plus `build_sfx.py`, the script that trimmed, layered and normalized them and synthesized the thwips. Not built into the game |
| `Sfx/ui_confirm.wav` | Menu item confirmed (starter placeholder) |
| `Sfx/ui_back.wav` | Menu back/cancel (starter placeholder) |
| `Sfx/player_join.wav` | Player joined (starter placeholder) |
| `Sfx/player_leave.wav` | Player left (starter placeholder) |
| `Music/menu_loop.wav`, `Music/game_loop.wav` | Old starter placeholder loops, not played anymore |

The other mp3s in `Music/` are from the same Spider-Man Unlimited rip and are saved for later
(boss fights etc.). Their file names now match the track titles in their tags (several were shifted
by one: e.g. the old `Action B.mp3` was really "Action A", which is where `Loops/Action A.wav` came
from). There's no real "Intro" track in the rip.

How it gets built (see `Builder.cs`):
- `Sfx/*.wav` and `Music/*.wav` build as `SoundEffect`.
- mp3s only get built if they're listed in `Builder.cs`, and they're built as `SoundEffect` instead
  of `Song` so the music player can cross-fade them.
- `Loops/*.wav` are copied as-is and read directly by `GameAudio` so it can loop past the intro.

Load content by its path relative to `Assets/` with no extension, e.g.
`Content.Load<SoundEffect>("Audio/Sfx/ui_move")`.
