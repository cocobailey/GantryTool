# Gantry Tool

A Cities: Skylines 1 mod for placing overhead truss gantries with lane control signals. Each signal head
sits over its lane and has two faces: drivers see a green ↓ over lanes going their way and a red ✕ over
lanes coming at them. Gantries are visual only and never affect traffic.

Requires the **Harmony** workshop item (CitiesHarmony, 2040656402).

## Using it

| Action | How |
|---|---|
| Open / close the tool | The Gantry Tool icon in the **Unified UI** toolbar if you use UUI, otherwise the floating **Π** button (drag it by its top strip); or **Ctrl+Shift+G** |
| Place a gantry | Left-click the first post, then left-click the second (span 6–80 m). The preview shows the real beam height, the signal heads, and a circle per detected lane. |
| Cancel the first post | Right-click or Esc |
| Delete a gantry | Hover it until it turns red, then right-click |
| Edit a signal | Left-click a signal head to open the **Lane signal** panel |
| Leave the tool | Right-click or Esc with nothing selected |

### Signals

Signals follow the road automatically: build, upgrade, reverse, move or bulldoze the road and the heads
update within about half a second. A gantry with no road under it is left bare.

In the **Lane signal** panel each face has its own dropdown:

- **Auto**: green ↓ / red ✕ from the lane's direction
- **Green arrow**: lane open
- **Red X**: lane closed
- **Yellow arrow down-left / down-right**: merge (blinks)
- **White diamond**: reserved lane
- **Blank**: off

Manual choices are saved with the city. They are tied to the lane's position on the gantry, so they
survive the road being upgraded or reversed with the same layout.

### TM:PE vehicle restrictions

If TM:PE is enabled, its per-lane vehicle restrictions feed the automatic signals:

- **Every road vehicle banned** (including emergency and service): red ✕ on both faces.
- **Bus-only** (buses allowed, private cars and trucks not): white ◇ on the face that would show the
  green ↓. The face oncoming traffic sees keeps its red ✕.
- **Anything else**: the normal signals for the lane's direction.

Changing a restriction in TM:PE updates the signals within about half a second. Manual choices in the
Lane signal panel still win. TM:PE is optional: without it the mod behaves as above with no restriction
handling. The integration can be turned off in the options.

### What counts as a lane

Car-carrying lanes only, which includes bus, tram and trolleybus lanes that cars share. Pedestrian,
parking, bike-only and rail lanes are skipped, as are lanes inside tunnels; slopes count. Lanes running
within 20° of the gantry line are ignored. Where roads are stacked, only the top road gets signals.
The beam is raised to clear the highest road crossed.

## Options

Content Manager → Mods → Gantry Tool → Options. Saved in
`%LOCALAPPDATA%\Colossal Order\Cities_Skylines\GantryTool.xml`.

| Option | Default | Applies to |
|---|---|---|
| Post height | 7.5 m | New gantries; the button applies it to all |
| Truss depth | 1.5 m | New gantries; the button applies it to all |
| Road clearance | 5.5 m | All gantries |
| Signal size | 1.2 m | All gantries (limited by truss depth) |
| Steel brightness | 0.62 | All gantries |
| Road search padding | 128 m | Raise if very long Network Anarchy segments are missed |
| Debug logging | off | Per-lane details in `output_log.txt` |

## Building

Requires the .NET SDK 8 or newer; the mod itself targets .NET 3.5.

1. Set the paths in `GantryTool.csproj` to match your install:
   - `ManagedDLLPath`: the game's `Cities_Data\Managed` folder.
   - `TmpeDLLPath`: TM:PE's Workshop folder (`...\workshop\content\255710\1637663252`), for `TMPE.API.dll`.
2. Build:

   ```
   dotnet build -c Release
   ```

The build copies the mod to `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Addons\Mods\GantryTool\`.

## License

[MIT](LICENSE).

## How it works

In short:

- **Lane direction:** `Invert ? InvertDirection(m_finalDirection) : m_finalDirection`, the same rule as
  `PathFind` and `NetSegment.CountLanes`. Left-hand traffic is already folded into `m_finalDirection`.
- **Updates:** a postfix on `NetSegment.UpdateLanes` and a prefix on
  `NetManager.ReleaseSegmentImplementation` queue changes from the simulation thread. The main thread
  matches them to gantries and rebuilds after a 0.3 s settle delay. A 5 s round-robin re-scan is the
  fallback.
- **Rendering:** runtime meshes drawn with the vanilla prop shader, borrowed from a loaded prop. The
  signal faces use a dot-matrix atlas generated in code, lit through the shader's illumination channel.
- **Save data:** key `GantryTool`, versioned binary (v2). Lanes and meshes are always recomputed on load.

Logs are prefixed `[GantryTool]`.
