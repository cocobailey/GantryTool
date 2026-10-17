# Gantry Tool

A Cities: Skylines 1 mod for placing overhead truss gantries with LED lane signals, speed limits, warnings
and your own signs, in six regional styles. Each signal head sits over its lane and has two faces, one for
each direction. Gantries are visual only and never affect traffic.

Requires the **Harmony** workshop item (CitiesHarmony, 2040656402).

## Using it

| Action | How |
|---|---|
| Open / close the tool | The Gantry Tool icon in the **Unified UI** toolbar if you use UUI, otherwise the floating **Π** button (drag it by its top strip); or **Ctrl+Shift+G** |
| Place a gantry | Left-click the first post, then left-click the second (span 6–80 m). The preview shows the real beam height, the signal heads, and a circle per detected lane. |
| Edit a gantry | Left-click its truss. Drag the post handles to move a post, the centre handle to move the whole gantry, or the ring to rotate it. The **Gantry** panel has its settings. |
| Edit a signal | Left-click a signal head. Drag it to shift it along the beam, or drag its corner to resize it. The **Lane signal** panel has its signs. |
| Delete a gantry | Hover it, then right-click (or **Delete gantry** in its panel) |
| Undo | **Ctrl+Z** (moves, settings, signs, deletes) |
| Cancel / leave the tool | Right-click or Esc |

### Gantry panel

| Setting | |
|---|---|
| Sign style | **Default** (the style chosen in the options) or a specific region |
| Height above road | Beam underside above the highest road it crosses (default 5.5 m) |
| Truss depth | Height of the truss |
| Signal box size | This gantry's box size, or the default from the options |

### Lane signal panel

The two faces of the head are at the top; click one to edit it. Pick a sign from a category:

- **Lane signals**: Auto, open (green ↓), closed (red ✕), merge left / right (blinking), reserved (◇),
  blank, and the yellow ✕ (lane closing ahead) in North American styles.
- **Speed limits**: a fixed number in the style's units (km/h 30–130, mph 20–80), or **Road**, which
  shows the lane's actual limit and follows changes, including TM:PE speed limits.
- **Warnings**: roadworks, queue ahead, accident, slippery road, fog, general danger.
- **Restrictions**: no overtaking, no overtaking for trucks, end of all restrictions.
- **Custom**: your own text or pictures (see below).

**Apply this sign to** copies the selected face's sign to every lane heading the same direction, to the
same face of every lane, or to both faces of the whole gantry. The panel also shifts and resizes the head,
and can remove it (removed heads still highlight on hover, so they can be restored).

Signs are tied to the lane's position on the gantry, so they survive the road being upgraded or reversed
with the same layout.

### Regional styles

| Style | Units | Look |
|---|---|---|
| Europe (DE/AT/CH) | km/h | Red-ring speeds, red triangle warnings, yellow merge arrows |
| Netherlands | km/h | White merge arrows, "END" for end of restrictions; no warnings |
| United Kingdom | mph | White merge arrows, open lanes blank on Auto, national speed limit sign |
| United States | mph | "SPEED LIMIT" signs, yellow ✕, yellow diamond warnings, "DO NOT PASS" |
| Canada | km/h | Printed "MAXIMUM" sign with an amber LED window and a beacon that flashes while the limit is below the road's |
| Australia / NZ | km/h | White merge arrows, yellow diamond warnings, "NO OVERTAKING" |

A sign a style doesn't have shows that style's closest equivalent; the head keeps its sign, so switching
back shows it again.

### Custom signs

In the **Custom** category:

- **Text**: up to two lines of 7 letters (A–Z, 0–9, `- / . !`) in white, amber, red, green or yellow.
- **Pictures**: PNG or JPG files from
  `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\GantryTool\Signs` (**Open folder**, then **Rescan**),
  shown as they are or redrawn as LED dots.

Pictures are shrunk to fit 64 KB and stored in the save, so shared cities show them too. Up to 24 custom
signs per city; signs no head uses are dropped when the city is saved.

### TM:PE

If TM:PE is enabled:

- Lane restrictions feed the Auto signals: lanes banned for every road vehicle show a red ✕ on both faces,
  bus-only lanes show a white ◇ instead of the green ↓.
