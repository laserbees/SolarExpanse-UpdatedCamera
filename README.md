# Updated Camera (Solar Expanse mod)

A [BepInEx 5](https://github.com/BepInEx/BepInEx) mod for **Solar Expanse** that keeps the camera
where you put it. The game recentres the camera on almost every click; with this mod it only moves
when you ask it to.

## Features

**Single click selects, double click centres**
- Clicking a planet, moon, the Sun, a spacecraft or a facility (in the 3D view, on its label, or
  on the planet icons at the bottom of the screen) opens its info window as usual, but no longer
  moves the camera. Double-click it to centre the camera on it.
- The same applies to rows and links in the game's windows: find-object results, facility,
  spacecraft and mission rows, the production queue, and an object's name in its info window.
- The trip planner no longer moves the camera: not when it opens, when you pick a destination or
  a point on the porkchop plot, or when you schedule the mission.
- Action buttons (trash, examine/discover, the space elevator view, destroying an asteroid) never
  move the camera.

**Consistent zoom when centring**
- Planets, moons, and asteroids and comets are each zoomed so they fill a set share of the screen
  height, so Jupiter and Earth look the same size when you centre on them. Spacecraft and stations
  use a distance multiplier.

**View offset beside side panels**
- When info windows cover the right side of the screen, the view shifts so the centred object sits
  in the middle of the uncovered area. It follows the panels smoothly as they slide in and out, and
  treats windows stacked side by side as one panel.

**HUD**
- Hides the company logo in the top-left corner.
- Moves and resizes the contracts box.

**Unchanged:** number-key camera shortcuts, clicking notifications, and the camera following a
ship or trajectory you're already following when it lands or ends.

## Installing

1. Install **BepInEx 5** (64-bit Windows, e.g. `BepInEx_win_x64_5.4.23.x.zip` from the
   [BepInEx releases](https://github.com/BepInEx/BepInEx/releases)): extract it into the Solar
   Expanse game folder, so `BepInEx` sits next to `Solar Expanse.exe`, then start the game once
   and close it.
2. Download `UpdatedCamera-<version>.zip` from this repository's
   [Releases](../../releases) and extract it into the same game folder. The DLL ends up at
   `BepInEx\plugins\UpdatedCamera\UpdatedCamera.dll`.
3. Start the game.

To uninstall, delete the `BepInEx\plugins\UpdatedCamera` folder.

## Settings

Press **F9** in game to open the settings panel. Changes apply immediately and are saved to
`BepInEx\config\laserbees.solarexpanse.updatedcamera.cfg`, which you can also edit with the game
closed.

| Setting | Default | What it does |
|---|---|---|
| Planets & Sun | 10% | Share of the screen height a planet fills when centred (0 = game default) |
| Moons | 20% | Same, for moons |
| Asteroids & comets | 20% | Same, for asteroids and comets |
| Spacecraft & stations | 1× | Distance multiplier when centring on spacecraft (1× = game default) |
| Shift the view beside right-side panels | on | The view offset described above |
| Also when the camera isn't following anything | on | Apply the offset even when nothing is centred |
| Strength | 83% | How far to shift, as a share of the covered width (100% = exact centre of the free area) |
| Stacked window gap | 20 px | How far apart two windows can be and still count as one panel |
| Hide company logo | on | Hides the logo in the top-left corner |
| Contracts box X / Y / size | 0 / 232 / 0.85 | Position and size of the contracts box |

The config file also has `DoubleClickSeconds` (default 0.4) and the panel key (default F9; not
F8, which is the game's bug-report key).

## Building from source

Requires the .NET SDK (any recent version, e.g. 8 or 9) and the game with BepInEx installed, since
the project references the game's own DLLs.

```
dotnet build -c Release
```

The build copies `UpdatedCamera.dll` into `<game>\BepInEx\plugins\UpdatedCamera\`. If the game
isn't at `F:\SteamLibrary\steamapps\common\Solar Expanse`, edit `GamePath` in
`UpdatedCamera.csproj` or pass it in:

```
dotnet build -c Release -p:GamePath="D:\Path\To\Solar Expanse"
```

Close the game before building; Windows locks the DLL while the game is running.

### Project layout

- `Plugin.cs`: mod entry point, settings, Harmony setup
- `CameraPatches.cs`: blocks click- and planner-triggered camera moves; double click to centre
- `UiClickPatches.cs`: the same for buttons and list rows in the game's windows
- `ZoomPatches.cs`: sets the zoom distance when centring, per kind of object
- `ViewOffset.cs`: shifts the view beside panels on the right
- `PostProcessingPatches.cs`: keeps that shift when the post-processing stack resets the camera
- `HudTweaks.cs`: hides the company logo, moves and resizes the contracts box
- `TuningPanel.cs`: the F9 settings panel

## License

[MIT](LICENSE)
