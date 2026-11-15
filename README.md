# Gantry and Signs Tool

A Cities: Skylines 1 mod for placing overhead sign gantries. They carry two kinds of sign:

- **Lane signals**: LED signal heads over each lane, with two faces, one for each direction. They show lane arrows
  and crosses, speed limits, warnings and your own text or pictures, in six regional styles.
- **Guide signs** (new in 2.0): printed direction, exit, HOV and message signs in six countries' styles, built in
  an editor and fitted over the lanes they're about.

Gantries are visual only and never change how traffic behaves.

Requires the **Harmony** workshop item (CitiesHarmony, 2040656402).

## Using it

| Action | How |
|---|---|
| Open / close the tool | The Gantry Tool icon in the **Unified UI** toolbar if you use UUI, otherwise the floating **Π** button (drag it by its top strip); or **Ctrl+Shift+G** |
| Place a gantry | Left-click the first post, then left-click the second (span 6–80 m). The preview shows the real beam height and a circle per detected lane. |
| Edit a gantry | Left-click its steel. Drag the post handles to move a post, the centre handle to move the whole gantry, or the ring to rotate it. The **Gantry** panel has its settings. |
| Edit a signal | Left-click a signal head. Drag it along the beam, or drag its corner to resize it. The **Lane signal** panel has its signs. |
| Edit a guide sign | Left-click it. The **Guide sign** panel opens on it. |
| Delete a gantry | Hover it, then right-click (or **Delete gantry** in its panel) |
| Undo / redo | **Ctrl+Z** / **Ctrl+Y** (moves, settings, signs, deletes) |
| Cancel / leave the tool | Right-click or Esc |

A new gantry starts as just the steel, and its panel asks whether it gets lane signals or a guide sign. The options
can make new gantries start with either instead.

### Gantry panel