- Speed limits set in TM:PE are what **Road** speed signs show.

Changes in TM:PE update the signals within about half a second. Manual signs still win. The restriction
handling can be turned off in the options.

### Ramp Meter

If the Ramp Meter mod is enabled, **Warnings** has a **Ramp metered** sign: a yellow RAMP METERED WHEN FLASHING
panel with two amber beacons on top. When you pick it, the face follows the nearest ramp meter ahead of the traffic
that reads it (up to 2 km). The beacons take turns flashing while that meter is metering, and stay dark when it
isn't, when the meter is gone, or when Ramp Meter isn't installed. Pick the sign again to link it to a different
meter. The panel shows which meter state the sign is following.

### What counts as a lane

Car-carrying lanes only, which includes bus, tram and trolleybus lanes that cars share. Pedestrian,
parking, bike-only and rail lanes are skipped, as are lanes inside tunnels; slopes count. Lanes running
within 20° of the gantry line are ignored. Where roads are stacked, only the top road gets signals.

## Options

Content Manager → Mods → Gantry Tool → Options. Saved in
`%LOCALAPPDATA%\Colossal Order\Cities_Skylines\GantryTool.xml`.

| Option | Default | Applies to |
|---|---|---|
| Height above road | 5.5 m | New gantries; the button applies it to all |
| Truss depth | 1.5 m | New gantries; the button applies it to all |
| Default sign style | Europe | Every gantry set to "Default" |
| Signal size | 1.2 m | Gantries without their own box size |
| Steel brightness | 0.62 | All gantries |
| Use TM:PE lane restrictions | on | All gantries |
| Road search padding | 128 m | Raise if very long Network Anarchy segments are missed |
| Debug logging | off | Per-lane details in `output_log.txt` |

## Compatibility with 1.0

Saves made with 1.1 still load in 1.0. Anything 1.0 doesn't know (new signs, styles, head positions) shows
as Auto there, and comes back when the city is opened in 1.1 again, even if it was saved in 1.0 in between.
Gantries placed with 1.0 keep their exact height and look.

## Building

Requires the .NET SDK 8 or newer; the mod itself targets .NET 3.5.

1. Set the paths in `GantryTool.csproj` to match your install:
   - `ManagedDLLPath`: the game's `Cities_Data\Managed` folder.
   - `TmpeDLLPath`: TM:PE's Workshop folder (`...\workshop\content\255710\1637663252`), for `TMPE.API.dll`.
2. Build:

   ```
   dotnet build -c Release
   ```

   Add `-p:Deploy=true` to copy the mod to
   `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Addons\Mods\GantryTool\`.

3. Save-compatibility checks (run after building; they include 1.0's own loader):

   ```
   dotnet run -c Release --project tests/GantryTool.Tests
   ```

## License

[MIT](LICENSE).

## How it works

In short:

- **Lane direction:** `Invert ? InvertDirection(m_finalDirection) : m_finalDirection`, the same rule as
  `PathFind` and `NetSegment.CountLanes`. Left-hand traffic is already folded into `m_finalDirection`.
- **Updates:** a postfix on `NetSegment.UpdateLanes` and a prefix on
  `NetManager.ReleaseSegmentImplementation` queue changes from the simulation thread. The main thread
  matches them to gantries and rebuilds after a 0.3 s settle delay. A 5 s round-robin re-scan is the
  fallback. TM:PE speed limit and restriction changes come through its notifier.
- **Rendering:** runtime meshes drawn with the vanilla prop shader, borrowed from a loaded prop. Each style
  has a sign atlas generated in code (dot-matrix LEDs, an LED dot font, printed signs), lit through the
  shader's illumination channel, with mip levels biased towards lit dots so signs stay bright at a distance.
- **Save data:** three keys. `GantryTool` is the 1.0 format (v3), so 1.0 can still read it.
  `GantryTool.Ext` holds everything added since (v4), matched to gantries by id and post positions.
  `GantryTool.Custom` holds the custom sign library. Lanes and meshes are always recomputed on load.

Logs are prefixed `[GantryTool]`.
