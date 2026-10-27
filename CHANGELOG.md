# Changelog

All notable changes to Jega are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the versions follow
[Semantic Versioning](https://semver.org/).

## [Unreleased]

## [0.9.0-beta.1] - 2026-10-27

The first public beta: an open beta for Revit 2025 and Danish addresses. How to start is in the README under
[Kom i gang](README.md#kom-i-gang) ([Quick start](README.en.md#quick-start) in English), and what Jega can't do yet
under [Kendte begrænsninger i betaen](README.md#kendte-begrænsninger-i-betaen)
([Known limitations](README.en.md#known-limitations-of-the-beta)).

### Added

- **Load from Address:** type a Danish address, pick a suggestion, and the site plan and the terrain are fetched from
  Datafordeleren with your own key and loaded into the model. The neighbouring buildings usually come with them. One
  Undo. If the neighbouring lots, the buildings, the roads, the water, the trees and hedges, the manhole covers and
  gullies, the street names and house numbers or the height model's surface can't be fetched, the site plan comes
  without them and the message says which; only the lot and the terrain are needed.
- The address field falls back to Danmarks Adresseregister on Datafordeleren (with the key) when Adressevælger refuses
  its token or doesn't answer; the search is then simpler (street, number and town).
- **Georeference by default:** the project gets real coordinates (ETRS89/UTM32). Its origin sits on the Project Base
  Point (the address point, whole metres), and true north follows the coordinates. A project with shared coordinates
  of its own is asked first. A new address replaces the current one; the project's origin is kept or moved to the new
  address.
- **Manual georeference** (Settings > Georeference): leaves the project's coordinates alone (e.g. System 34, or a
  surveyor's); the site is placed at the Project Base Point (the default), the Survey Point or the Internal Origin.
- **The site plan** as a DXF import: the lot and its boundary lengths, neighbouring lots and parcel numbers,
  buildings, road and path edges, water, the setback line, spot elevations, roof heights, the origin and the address
  point, north arrow, scale bar (1:100 to 1:5000), legend and notes. Each kind of thing is on its own Danish layer.
- **Manhole covers and gullies** (GeoDanmark) on the site plan with their cover level, on their own layers BROENDE and
  RISTE; the kind of cover isn't known, and the note points to LER before excavation (LER Act § 7).
- **Street names and house numbers** (Danmarks Adresseregister, via Datafordeleren's GraphQL) on the site plan: each
  number in the building its address belongs to, each street name once along its own name line; on their own layers
  HUSNUMRE and VEJNAVNE.
- **Trees and hedges** (GeoDanmark) on the site plan: the prominent trees as symbols, tree groups and woods as
  outlines, hedges as lines; layers TRAEER and HEGN.
- Texts on the site plan keep clear of the point symbols (covers, gullies, trees) as well as of each other; a layer
  whose drawing fails is left out and the receipt names it.
- **The site plan is drawn for the view it stands in:** in Project North the map turns with the model, in True North it
  lies north up; frame, texts and legend stay straight in both, the north arrow points north. The True / Project North
  button and Refresh draw it again; Export still writes north up. Settings > Site plan & terrain can turn the whole
  plan as one instead.
- **Refresh:** puts the site plan back on its coordinates and brings the terrain frame, the terrain line labels, the
  neighbouring buildings and the Toposolid up to date; offers to draw a deleted site plan again. Jega's other buttons
  refresh first when needed.
- **Work Direction:** Revit's Rotate Project North from the ribbon, to work square to a building. **True / Project
  North** switches the active plan view.
- **Generate Terrain:** a Toposolid on the lot, shaped by the terrain, on its own "Terrain" level. Jega asks before it
  makes again a Toposolid that was changed by hand.
- **Terrain Line:** elevations along any detail line you draw; with the "Jega Line" style also falls, slopes and
  HIGH / LOW. The labels follow every edit.
- **Draw Lot Boundary:** the lot as a filled region on its boundary, with the elevations along it.
- **Context Buildings:** the neighbouring buildings as masses (Generic Models), heights from Danmarks
  Højdemodel. On and off; Shift+click fetches again.
- **Layers:** choose the site plan's layers and roof heights; it is drawn again without fetching.
- **Elevations:** absolute (DVR90), relative (the floor at ±0.00, the Project Base Point gets the floor's elevation)
  or a terrain point on one of your levels.
- **Export:** the site plan DXF and the terrain CSV to a folder; asks before overwriting. **Load Terrain CSV File**
  reads such a CSV back.
- **Large lots** (over about 160 m) are fetched in parts, with a choice of terrain grid.
- **Danish and English.** Danish is the default; the language belongs to the project. The DXF layer names are always
  Danish.
- **Copies are yours:** a copy of Jega's Toposolid, level, lot boundary, terrain frame, neighbouring buildings or site
  plan is never moved or deleted by Jega.
- **Shared models:** an element made again keeps the old one's workset and phases; Terrain Line leaves a view a
  colleague has borrowed.
- **The data credit** on the site plan, in the messages and in About names Klimadatastyrelsen's datasets (Danmarks
  Højdemodel, GeoDanmark, Danmarks Adresseregister), Geodatastyrelsen's Matriklen, Datafordeleren and Adressevælger
  ([DATA.md](DATA.md)).
- Calls are tried again on server errors, no answer and 429.
- Calls follow Windows' proxy settings and sign in to the proxy with the user's Windows login; a refusing proxy gets
  its own message. Every call carries `User-Agent: Jega/<version>`.
- Your Datafordeler key is stored encrypted for your Windows user, never in the model.
- An unforeseen failure stops the command with a message and writes the details to `%APPDATA%\Jega\jega.log`.
- A per-user installer (no administrator) that puts Jega into Revit 2025, and Revit 2026 if it is there, as an
  Autodesk bundle. Revit 2026 is untried.

[Unreleased]: https://github.com/AskandarS/Jega/compare/v0.9.0-beta.1...HEAD
[0.9.0-beta.1]: https://github.com/AskandarS/Jega/releases/tag/v0.9.0-beta.1
