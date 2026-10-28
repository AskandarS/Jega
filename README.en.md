# Jega

**From a Danish address to a site plan and terrain in Revit, in moments.**

*[Dansk udgave](README.md)*

Jega is a free, open source Revit add-in for the Danish building industry. You type an address. Jega fetches the lot,
the neighbours, the buildings, the road edges and the terrain from Denmark's open data (Matriklen, Danmarks Højdemodel,
GeoDanmark and Danmarks Adresseregister) and puts them into your model: the site plan as a DXF in the plan view, the
terrain as a Toposolid on the lot, the neighbouring buildings as 3D volumes with heights from the elevation model, and
the project georeferenced in real coordinates.

No server, no Jega account, no subscription. Jega talks to the public services directly, with your own free key.

> **Open beta, version 0.9.0-beta.1.** Jega works, but it is new. Bugs and ideas are welcome (see [Report a bug](#report-a-bug)).
> Jega speaks Danish to start with: step 0 of [Quick start](#quick-start) switches a project to English.

**To start:**

1. **Get Jega:** run `Jega-0.9.0-beta.1-setup.exe` from [Releases](https://github.com/AskandarS/Jega/releases) with
   Revit closed (see [Install](#install)).
2. **Make a free key** at [datafordeler.dk](https://datafordeler.dk): Log på Datafordeleren > Datafordeler
   Administration (first time: Opret Bruger), then IT-systemer > Opret > API-Keys > Opret. Copy the key right away; it
   can take up to 15 minutes to start working (see [Requirements](#requirements)).
3. **Run it in Revit:** the **Jega** tab > **Settings** > paste the key. Press **Load from Address**, type the address,
   and wait.

Step by step under [Quick start](#quick-start).

*The videos are in Danish.*

https://github.com/user-attachments/assets/60babc87-a76d-4ff2-a366-48443cdaff46

## What Jega does

- **The site plan** as a DXF, imported in your plan view: the lot with its boundary lengths, the neighbouring lots and
  parcel numbers, buildings, road and path edges, lakes and streams, trees and hedges, manhole covers and road gullies
  with their level, street names and house numbers, the setback line, spot elevations, roof heights, north arrow,
  scale bar, legend and notes. Each kind of thing is on its own layer. The buildings are GeoDanmark's roof surfaces,
  not facades. The texts are sized for the drawing's scale on A3; it is in the frame (SCALE 1:x; MÅLESTOK 1:x in a
  Danish project).
- **The terrain** from Danmarks Højdemodel, kept in the model.
- **Real coordinates:** the project is georeferenced (ETRS89/UTM32), and true north follows the coordinates.
- **A Toposolid** on the lot, shaped by the terrain (**Generate Terrain**).
- **The lot** as a filled region exactly on the lot boundary, with the elevations along it (**Draw Lot Boundary**).
- **Terrain lines:** draw a line and get the elevation at every corner, the falls, the slopes and HIGH / LOW
  (**Terrain Line**).
- **The neighbouring buildings** as masses (Generic Models), with heights from Danmarks Højdemodel (**Context
  Buildings**).
- **Export:** the site plan (DXF) and the terrain (CSV), e.g. for a surveyor or the municipality.

It is not a survey, it doesn't model the existing building, the Toposolid covers the lot only, and it fetches no BBR
or planning data. The lot is the one cadastral parcel the address point lies in; a property of several parcels gets
that one only, the others appear as neighbouring lots. Jega doesn't know what is under a manhole cover (sewer, water,
heating); utility information is obtained from LER before excavation (LER Act § 7). Danish addresses only. Revit LT
can't run add-ins.

## Requirements

- **Autodesk Revit 2025** (full Revit) on Windows. If you have Revit 2026 too, Jega loads there as well. It
  probably works, but hasn't been tried: please report how it goes.
- **Internet** to `adressevaelger.dk`, `wfs.datafordeler.dk`, `wcs.datafordeler.dk` and `graphql.datafordeler.dk`
  (HTTPS; all with your key or Adressevælger's token).
- **A free Datafordeler user and an API key.** Create a user at [datafordeler.dk](https://datafordeler.dk) and make an
  API key for it. Jega's Settings has the link **Get a key at datafordeler.dk** on the Data access tab. The way on the
  site (in Danish only): **Log på Datafordeleren > Datafordeler Administration**, log in with e-mail or MitID (the
  first time: **Opret Bruger**, create user), then **IT-systemer > Opret (create) > API-Keys > Opret**. The key is shown
  only once, so copy it straight away. A new key can take up to 15 minutes before it works.

https://github.com/user-attachments/assets/06826247-5b5b-4243-8e1c-1eeed88d9d4e

The key is your own, tied to your Datafordeler user. You are responsible for it and for keeping Datafordeleren's terms;
whether one key may be shared in an office is for the terms to say, not Jega.

## Install

1. Download `Jega-0.9.0-beta.1-setup.exe` under [Releases](https://github.com/AskandarS/Jega/releases). The file is
   new and few have downloaded it yet, so the browser may warn that it "isn't commonly downloaded". Choose to keep the
   file (in Edge: right-click the file > **Keep**).
2. Close Revit.
3. Run the file. Jega isn't signed yet, so Windows may say "Windows protected your PC". Click **More info**, then
   **Run anyway**.
4. Follow the installer. It needs no administrator rights: Jega is installed for your own Windows user only, in
   `%APPDATA%\Autodesk\ApplicationPlugins\Jega.bundle`, where Revit 2025 and 2026 find it themselves.
5. Start Revit. Revit shows **Security - Unsigned Add-In** ("The publisher of this add-in could not be verified"), as
   Jega isn't signed yet. Choose **Always Load**; Revit asks only the first time.
6. The **Jega** tab is now in the ribbon.

**A new version:** run the new installer. It installs over the old one without uninstalling first. With Revit open,
the installer asks you to close it and press **Retry**.

**Uninstall:** close Revit, then Windows' **Settings > Apps > Installed apps**, **Jega 0.9.0-beta.1 (Revit)**,
**Uninstall**. It removes
`Jega.bundle`. Your key stays, so a reinstall needs no new key: it is in `%APPDATA%\Jega` with the log and the
elevation cross family. To remove them too, type `%APPDATA%\Jega` in File Explorer's address bar, press Enter and
delete the folder; delete `%LOCALAPPDATA%\Jega` (the last terrain CSV folder) as well, if it is there.

What Jega made stays in the models as ordinary Revit elements. They open without Jega and without a warning. The
terrain lines' elevations just no longer follow.

## Quick start

### 0. Open a project and switch it to English

Open a project, or make a new one from your template: Jega's buttons are greyed out until one is open. Then press
**Indstillinger** (Settings) in the Jega tab, choose **English** in the list top right, and press **OK**.

Do it once in each project, before Load from Address: the language is saved in the project, and every new project
starts in Danish. (Switched later, Jega offers to draw the site plan again in English.)

### 1. The first time: your key

Press **Settings** again, now in English. Without a key it opens on the **Data access** tab. Paste the key into
**Datafordeler API key** and press **Test**. Three lines saying OK mean the map data, the terrain and the addresses
answer. Press **OK**. Forget it, and Load from Address asks for the key and carries on once it is saved.

### 2. Load from Address

Open a floor plan (Floor Plan or Site Plan) and press **Load from Address**. Type the address, e.g. Vejle Rådhus,
`Skolegade 1, 7100 Vejle`, pick the suggestion and press **Load** (or Enter). If Adressevælger doesn't answer or
refuses its token, Jega searches Danmarks Adresseregister on Datafordeleren with your key instead and says so under the
field, e.g. "Adressevælger couldn't be used (400, unknown token); searching via Datafordeleren now." That search
doesn't forgive typos: type street, number and town.

The site plan and the terrain come in, and usually the neighbouring buildings too (shown with **Context Buildings**).
The project gets real coordinates (ETRS89/UTM32). The project's origin is placed on the Project Base Point (the
address point, whole metres), so the coordinates are round numbers. True north follows the coordinates. It all takes one Undo.

If the lot is over about 160 m across, Jega first asks how fine the terrain grid should be. If the project has
coordinates already, Jega asks first (see [Georeference](#georeference)). If the neighbouring lots, the buildings, the
roads, the water, the trees and hedges, the manhole covers and gullies, the street names and house numbers or the
height model's surface (roof heights and neighbouring buildings) can't be fetched, the site plan comes without them
and the message says so; only the lot and the terrain are needed. Load again later.

### 3. Fit the house

The site plan is placed by the real coordinates and needn't be turned. Draw the house on the lot; if it is drawn already, move it onto
the lot with Revit's Move and Rotate.

To work square to the house, press **Work Direction**. It is Revit's Rotate Project North: choose
**Align Selected Line or Plane** and click one of the house's walls. The whole model turns together; the coordinates
and true north stay. Once the turning is done, Jega runs **Refresh** itself (its own Undo). Work Direction only works in a plan view set to Project North; switch
with **True / Project North**.

The site plan is drawn for the view it stands in: in Project North the map (the lot, the buildings, the roads and the
rest) lies turned as the model does, in True North north up. The frame, the title, the notes and the legend lie
straight in both, and the north arrow points north with its N upright. When the map lies turned, a note on the drawing
says by how many degrees. Jega's **True / Project North** draws it again at once. After **Work Direction**,
**Refresh** draws it again in the same place, and the message says "The site plan was drawn again for the view's
north: its frame and texts straight, the lines turned with the model."; the terrain frame, the elevations and the
Toposolid follow as before. If you switch the view's orientation in Revit's properties, the next Jega button
straightens it (Refresh runs first). With the plan imported in every view (**Only in that view** off), it can lie
straight in one kind of view at a time; it is drawn for the view you are in when it is drawn again. **Export** always
writes north up. To have the whole plan turn as one, paper and all, turn **The paper lies straight in the view** off
under Settings > **Site plan & terrain**.

### 4. Refresh

**Refresh** puts the site plan back on its coordinates and brings the terrain frame, the terrain line elevations, the
neighbouring buildings (if shown) and the Toposolid (if made) up to date. One Undo. Jega's other buttons refresh first
when the site plan has moved or turned since. If the site plan has been deleted, Refresh offers to draw it again from
what the model keeps.

### 5. Generate Terrain

**Generate Terrain** makes a Toposolid on the lot, bounded by the lot boundary and shaped by the terrain. The
Toposolid is placed on its own level, "Terrain", at the lowest point of the terrain by default. Another point: Settings >
**Site plan & terrain** > **Terrain point for the Terrain level**.

The Toposolid gets its own type, "Jega Terrain", copied from the model's Toposolid type. If the template has no
Toposolid type, Jega says so; get one with Manage > Transfer Project Standards.

Changed by hand, Jega asks before it makes it again. A copy you make of it is yours: Jega doesn't touch it.

### 6. Terrain Line

Press **Terrain Line** in a plan view and draw a line along the house. Revit's Detail Line starts in the line style
"Jega Line (elevations only)": every corner and end gets its elevation and a cross. Switch the line to "Jega Line" in
Properties for the falls, slopes and HIGH / LOW. Edit the line, and the elevations follow.

### 7. As needed

- **Context Buildings:** shows and hides the neighbouring buildings. **Shift+click** fetches them again.
- **Layers:** the site plan's layers and roof heights, e.g. **Trees, tree groups and woods (GeoDanmark; seen from the
  air, no species)**, **Hedges and fences (GeoDanmark)**, **Manhole covers with cover level (GeoDanmark; the kind
  isn't known)**, **Road gullies with level (GeoDanmark)**, **House numbers in the buildings (Danmarks
  Adresseregister)** and **Street names (Danmarks Adresseregister)**. At OK it is drawn again; nothing is fetched.
- **Relative elevations:** Settings > **Elevations (±0.00)** > **Relative elevations**, and enter the floor's
  elevation (see [Elevations](#elevations)).
- **Offset:** whole mm added to every elevation from Terrain Line and Draw Lot Boundary (elevation = terrain + offset),
  e.g. a paving's thickness (150) or an excavation (-900). Set it in Settings > **Elevations (±0.00)** > **Offset**, or
  in the lot elevations' window after **Draw Lot Boundary**; it is the same value, and the labels on the plan follow.
- **Export:** the site plan DXF and the terrain CSV to a folder you choose, as `<name>-siteplan.dxf` and
  `<name>-terrain.csv`; the name is the project file's. The DXF is always north up, as the surveyor's and the
  municipality's plans are.

## The ribbon

Before you switch a project to English (and in every new project), the ribbon is in Danish:

| English | Danish | Panel (Danish) |
|---|---|---|
| Load from Address | Hent fra adresse | Site (Grund) |
| Load Terrain CSV File (the arrow under it) | Indlæs terræn fra CSV | Site (Grund) |
| Refresh | Opdater | Site (Grund) |
| Layers | Lag | Site (Grund) |
| Work Direction | Arbejdsretning | Site (Grund) |
| True / Project North | True / Project North | Site (Grund) |
| Generate Terrain | Opret terræn | Model |
| Draw Lot Boundary | Tegn grund | Model |
| Context Buildings | Nabobygninger | Model |
| Terrain Line | Terrænlinje | Terrain Elevations (Terrænkoter) |
| Settings | Indstillinger | Jega |
| Export | Eksportér | Jega |
| About | Om Jega | Jega |

The buttons are greyed out with no project open and in the family editor; Work Direction and True / Project North
outside plan views too. **About** always works.

- **Load Terrain CSV File:** a terrain in Jega's own format, as Export writes it: one `X,Y,Z` line per point in mm, no
  header, a point as decimal separator; X and Y from the origin (the address point, whole metres), Z DVR90; a regular
  grid without holes.
- **Draw Lot Boundary:** the lot as a filled region ("Jega Lot Boundary") on its boundary, with the elevations along
  it. Refresh doesn't move it: draw it again when the site plan has moved.
- **True / Project North:** switches the active plan view's orientation. A view template that sets it decides.
- **About:** the version (at the bottom), the licence and where the data comes from.

## Settings

Five tabs: **Georeference**, **Elevations (±0.00)**, **Site plan & terrain**, **Layers** and **Data access** (the key,
and under **Advanced** an Adressevælger token of your own, normally empty). **Site plan & terrain** has **The paper
lies straight in the view (only the map turns)** (on by default; off, the whole plan turns as one); its **Advanced**
holds the Toposolid's **Point spacing inside the lot** and **Point spacing along the lot boundary**. Most of it is saved in the project; the key on your PC
only. The language is the list top right, saved in the project. The layer names (`GRUND`, `KOTER` …) are always
Danish.

### Elevations

- **Absolute elevations** (default): Revit's 0 is 0.00 m DVR90.
- **Relative elevations:** the floor (±0.00) is at Revit's 0. Enter its elevation in m DVR90 in **Floor elevation
  (±0.00)**, e.g. `10.13`; **Use the ground elevation along the house** fills in the highest terrain along the existing
  building (add the plinth yourself). At OK the terrain and the Toposolid move; the building stays. The Project Base
  Point gets the floor's elevation, so elevations from it show the height above the floor and those from the Survey
  Point DVR90. With links, Jega asks first; with Manual georeference the floor is kept in Jega only.
- **A terrain point on one of your levels:** the terrain moves up or down so that point is on the level.

Jega's own elevations on the terrain lines always show DVR90.

### Georeference

By default **Load from Address** gives the project real coordinates (ETRS89/UTM32): the project's origin on the
Project Base Point gets the address's E and N in whole metres, and true north follows the coordinates. This changes
the project's shared coordinates, so links placed by shared coordinates move with it.

- **The project has shared coordinates already** (a template's, a surveyor's): Jega asks first and says how far from
  the Project Base Point the project's coordinates place the address. If they fit the address, choose **Keep the
  project's coordinates** (the site is placed by them; nothing changes in the project) or **Give the project the
  address's coordinates (ETRS89/UTM32)** (the site is placed on the Project Base Point; the project's coordinates are
  overwritten, and links placed by them move with them; Jega keeps the project's previous coordinates, so they can be
  put back when you turn Manual georeference on). With a site in place already (turning Manual georeference off), each
  choice says how many metres and degrees the site moves. If they don't fit, the choices are **Manual georeference**
  or **Give the project the address's coordinates (ETRS89/UTM32)**.
- **A new address in the same project** replaces the current one; there is one address at a time. Jega asks whether to
  **Keep the project's origin** (the new address is placed by the real coordinates, 850 m from the current one, say)
  or **Move the project's origin to the new address** (the new address is placed on the origin). Over 10 km from
  Revit's Internal Origin the project's origin can't be kept, and the other choice is **Manual georeference**.
- **Manual georeference:** Jega doesn't change the project's coordinates (e.g. System 34 or a surveyor's). The address
  is placed at the point you choose in **Placed at (in Revit)**: Project Base Point (default), Survey Point or Internal
  Origin. You turn and move the site plan into place yourself and press **Refresh**; the terrain follows. Use it only
  when the address can't follow the project's coordinates.
- **Back from Manual georeference:** turn it off in Settings. If the project's coordinates aren't the address's own,
  Jega asks as at Load from Address.

The address point is the address's official point in the address register, not a surveyed control point. The
coordinates fit the lot and the terrain, but not well enough for setting out.

### The coordinates in the project

- **The project's origin** is the Project Base Point. It gets the address point's E and N in whole metres
  (ETRS89/UTM32), e.g. E 595,129 / N 6,399,156. Everything Jega places is measured from it: the site plan, the terrain,
  the lot and the neighbouring buildings.
- **The Survey Point** is moved by Revit out to the coordinate system's 0,0, over 6,000 km away. It is not an error:
  that is how shared coordinates work in Revit. Don't move it: the project's coordinates would move with it.
- **Two crosses on the site plan:** the large cross is the drawing's 0,0, the project's origin (the address point,
  whole metres). The small cross is the address point, the address's official point in the address register, at most
  about 0.7 m from it.
- **Other models** (the engineer's, the landscape architect's, the surveyor's) in ETRS89/UTM32 are placed in the same
  place when you link them with **Auto – By Shared Coordinates**.
- **Export:** the DXF and the CSV are in mm from the project's origin, not in UTM32. The origin's E/N is in the note on
  the site plan. Pass it on with the files.
- **North:** Revit's True North (Jega's "true north") follows the grid of ETRS89/UTM32 (map north), not the geographic
  North Pole. That applies to every model georeferenced in ETRS89/UTM32, and it is what makes them fit together. The
  difference (the meridian convergence) is 0° at 9° east and grows eastwards: about 1.3° in Skagen, about 3° in
  Copenhagen and about 5° on Bornholm
  ([Klimadatastyrelsen](https://cdn1.gopublic.dk/klimadatastyrelsen/media/2917583/001-etrs89-utm.pdf),
  [meridian convergence](https://en.wikipedia.org/wiki/Transverse_Mercator_projection#Convergence)). Sun studies in
  Revit use True North: 3° is about 12 minutes of the sun. Jega doesn't set the project's location for them
  (Manage > Location). You choose it yourself.
- **Distances:** ETRS89/UTM32 is a map projection. Distances on the map differ from distances on the ground, up to
  about 40 cm per km in zone 32
  ([Klimadatastyrelsen](https://cdn1.gopublic.dk/klimadatastyrelsen/media/2917583/001-etrs89-utm.pdf)). On Bornholm,
  which Jega also computes in UTM32, it is over 1 m per km
  ([scale factor](https://en.wikipedia.org/wiki/Transverse_Mercator_projection#Point_scale)). The boundary lengths are
  the map's. For setting out, use a surveyor's measurements.

## Data and credit

Lots from Matriklen (Geodatastyrelsen), buildings, roads and water from GeoDanmark, the terrain from Danmarks
Højdemodel, all via Datafordeleren; addresses from Danmarks Adresseregister via Adressevælger, and the street names
and house numbers round the lot from Danmarks Adresseregister via Datafordeleren's GraphQL. Sources, terms and
licences (CC BY 4.0 among them): [DATA.md](DATA.md). Jega writes the required credit on the site plan, in the
messages after Load from Address, Context Buildings (when they are fetched) and Export, and in About. Keep it when you
use the drawing further:

> Contains data from Klimadatastyrelsen (Danmarks Højdemodel, GeoDanmark, Danmarks Adresseregister) and
> Geodatastyrelsen (Matriklen) via Datafordeleren and Adressevælger.

**Everything Jega draws is indicative**, elevations to about ±0.1 m; the boundaries are the cadastral map's. Check
against a survey and official sources before you design to, apply for a permit with, or build from anything. Jega
comes without warranty.

## Known limitations of the beta

- Revit 2025 is tried; Revit 2026 isn't. Danish addresses only.
- Revit 2024 and earlier can't load Jega (it is built for .NET 8, which Revit 2025 and 2026 use).
- Not signed yet, so Windows and Revit ask at install and first start.
- A single hole in the height model in the terrain grid (the lot and 5 m around it) stops the fetch. A lot over about
  2 km across can't be fetched. Over about 160 m the terrain is fetched in several parts.
- The elevation cross is made from Revit's "Metric Generic Annotation.rft" template. Without it, the elevations come
  without crosses. Jega says so.
- The services give at most a certain number of features per call. With a very large **Margin around the lot**,
  neighbouring lots or buildings can therefore be missing from the site plan; the message says what is missing, not
  why. Lower the margin and load again.
- Manhole covers and gullies are only those seen from the air: in roads and squares, rarely in gardens; the kind of
  cover isn't known.
- Trees are only the prominent ones seen from the air, without species, height or crown size; a garden full of small
  trees looks empty.
- The terrain lines' labels follow the view's direction. After Work Direction, the next Refresh straightens them.
- A mirrored site plan can't be followed. Draw Lot Boundary doesn't follow Refresh: draw it again when the site plan
  has moved.
- The terrain lines' labels are made again when a terrain line in the view changes, and by Refresh and True / Project
  North. A label you moved, deleted or hid comes back. For fewer, use the style "Jega Line (elevations only)", which
  shows only the elevations at the corners.
- One site plan per project. Shared models are still in development; they have been tried with two users. Let one user
  load the address and set up the terrain, and synchronize before Refresh (see [For BIM managers](#for-bim-managers)).
- Address search uses Adressevælger's shared token, as Klimadatastyrelsen recommends until its user management is
  ready. Should the service later need a token of your own, it goes in Settings > Data access > Advanced. If
  Adressevælger doesn't answer, Jega searches via Datafordeleren with your key (see
  [Load from Address](#2-load-from-address)).
- Datafordeleren is changing its services. If fetching suddenly stops working, look for a new version.

## Privacy

On your PC: `%APPDATA%\Jega\settings.json` (your key, encrypted for your Windows user, and any Adressevælger token of
your own, unencrypted), `%APPDATA%\Jega\jega.log` (at most about 1 MB), `%LOCALAPPDATA%\Jega\last-folder.txt`, and
the site plan briefly in `%TEMP%\Jega\` (kept only if the import fails, so you can import it yourself). In the project
file: the settings, the address and its coordinates, the terrain, the lot, the surroundings and the neighbouring
buildings; an unsaved project's Export files are named after the address.

Sent: what you type in the address field, as you type, to Adressevælger (if it doesn't answer, to Danmarks
Adresseregister on Datafordeleren with your key instead); an area round the lot and your key to
Datafordeleren; an area round the lot and your key to Danmarks Adresseregister on Datafordeleren
(`graphql.datafordeler.dk`); Jega's name and version (`User-Agent: Jega/0.9.0-beta.1`) to all four services, so they
can tell the call comes from Jega.
All over HTTPS, the key and token in the address of each call, so an office proxy that opens HTTPS can see them. Jega
sends only to those four services, and no usage data.

## Report a bug

Under [Issues](https://github.com/AskandarS/Jega/issues).

**GitHub issues are public.** Never write a client's address or show one in a screenshot; hide addresses,
coordinates (E/N) and file names. Show the bug with a public address if you can, e.g. Vejle Rådhus, Skolegade 1, 7100
Vejle, or Skagens Museum, Brøndumsvej 4, 9990 Skagen.

If the message after **Load from Address** says "Left out of the site plan because drawing the layer failed: …", that is
a bug in Jega, not in the data. Please report it with the layer's name.

Please include Jega's version (**About**, at the bottom), Revit's version, what you did and what you saw, and attach
`jega.log` from `%APPDATA%\Jega` (when Jega stops unexpectedly, its message has an **Open the folder** button). Look
through the log first: it can hold view names, paths and error texts. Never send your key or `settings.json`.

New versions come under [Releases](https://github.com/AskandarS/Jega/releases): **Watch > Custom > Releases** on GitHub.

## For BIM managers

- Jega adds the line styles "Jega Grid", "Jega Edge", "Jega Line" and "Jega Line (elevations only)", the text type
  "Jega 3mm" (the name follows **Label text size**: "Jega 4mm" and so on), the family "Jega Cross", the Toposolid type "Jega Terrain", the filled region type "Jega Lot Boundary",
  the level "Terrain" (or "Jega Terrain") with its plan, and the site plan's CAD import.
- **The elements** (the Toposolid, the "Terrain" level, the lot boundary, the terrain frame, the neighbouring buildings,
  the site plan and the terrain lines' labels) and the text type Jega finds by a mark, so you may rename them.
- **The types, the line styles and the family** Jega finds by name. Rename "Jega Grid", "Jega Edge", "Jega Terrain",
  "Jega Lot Boundary" or "Jega Cross", and Jega makes a new one with the old name the next time it needs it. Rename the
  two "Jega Line" styles, and Jega no longer follows the lines. Change colour and weight as you like, not the name.
- A copy of Jega's Toposolid, level, lot boundary, terrain frame or neighbouring buildings is yours: Jega never moves or
  deletes it. A copy of the site plan can disappear, though, when Jega draws the site plan again (Load from Address,
  Layers, language, and Refresh after Work Direction); import it again where you need it.
- Its data sits on hidden DataStorage elements, readable by anyone, written only by Jega: the settings, the address,
  the terrain (as text, up to about 30 MB at the largest grid of 1,000 × 1,000 points, so the model grows accordingly),
  the lot, the surroundings and the neighbouring buildings. Never your key.
- To take Jega out of a model, delete the site plan, the Toposolid, the "Terrain" level, the lot boundary, the terrain
  frame and the neighbouring buildings; the types ("Jega Terrain" and so on) then go with **Manage > Purge Unused**
  like any other. The data on the DataStorage elements stays: Purge Unused doesn't remove it. It does no harm, but the
  terrain can take up to about 30 MB. A button to remove it comes after the beta.
- Load from Address changes the shared coordinates and true north; relative elevations give the Project Base Point the
  floor's elevation (asked first with links). With Manual georeference, Jega leaves the coordinates alone.
- The neighbouring buildings are DirectShapes in Generic Models with a comment ("Jega context: …", "Jega
  nabobygning: …" in Danish). They appear in schedules and IFC exports: filter them by **Comments**, or hide them with
  Context Buildings before the export.
- An element made again keeps the old one's workset and phases. The exception in the beta: turn **Context Buildings**
  off and on again, and they are made anew in the active workset and the view's phase. Refresh keeps the sorting. To
  hide them without losing it, turn their workset off in the view.
- Shared models (still in development): synchronize and choose Reload Latest before you run Refresh. Synchronize after
  Refresh too: until you do, the others can't run Refresh. If Refresh says
  "The site plan isn't in the model", choose **Cancel**, synchronize and try again. If it happens again, attach
  jega.log to the bug report.
- Network: only `adressevaelger.dk`, `wfs.datafordeler.dk`, `wcs.datafordeler.dk` and `graphql.datafordeler.dk`, over
  HTTPS. A firewall or proxy must let them through. Jega uses Windows' proxy settings and signs in to the proxy with
  the user's Windows login. If the proxy refuses, the message says it is the office network, not the key.

## Developers

[CONTRIBUTING.md](CONTRIBUTING.md) (build, test, contribute), [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md),
[docs/GLOSSARY.da.md](docs/GLOSSARY.da.md) (the Danish words), [CHANGELOG.md](CHANGELOG.md).

## License

Jega is free software under the **GNU General Public License, version 3** (see [LICENSE](LICENSE)). It comes with **no
warranty**. [NOTICE](NOTICE) also permits running it with Revit's API.

Autodesk and Revit are trademarks of Autodesk, Inc. Jega is not affiliated with or endorsed by Autodesk.

Made by Shamer Askandar.

Developed with AI assistance.