| Setting | |
|---|---|
| Type | **Span** (a post at each end), **Cantilever** (one post, either end), **Median T** (one post in the middle), or **On bridge** (no posts: a bar hung under the edge of a bridge or raised road running along it, which snaps under the deck) |
| Look | Box truss, flat truss, double tube, single tube, curved tube or tri-chord |
| Paint | Bare steel, dark, green, blue, brown or weathered |
| Signal style | **Default** (the style chosen in the options) or a specific region |
| Height above road | Beam underside above the highest road it crosses (default 5.5 m) |
| Truss depth | Height of the truss |
| Signal box size | This gantry's box size, or the default from the options |
| Lane signals | Automatic signals over each lane; off for a signs-only gantry |
| No red Xs on the back of signals | The back of each signal shows nothing instead of a red ✕ |
| Lower speed signals in heavy traffic | See [Live signals](#live-signals) |
| Catwalk, lamps on guide signs | A walkway along the beam, and lamps over the guide signs |

**Add signal** adds a signal head beyond the automatic one per lane, and **Add guide sign** adds a guide sign (up to 8
of each per gantry).

### Lane signal panel

The two faces of the head are at the top; click one to edit it. Pick a sign from a category:

- **Lane signals**: Auto, open (green ↓), closed (red ✕), merge left / right (blinking), exit left / right
  (green ↖ ↗), reserved (◇), blank, and the yellow ✕ (lane closing ahead) in North American styles.
- **Speed limits**: a fixed number in the style's units (km/h 30–130, mph 20–80), or **Road**, which
  shows the lane's actual limit and follows changes, including TM:PE speed limits.
- **Warnings**: roadworks, queue ahead, accident, slippery road, fog, general danger.
- **Restrictions**: no overtaking, no overtaking for trucks, end of all restrictions.
- **Custom**: your own text or pictures (see below), and changing messages.

**Apply this sign to** copies the selected face's sign to every lane heading the same direction, to the
same face of every lane, or to both faces of the whole gantry. The panel also shifts and resizes the head,
and can remove it (removed heads still highlight on hover, so they can be restored).

Signs are tied to the lane's position on the gantry, so they survive the road being upgraded or reversed
with the same layout.

**Added signals** stay where you put them and show only signs you pick. **Link to lane** makes one follow that lane
and show Auto and Road speed too, for example a speed box beside each lane's arrow. If the lane goes away, the
signal unlinks where it stands.

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

- **Text**: up to three lines of 10 letters (A–Z, 0–9, `- / . !`) in white, amber, red, green or yellow. Short text
  is drawn large; only lines too wide for the normal LED grid use smaller dots.
- **Pictures**: PNG or JPG files from the `GantryTool/Signs` folder (**Open folder**, then **Rescan**), shown as
  they are or redrawn as LED dots.
- **Changing messages**: show a custom sign on a face and press **Add to changing messages**; do the same with
  more. The face then changes between them on its own, every few seconds (**Every N s**), always or only in heavy
  traffic. **Stop changing** ends it.

Pictures are shrunk to fit 64 KB and stored in the save, so shared cities show them too. Up to 24 custom
signs per city; signs no head uses are dropped when the city is saved.

## Guide signs

**Add guide sign** in the gantry panel adds a printed sign, fitted over the lanes beneath it. Its panel has two pages:

- **Design**: what the sign says. Click any part of the sign's picture to change it, or drag it somewhere else; it
  snaps above, below or beside the other parts. **+ Text**, **+ Shield**, **+ Arrow**, **+ Lane arrows**,
  **+ Exit tab**, **+ Exit only band / plate**, **+ Divider** and **+ Your own picture** add parts.
  **Choose a different sign...** starts from a ready-made one.
- **Placement**: which way it faces, the lanes it covers and its size. The picture shows that side of the gantry as
  drivers see it: drag a sign along the lanes, or by its yellow edges to cover more or fewer. Signs facing the same
  way line up and share a size unless you set one.

Ready-made signs come in **Direction signs**, **Exits**, **HOV and bus lanes** and **Other signs** (messages,
services, lane closed, blank), for the **United States, Canada, United Kingdom, Netherlands, Europe (German style)
and Australia / New Zealand**. Canada includes B.C. and Ontario HOV and bus lane signs, from the provinces' sign
catalogues. Each city keeps the country it was started with; the options set the one for new cities.

- **Road names**: new signs start with the name of the road below, or of the road the next exit leads to.
  **Fill in from the road** does it again, and signs follow a renamed road, except words you've changed yourself.
- **Message signs**: up to 5 messages that change on their own, always or only in heavy traffic.
- **Your own pictures**: PNG or JPG, on part of a sign or covering it all. They're saved with the city.
- **Copy and reuse**: Ctrl+C / Ctrl+V, copy to the other side, copy along a road, add an advance sign on the
  gantry before, or **Save as my sign...** to reuse in any city. **Export my signs** and **Import sign packs**
  share them as `.signpack` files.
- A sign that would sit over a signal stands on top of the truss instead; on flat trusses, tubes and bridges signs
  hang on rails tied to the steel.

## Live signals

With **Lower speed signals in heavy traffic** on, speed signals step down when traffic under the gantry slows:
20 km/h (or the mph equivalent) at a time, three steps at most, never below 30. Traffic counts as heavy when cars
drive on average this far under the limit (30% by default; set in the options), measured every 5 seconds and
smoothed so one car braking doesn't flick the signs. Canadian speed signs flash their beacon while lowered.

Message signs and changing messages set to **only in heavy traffic** use the same measure. Cars themselves don't slow
down: the signs are advisory.

### TM:PE

If TM:PE is enabled:

- Lane restrictions feed the Auto signals: lanes banned for every road vehicle show a red ✕ on both faces,
  bus-only lanes show a white ◇ instead of the green ↓.
- Speed limits set in TM:PE are what **Road** speed signs show, and what live speed signals compare against.

Changes in TM:PE update the signals within about half a second. Manual signs still win. The restriction
handling can be turned off in the options.

### What counts as a lane

Car-carrying lanes only, which includes bus, tram and trolleybus lanes that cars share. Pedestrian,
parking, bike-only and rail lanes are skipped, as are lanes inside tunnels; slopes count. Lanes running
within 20° of the gantry line are ignored. Where roads are stacked, only the top road gets signals.

## Options

Content Manager → Mods → Gantry and Signs Tool → Options. Saved in `GantryTool.xml` in the game's data folder
(`%LOCALAPPDATA%\Colossal Order\Cities_Skylines` on Windows, `~/Library/Application Support/Colossal Order/Cities_Skylines`
on a Mac). Pictures and saved signs live in the `GantryTool` folder next to it.

| Option | Default | Applies to |
|---|---|---|
| Height above road, truss depth | 5.5 m, 1.5 m | New gantries; the button applies them to all |
| New gantries start with | Ask | Ask, lane signals or a guide sign |
| New gantry type, built from | Span, box truss | New gantries |
| No red Xs on the back of signals | off | New gantries |
| New guide signs' text and symbol size | 100% | New guide signs |
| Heavy traffic | 30% under the limit | Live speed signals and heavy-traffic messages |
| Fill in road names on new guide signs | on | New guide signs |
| Keep guide signs up to date when a road is renamed | on | All guide signs |
| Guide sign country for new cities | From the signal style | New cities |
| Default signal style | Europe | Every gantry set to "Default" |
| Signal size | 1.2 m | Gantries without their own box size |
| Steel brightness | 0.62 | All gantries |
| Use TM:PE lane restrictions | on | All gantries |
| Road search padding | 128 m | Raise if very long Network Anarchy segments are missed |
| Debug logging | off | Per-lane details in the game log |

## Compatibility

Saves made with 2.0 still load in 1.x: guide signs, the gantry types and looks, and live signals are kept in save
blocks of their own that older versions don't read. There, every gantry is a box truss span with automatic lane
signals, as 1.x builds them. Opening the city in 2.0 again brings everything back, even if it was saved in 1.x in
between, except on gantries moved or deleted there. Gantries placed with 1.x keep their exact height and look.

Works on Windows and macOS, and with FPS Booster (2.0 fixes its "errors during patching" message for this mod).

## Building

Requires the .NET SDK 8 or newer; the mod itself targets .NET 3.5.

1. The game's paths are set in `GantryTool.csproj`: Windows paths at the top, and a macOS group that takes over on
   a Mac. Change them if your game is somewhere else:
   - `ManagedDLLPath`: the game's `Cities_Data\Managed` folder (on a Mac, `Cities.app/Contents/Resources/Data/Managed`).
   - `TmpeDLLPath`: TM:PE's Workshop folder (`.../workshop/content/255710/1637663252`), for `TMPE.API.dll`.
2. Build:

   ```
   dotnet build -c Release
   ```

   Add `-p:Deploy=true` to copy the mod to the game's local `Addons/Mods/GantryTool` folder.

3. Save-compatibility and sign checks (after building; they include 1.0's own loader):

   ```
   dotnet run -c Release --project tests/GantryTool.Tests
   ```

   On a Mac, build that project and run the `.exe` with Mono instead:
   `mono tests/GantryTool.Tests/bin/Release/net472/GantryTool.Tests.exe`. It expects copies of the Windows game's
   `Assembly-CSharp`, `ColossalManaged`, `ICities` and `UnityEngine` DLLs in `reference/windows-managed/`.

   One check looks up everything the mod uses from the game in each platform's DLLs: put copies in
   `reference/windows-managed/` and `reference/mac-managed/` (not in git). A platform without copies is skipped with
   a note. The checks also rewrite the built DLL with Mono.Cecil the way FPS Booster does at startup.

4. Guide sign fonts, shields and pictures are baked by `python tools/guide/make_assets.py` (needs Pillow).

## License and credits

[MIT](LICENSE). Guide signs build on **Sign Builder for Cities: Skylines II by Andy Smith**: its design format, layout
rules, themes and templates, adapted for Cities: Skylines 1. Thank you, Andy! Fonts, shields and sign pictures come
from the sources listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md), each under its own licence.

## How it works

In short:

- **Lane direction:** `Invert ? InvertDirection(m_finalDirection) : m_finalDirection`, the same rule as
  `PathFind` and `NetSegment.CountLanes`. Left-hand traffic is already folded into `m_finalDirection`.
- **Updates:** a postfix on `NetSegment.UpdateLanes` and a prefix on
  `NetManager.ReleaseSegmentImplementation` queue changes from the simulation thread. The main thread
  matches them to gantries and rebuilds after a 0.3 s settle delay. A 5 s round-robin re-scan is the
  fallback. TM:PE speed limit and restriction changes come through its notifier.
- **Rendering:** runtime meshes drawn with the vanilla prop shader, borrowed from a loaded prop. Each signal style
  has a sign atlas generated in code (dot-matrix LEDs, an LED dot font, printed signs), lit through the
  shader's illumination channel. Guide signs are laid out from JSON designs and drawn on a worker thread with
  fonts and shields baked into small files, one texture per distinct design.
- **Live signals:** every 5 s, the speed of each car on the gantry's lanes compared to its lane's limit.
- **Save data:** `GantryTool` is the 1.0 format (v3), so 1.0 can still read it. `GantryTool.Ext` holds per-gantry
  settings added since, matched to gantries by id and post positions. `GantryTool.Custom` holds the custom sign
  library and `GantryTool.Heads` added signals. 2.0 adds `GantryTool.Guide` (guide sign designs),
  `GantryTool.GuidePictures`, `GantryTool.Country` and `GantryTool.Live` (live speed and changing messages).
  Lanes and meshes are always recomputed on load.

Logs are prefixed `[GantryTool]`.
