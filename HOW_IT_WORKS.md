# How it works

Technical notes on what this mod changes in Solar Expanse, which parts of the game it hooks, and
why each change is done the way it is. The [README](README.md) covers what the mod does from a
player's point of view; this file is for anyone reading or changing the code.

All game code is patched with [Harmony](https://harmony.pardeike.net/) (prefixes, finalizers and
field accessors). Nothing in the game's assets or scenes is modified.

- [Click behaviour: select vs. centre](#click-behaviour-select-vs-centre)
- [Buttons and rows in windows](#buttons-and-rows-in-windows)
- [Trip planner](#trip-planner)
- [Consistent zoom when centring](#consistent-zoom-when-centring)
- [View offset beside side panels](#view-offset-beside-side-panels)
- [Post-processing compatibility](#post-processing-compatibility)
- [HUD tweaks](#hud-tweaks)
- [Settings panel](#settings-panel)
- [Robustness](#robustness)
- [Game code touched](#game-code-touched)

---

## Click behaviour: select vs. centre

**File:** `CameraPatches.cs`

### The problem

Almost every click in the game calls `MyCameraController.ChangeTarget`, which recentres and
re-zooms the camera. Clicking a planet just to read its info window throws away whatever view the
player had set up.

### The approach

Instead of rewriting each click handler, the mod lets the handler run normally but **blocks the
camera move** that happens inside it:

1. `CameraGate` holds a counter. While it's above zero, camera moves are "suppressed".
2. The camera-move methods themselves get a prefix that returns `false` (skips the original)
   while suppressed:
   - `MyCameraController.ChangeTarget(Transform, bool)`
   - `MyCameraController.ChangeTarget(ObjectInfo, bool)`
   - `MyCameraController.ChangeTargetForPlanMission`
3. Each click handler gets a prefix that enters the gate and a finalizer that leaves it.
   Everything else the handler does (opening the info window, highlighting, selection) still
   happens.

A finalizer is used rather than a postfix so the gate is always released, even if the game's
handler throws. A counter is used rather than a bool because handlers can call each other.

### Double click to centre

`CameraGate.RegisterClick` remembers the last clicked object and the time. A second click on the
**same object** within `DoubleClickSeconds` (default 0.4 s) counts as a double click. In that case
the finalizer, after the gate is released, calls `ChangeTarget(transform)` itself, which is the
same call the game would have made.

Comparison is by reference (`ReferenceEquals`), so clicking the planet and then its label counts
as a double click only if both resolve to the same object.

### Handlers patched

| Where the player clicks | Game method |
|---|---|
| Planet, moon or Sun in the 3D view | `InfoBase.MyOnMouseUpAsButton` |
| An object's name label in the 3D view | `InfoBase.MyOnMouseUpAsButtonFromObjectLabel` |
| Planet icons at the bottom of the screen, moon labels, favourites | `HighlightHoverObject.ChangeTarget` |
| Older planet buttons some screens may still use | `ChangeTargetButtons.ChangeTarget` |
| Spacecraft and facility markers in the 3D view | `Bilboard.MyOnMouseUpAsButton` |
| Labels next to spacecraft in flight | `MissionsLabelsMainUI.Click` |

Notes on specific handlers:

- **`HighlightHoverObject`**: only counts as a click when the component is enabled, matching the
  game's own check.
- **`Bilboard`**: centres on the marker's private `targetCamera` field when set, otherwise the
  marker itself, which is what the game does.
- **`MissionsLabelsMainUI`**: on double click it also sets `TargetIsSCOnFly = true`, so the camera
  follows the ship in flight as it would have in the unmodified game.

### What's intentionally left alone

Number-key camera shortcuts, clicking notifications, and the camera following a ship or
trajectory when it lands or ends are not wrapped by the gate, so they still move the camera.

---

## Buttons and rows in windows

**File:** `UiClickPatches.cs`

### The problem

Many rows and buttons inside the game's windows also call `ChangeTarget`. Unlike the 3D view,
it's not obvious from the outside what each one would centre on.

### The approach

`UiClickGate` wraps these methods the same way, but instead of working out the target in
advance, it **records** it: the `ChangeTarget` prefixes in `CameraPatches.cs` call
`UiClickGate.Record(newTarget)` when they block a move. When the outermost wrapped call finishes,
`UiClickGate.End` knows what the click would have centred on.

- **Rows and links** (`UiRowClickPatch`, `allowDoubleClick: true`): a single click does
  everything except move the camera; a second click on the same target within the double-click
  time centres it, using the recorded target.
- **Action buttons** (`UiActionClickPatch`, `allowDoubleClick: false`): never move the camera.
  Double-click is off because a second click would run the action again.

Some of these clicks also set `TargetIsSCOnFly` (camera follows a ship in flight). The gate saves
that flag before the click and restores it afterwards when the move was blocked, and only applies
the new value if the move actually goes ahead on a double click.

Methods are listed by name as strings (`"Namespace.Type:Method"`) and resolved with
`AccessTools.Method`. A name that no longer exists, or is ambiguous, is logged and skipped instead
of failing the whole patch. `"(object)"` at the end of a name selects the overload taking a single
`object` parameter.

### Methods patched

Rows and links (single click selects, double click centres):

| Element | Game method |
|---|---|
| Find-object results | `SearchObjectElements.SearchRow.OnClickSelectButton` |
| Facility in an object's facility list | `UIFacilityList.ShowFacilityOnOrbitAndOpenFacilityInfoWindow`, `UIFacilityList.OpenFacilityInfoWindow` |
| Spacecraft row in the rockets list | `UIRowRocket.ShowOnOrbitAndOpenFacilityInfoWindow` |
| Mission row in an info window | `UIRowMission.RowClick` |
| Production queue item | `ProductionItem.OnClick` |
| Object name at the top of its info window | `UIObjectName.ClickName` |

Action buttons (never move the camera):

| Action | Game method |
|---|---|
| Trash button in the info window | `UIObjectName.OnClickTrashObjectInfo` |
| Accept Examine / Discover | `ExplorationWindow.OnAcceptExamineButtonClick`, `ExplorationWindow.OnAcceptDiscoverButtonClick` |
| Opening a space elevator's facility info | `FacilityInfoWindow.SetData(object)` |
| Destroy asteroid button, and the resulting explosion | `FacilityInfoWindow.OnClickDestroyAsteroid`, `ObjectInfo.VirtualDestroyFromNuclearBomb` |

---

## Trip planner

**File:** `CameraPatches.cs`

The trip planner moved the camera at several points, which made it hard to look at the solar
system while planning. These are all blocked:

| When | Game method | How |
|---|---|---|
| Opening the planner (centred on the origin) | `PMTabDestination.ActiveTab` | Wrapped in the gate |
| Picking a destination (centred on it) | `PMTabDestination.DestinationInputOnObjectSelect` | Wrapped in the gate |
| Picking a point on the porkchop plot (zooms out to show the trajectory) | `PlanMissionWindow.ChangeTargetForCamera` | Skipped entirely: the method only moves the camera |
| Scheduling a mission (jumps to the origin as the planner closes) | `PMTabSchedule.CreateFly` | Wrapped in the gate |

---

## Consistent zoom when centring

**File:** `ZoomPatches.cs`

### The problem

When centring, the game uses a hand-picked distance per object (`ObjectInfo.ZoomToSet`). Bodies
of different sizes end up framed very differently: Jupiter and Earth look nothing alike in size
when you centre on them.

### The approach

After the game sets its distance, the mod replaces it with **the distance at which the body fills
a set share of the screen height**:

```
halfFov   = fieldOfView / 2
halfAngle = atan(share * tan(halfFov))
distance  = radius / sin(halfAngle)
```

A sphere of radius `r` at distance `d` spans an angle of `2·asin(r/d)`; its on-screen height
relative to the screen height is `tan(asin(r/d)) / tan(halfFov)`. The formula above solves that
for `d`. `ShareAtDistance` is the inverse, used by the settings panel to show how much of the
screen the current target fills.

- **Radius** comes from the target's `SphereCollider` (scaled by `lossyScale`), which is the same
  collider the game uses for its closest-zoom limit. Falls back to the largest extent of any
  other `Collider`.
- **Category** comes from `ObjectInfo.objectTypes`: `Moons` → moons; `Asteroid`, `Comet`, `Belt`
  → asteroids & comets; `Orbit`, `Spacecraft`, or no `ObjectInfo` at all → spacecraft; everything
  else (planets, dwarf planets, the Sun) → planets.
- **Spacecraft and stations** have no meaningful size, so their game distance is multiplied by
  `ZoomSpacecraft` instead.
- A share of 0 keeps the game's distance for that category.
- The result is written to `MyCameraController.FinalZoom` as a negative value (the camera sits
  behind the target). The setter clamps to the game's zoom limits and restarts the zoom
  animation, so the game's own transition is used.

### Where it hooks

The game sets `FinalZoom` inside `ChangeTarget` and `SetTargetAndZoomBasedOnTimeScaleAndObjectType`,
and these call each other. `ZoomScaler` tracks nesting depth and only acts when the **outermost**
call finishes. It records `CurrentFinalZoom` on entry and does nothing if it didn't change, which
covers blocked moves (see above) and re-selecting the same target without a reset.

`SetTargetAndZoomBasedOnTimeScaleAndObjectType` is also hooked on its own because the game calls
it when the time speed changes and the camera swaps between a moon or ship and its parent planet.

---

## View offset beside side panels

**File:** `ViewOffset.cs`

### The problem

Info windows open on the right side of the screen and cover part of the view. The object the
camera is centred on is often behind them.

### The approach: an off-centre projection

Rather than moving the camera, the mod **skews the camera's projection matrix** so the centre of
the view sits in the middle of the uncovered area:

```csharp
cam.ResetProjectionMatrix();
Matrix4x4 m = cam.projectionMatrix;
m[0, 2] += shift;   // shift = covered width / screen width * strength
cam.projectionMatrix = m;
```

In normalised device coordinates the screen spans -1..1, so adding the covered fraction of the
width to `m[0,2]` moves the centre left by half that, which is the middle of the uncovered area.

Why the projection and not the camera position:

- The camera, its target and the scene are unchanged, so nothing else in the game (orbit lines,
  labels, the game's own camera logic) needs to know about the offset.
- Unity builds mouse rays from the same projection matrix, so **clicking on objects stays
  accurate** with no extra work.
- Turning it off is just `ResetProjectionMatrix()`.

It runs in the plugin's `LateUpdate`, after `MyCameraController.Update` has positioned the camera
for the frame. It applies to the main camera plus any enabled perspective cameras under it, so
every 3D layer shifts together.

### Measuring how much is covered

Every frame, `MeasureCovered` reads the open windows directly from `UIManager`'s private `windows`
and `windowsSecond` dictionaries and converts each one's `RectTransform` to screen pixels. It then
walks leftwards from the right edge:

- The first window may start up to `EdgeMargin` (120 px at 1080p) from the edge, because the
  game parks windows 77 px left of the edge to leave room for its right-hand toolbar.
- After that, any window whose right edge is within `StackGap` (default 20 px) of the covered
  area joins it. This is how two windows stacked side by side count as one panel.
- Windows wider than 80% of the screen are treated as full-screen screens, not side panels, and
  ignored.

Because each window's rect is read live, the offset follows **slide** animations exactly with no
extra smoothing. **Fade** animations are handled by multiplying the window's width by its
`CanvasGroup.alpha`, but only while closing. When the game switches windows it hides the old one
instantly and fades the new one in; counting the fade-in would make the view dip towards the
centre and back.

### Avoiding jumps when windows swap

Two game behaviours would otherwise make the view jump:

- **Sudden drops.** When a window is removed outright, the game often opens another one a frame
  later (for example, closing the spacecraft window reopens the object window). If the covered
  width drops by more than 100 px (at 1080p) in one frame, the previous width is held for 0.25 s
  before following the drop.
- **Spacecraft window.** `SpaceCraftInfoWindow` reopens the window it came from (`OpenedFrom`)
  only after it has fully finished closing, plus a frame. While it's closing it's counted at the
  rect it had while open (remembered in `LastOpenRect`), and the sudden-drop hold bridges the gap
  until the other window appears.

A short exponential ease (about 95% of the way in 0.1 s) smooths windows that appear without any
animation.

### Settings

`Strength` (default 0.83) scales the shift; 1.0 puts the centre exactly in the middle of the
uncovered area. `WhenNotFollowing` controls whether the offset also applies when the camera has no
target.

---

## Post-processing compatibility

**File:** `PostProcessingPatches.cs`

The game uses Unity's Post-Processing Stack v2. With anti-aliasing on, `PostProcessLayer`
resets the camera's projection in `OnPreCull` (to add its jitter) and again in `OnPostRender`.
That undoes the view offset.

The mod adds a postfix to both methods that calls `ViewOffset.Reapply`, which adds the shift back
on top of whatever the post-processing layer set, so its anti-aliasing jitter is kept. It checks
first whether the matrix was actually reset (`m[0,2]` is near 0 plus a tiny jitter, rather than
near the shift) so the shift is never applied twice. `nonJitteredProjectionMatrix` is corrected
the same way.

`PostProcessLayer` is looked up by name (`AccessTools.TypeByName`) so the project doesn't need to
reference the post-processing assembly. If the type isn't found, `Prepare()` returns false and the
patches are skipped.

---

## HUD tweaks

**File:** `HudTweaks.cs`

### Company logo

The logo is `UIManager`'s private `corporationLogo` image, but it has a frame drawn by sibling or
parent graphics. `CompanyLogoLevel` picks which ancestor to hide (default 1: the logo's parent,
which covers the logo and its frame).

Every `Graphic` under that object is **disabled rather than deactivating the GameObject**, so the
game's scripts on it keep running. This is re-checked every frame because the game may re-enable
or add graphics. Everything that was disabled is remembered and re-enabled if the setting is turned
off.

### Contracts box

The contracts box is found with `FindObjectOfType<CurrentContractListMainUI>()`, at most once per
second until it exists, since that call is slow. `ContractsBoxLevel` picks which ancestor to move
(default 0: the list itself).

The original `anchoredPosition` and `localScale` are captured once, and every frame the box is set
to the original plus the configured offset and scale. If the parent has a `LayoutGroup`, it would
snap the box back each frame, so the box gets a `LayoutElement` with `ignoreLayout = true`. If the
target changes (a different level is chosen), the previous object is restored to its original
position, scale and layout setting, and any `LayoutElement` the mod added is destroyed.

`AncestorAt` walks up the hierarchy but stops below the canvas, so a large level can't move the
whole canvas.

### Making room for the tutorial advisor

The tutorial's advisor box (portrait, typed-out text and Skip button) isn't positioned by code:
`TutorialWindowAdvisor.SetData` ignores the position it's given. In the scene it's anchored to the
top-left corner at (325, -4), and at 1080p it reaches about 203px down. The unmodded contracts box
starts at 237px, so the two were simply placed one above the other by hand. Moving the box up puts
it underneath the advisor, which draws on top because `Windows` comes after `LeftPanel`.

While `TutorialManager.Instance.TutorialWindowAdvisor` is active, the box is pushed down until its
top edge is 10 units below the advisor's lowest visible part. Only `ImageFrame`, `ScrollFrame` and
`ScrollFrame/ButtonSkip` are measured; the advisor's `Gradient` child is a 1500px-tall backdrop and
would push the box off screen. Both edges are converted into the box's parent space, measured with
the box at its configured position, so the push is only as much as needed (none if the box is
already lower). Two limits apply:

- If the box doesn't overlap the advisor horizontally (moved aside with `ContractsBoxX`), it isn't
  pushed at all.
- The push never takes the box's bottom edge below the bottom of the root canvas. If there isn't
  enough room, the box goes as low as it can and the advisor, drawn on top, covers its top.

The box slides to and from that position with an exponential ease on unscaled time, since the
tutorial often pauses the game. The pointer windows (`TutorialWindowCenter/Left/Right/Top/Bottom`)
need no change: `TutorialManager.Update` re-reads the highlighted element's screen rect every frame,
so they already follow the moved box.

---

## Settings panel

**File:** `TuningPanel.cs`, settings in `Plugin.cs`

An IMGUI window toggled with F9 (configurable). F8 is avoided because it's the game's bug-report
key; a config left on F8 from an earlier version is migrated to F9 on load.

- Sliders change values every frame while dragged, so `SaveOnConfigSet` is off and the config is
  saved two seconds after the last change, when the panel closes, and when the game exits.
- The panel scales with screen height so it's readable above 1080p.
- It shows the current camera target, its zoom category and how much of the screen it fills,
  plus a **Re-center** button to try zoom settings without clicking around.
- `InputBehindPanelPatch` skips `Manager.InputManager.Update` while the mouse is over the panel
  and a button is held or released, so clicks on the panel don't also click planets behind it.

---

## Robustness

**File:** `Plugin.cs`

Built so that a game update breaks as little as possible:

- **Patches are applied one class at a time.** If one no longer matches the game (a renamed or
  removed method), it's logged as an error and the rest still apply.
- **Window click patches resolve methods by name** and skip missing or ambiguous ones with a
  warning (see above).
- **Per-frame features are guarded.** If `ViewOffset` or `HudTweaks` throws, the error is logged
  once and that feature is turned off for the session, instead of filling the log every frame.
- All Harmony patches are removed in `OnDestroy`.

---

## Game code touched

A quick reference of every game type and member the mod depends on. If a game update changes one
of these, the matching feature is the one to check.

### Patched methods

| Type | Member | Patch | Feature |
|---|---|---|---|
| `CameraControl.MyCameraController` | `ChangeTarget(Transform, bool)` | Prefix (block), Prefix/Finalizer (zoom) | Click gate, zoom |
| `CameraControl.MyCameraController` | `ChangeTarget(ObjectInfo, bool)` | Prefix (block), Prefix/Finalizer (zoom) | Click gate, zoom |
| `CameraControl.MyCameraController` | `ChangeTargetForPlanMission` | Prefix (block) | Trip planner |
| `CameraControl.MyCameraController` | `SetTargetAndZoomBasedOnTimeScaleAndObjectType` | Prefix/Finalizer | Zoom |
| `InfoBase` | `MyOnMouseUpAsButton`, `MyOnMouseUpAsButtonFromObjectLabel` | Prefix/Finalizer | Click gate |
| `HighlightHoverObject` | `ChangeTarget` | Prefix/Finalizer | Click gate |
| `ChangeTargetButtons` | `ChangeTarget` | Prefix/Finalizer | Click gate |
| `Bilboard` | `MyOnMouseUpAsButton` | Prefix/Finalizer | Click gate |
| `MissionsLabelsMainUI` | `Click` | Prefix/Finalizer | Click gate |
| `PMTabDestination` | `ActiveTab`, `DestinationInputOnObjectSelect` | Prefix/Finalizer | Trip planner |
| `PMTabSchedule` | `CreateFly` | Prefix/Finalizer | Trip planner |
| `PlanMissionWindow` | `ChangeTargetForCamera` | Prefix (skip) | Trip planner |
| Window rows and buttons | see [Methods patched](#methods-patched) | Prefix/Finalizer | Window clicks |
| `UnityEngine.Rendering.PostProcessing.PostProcessLayer` | `OnPreCull`, `OnPostRender` | Postfix | View offset |
| `Manager.InputManager` | `Update` | Prefix (skip) | Settings panel |

### Private fields read

| Type | Field | Used for |
|---|---|---|
| `UIManager` | `windows`, `windowsSecond` | Finding open windows for the view offset |
| `UIManager` | `corporationLogo` | Hiding the logo |
| `UIWindow` | `canvasGroup` | Fade-out opacity |
| `UIWindow` | `showLeftRight` | Whether a window slides or fades |
| `Bilboard` | `targetCamera` | What a marker centres on |

### Public members used

`MyCameraController`: `Target`, `MyCamera`, `CurrentFinalZoom`, `FinalZoom`, `TargetIsSCOnFly`.
`UIWindow`: `Open`, `ShowingOrHiding`. `SpaceCraftInfoWindow.OpenedFrom`.
`ObjectInfo.objectTypes`. `HighlightHoverObject.MyTargetObjectInfo`.
`ChangeTargetButtons.idMyTargetObjectInfo`. `MissionsLabelsMainUI.MissionInfo`.
`CurrentContractListMainUI`. `TutorialManager.TutorialWindowAdvisor` (and the advisor's child names `ImageFrame`, `ScrollFrame`, `ButtonSkip`).
