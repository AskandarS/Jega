# Ordliste: Jega på dansk

Den danske udgave af Jegas brugerflade (`Strings.da.resx` i Jega.Revit og Jega.Core) holder sig til denne liste. Står
et ord ikke her, er det almindeligt dansk. Ret listen først, når et ord skal ændres, og så teksterne efter den.

Dansk er Jegas standardsprog: et projekt, der ikke har valgt sprog, taler dansk. Engelsk vælges pr. projekt i
Indstillinger.

**Grundvalget:** Revit findes ikke på dansk, så danske tegnestuer kører engelsk Revit. Derfor gælder:

- **Navne på ting, man klikker på eller vælger i Revit** (Survey Point, Toposolid, Edit Type, Properties, Detail Line,
  Generic Models, Insert > Import CAD, Rotate Project North …) beholdes på engelsk, så brugeren kan finde dem.
- **Fagbegreber i den løbende tekst** skrives på almindeligt dansk fagsprog (kote, punktkote, niveau, geografisk
  nord, delte koordinater).

**Stilen i brugerfladen:** eksplicit og formel. Sig tingen igen i stedet for "de/dem/den", hvor det kan læses på to
måder. "Adressen" er det hele (situationsplan, terræn, naboer): "Adressen er placeret efter de virkelige koordinater
(ETRS89/UTM32)". "Grunden" er kun grunden, "situationsplanen" kun tegningen. "Placeret/placeres" om adressens plads
efter E/N; "ligger" om koter. Projektets nulpunkt skrives "(adressepunktet, hele meter)".

## 1. Fagtermer

| Engelsk | Dansk | Begrundelse |
|---|---|---|
| elevation | **kote** | En højde. "Terrain elevation" = terrænkote, "floor elevation" = gulvkote. |
| Elevations (fanen) | Koter | Fanen hedder "Koter (±0,00)". |
| Terrain Elevations (panelet) | Terrænkoter | |
| level | **niveau** | Altid og kun en Revit Level; forveksles ikke med kote. |
| the Terrain level | niveauet "Terrain" | "Terrain" er et navn fra `Config` og oversættes ikke. |
| Revit's 0 | Revits 0 | Revits interne nul, ikke ±0,00. |
| ±0.00 | ±0,00 | Dansk decimalkomma. |
| absolute / relative elevations | absolutte / relative koter | |
| lot | **grund** | Det, man bygger på. |
| lot boundary | **skel** | "Along the lot boundary" = langs skellet. |
| parcel | **jordstykke** | Matriklens ord for den enkelte flade. |
| site | grund | Panelet "Site" = Grund. |
| site plan | **situationsplan** | |
| terrain | **terræn** | |
| terrain grid | terrænnet | |
| terrain frame | terrænramme | |
| terrain line | terrænlinje | |
| Toposolid | **Toposolid** | Revits elementtype; bøjes ikke. |
| offset | **Offset** (feltnavn), "offset" i løbende tekst | Som i Revit. |
| spot elevation | **punktkote** | På situationsplanen (DXF) bare "koter". |
| ridge / eaves | **OK tag** / **tagfod** | Med "ca." på tegningen; signaturen siger "Tagkoter". |
| manhole cover | **brønddæksel** | GeoDanmarks punkt, set fra luften; typen kendes ikke. Laget `BROENDE`. |
| gully (road gully) | **nedløbsrist**, rist | Laget `RISTE`. |
| cover level | **dækselkote** | Koten på dækslet (m DVR90). |
| house number | **husnummer** | Danmarks Adresseregisters; i den bygning, adressen hører til. Laget `HUSNUMRE`. |
| street name | **vejnavn** | Laget `VEJNAVNE`. |
| name line | **navnelinje** | Adresseregistrets linje, hvor vejnavnet skal stå. |
| tree / tree group / wood | **træ** / **trægruppe** / **skov** | GeoDanmarks, set fra luften; kun markante træer. Laget `TRAEER`. |
| hedge | **hegn** | Også stakit og mur i GeoDanmarks betydning. Laget `HEGN`. |
| vegetation (legend group) | **BEPLANTNING** | Gruppen i signaturforklaringen. |
| Survey Point / Project Base Point / Internal Origin | uoversat | Revits navne. |
| shared coordinates | **delte koordinater** | |
| real-world coordinates, real coordinates | **virkelige koordinater** | Med "(ETRS89/UTM32)", hvor teksten står alene og systemet ikke er nævnt. Aldrig "rigtige koordinater". |
| georeference (fanen), Manual georeference | **Georeference**, **Manuel georeference** | Fanen i Indstillinger og fluebenet på den. |
| the project's origin | **projektets nulpunkt**, nulpunktet | Adressepunktet i hele meter, på Project Base Point. Brugerfladen siger "(adressepunktet, hele meter)". Laget `NULPUNKT`. |
| true north / project north | **geografisk nord** / projektnord | Visningens Orientation "True North" / "Project North" står på engelsk. |
| Work Direction | **Arbejdsretning** | knap i panelet Grund; Revits Rotate Project North |
| True / Project North | **True / Project North** | knap i panelet Grund; Revits egne ord |
| Layers (tab and button) | **Lag** | fanen i Indstillinger og knappen i panelet Grund |
| Rotate Project North, Orientation, View Template | (står på engelsk) | Revits egne navne |
| setback | **afstand til skel** | Byggeloven/BR18's ord. |
| address point | **adressepunkt** | Adresseregistrets eget ord er *adgangspunkt*; Jega siger adressepunkt, fordi det er adressens punkt. |
| neighbouring buildings / neighbours | **nabobygninger** / naboer | Comments på nabobygningerne: "Jega nabobygning". |
| house | **bygningen** | Både den eksisterende på grunden og den, der tegnes; ikke alle er huse. "Along the house" = langs bygningen. |
| floor plan / plan view / view | plantegning / planvisning / visning | |
| fall / slope | fald / hældning | |
| HIGH / LOW (på planen) | HØJ / LAV | |
| elevation cross | kotekryds | |
| label (på planen) | påskrift | |
| line style | linjestil | |
| masses (Generic Models) | volumen (Generic Models) | Nabobygningerne; aldrig "klodser". |
| indicative | vejledende | |
| survey / surveyor | opmåling / landinspektør | |
| service | tjeneste | |
| key (API key) / token | nøgle, API-nøgle / token | |
| ribbon | båndet | |

### Beskederne efter hver kommando

| Engelsk | Dansk |
|---|---|
| receipt | meddelelse |
| Nothing was loaded | Intet blev hentet (fra nettet) / Intet blev indlæst (fra en fil) |
| Nothing was changed / refreshed | Intet blev ændret / opdateret |
| Revit stopped X and showed its reason in the warning just before this one | Revit stoppede X og viste årsagen i advarslen lige før denne |
| … then press Refresh | … og tryk så på Opdater |
| X again (et trin) | Kør X igen |
| please report it | så meld det gerne |
| Jega stopped unexpectedly (a failure no command foresaw) | Jega stoppede uventet |
| Open the folder | Åbn mappen |
| the key was refused / Adressevælger refused the token | nøglen blev afvist / Adressevælger afviste tokenet |
| (recommended) | (anbefalet) |
| Cancel / Close / OK | Annuller / Luk / OK |

### Hente, indlæse, Undo

| Engelsk | Dansk |
|---|---|
| fetch, load (from address) | **hente** (fra nettet) |
| load (a file, a family, a type) | **indlæse** |
| store / keep in the model, save | gemme |
| One Undo. | Fortrydes i ét trin. |
| Undo (Revits knap) | Undo |
| Undo-navne | "Jega: " + knappens danske navn, fx "Jega: Hent fra adresse" |

## 2. Knapper og paneler

Store knapper har et linjeskift, som i den engelske.

| Engelsk | Dansk |
|---|---|
| Panel: Site / Model / Terrain Elevations / Jega | **Grund** / **Model** / **Terrænkoter** / **Jega** |
| Load from Address | **Hent fra adresse** |
| Load Terrain CSV File | **Indlæs terræn fra CSV** |
| Refresh | **Opdater** |
| Layers | **Lag** |
| Work Direction | **Arbejdsretning** |
| True / Project North | **True / Project North** |
| Generate Terrain | **Opret terræn** |
| Draw Lot Boundary | **Tegn grund** (den tegner grunden; teksterne om, hvad den følger, siger "skellet") |
| Context Buildings | **Nabobygninger** |
| Terrain Line | **Terrænlinje** |
| Settings | **Indstillinger** |
| Export | **Eksportér** |
| About | **Om Jega** |

## 3. Indstillinger og dialoger

Core's `Options_*` begynder med feltets danske navn fra denne tabel: omdøb et felt, omdøb begge.

| Engelsk | Dansk |
|---|---|
| Georeference / Elevations (±0.00) / Site plan & terrain / Layers / Data access | **Georeference** / **Koter (±0,00)** / **Situationsplan og terræn** / **Lag** / **Dataadgang** |
| Advanced | Avanceret |
| Manual georeference / Placed at (in Revit) | Manuel georeference / Placeres ved (i Revit) |
| Absolute / Relative elevations | Absolutte koter / Relative koter |
| Floor elevation (±0.00) | Gulvkote (±0,00) |
| Use the ground elevation along the house | Brug terrænkoten langs bygningen |
| Terrain point on a level | Terrænpunkt på et af dine niveauer (feltet); "Terrænpunkt på et niveau" i meddelelserne |
| Terrain lines / Offset / Elevation cross size / Label text size | Terrænlinjer / Offset / Størrelse på kotekryds / Tekststørrelse |
| Margin around the lot / Setback from boundary | Margen om grunden / Afstand til skel |
| Thickness / Terrain point for the Terrain level | Tykkelse / Terrænpunkt for niveauet Terrain |
| Highest along the house | Højeste langs bygningen |
| Point spacing inside the lot / along the lot boundary | Punktafstand inde på grunden / langs skel |
| Include the buildings on the lot | Medtag bygningerne på grunden |
| Datafordeler API key / Adressevælger token | Datafordeler-API-nøgle / Adressevælger-token |
| The project has shared coordinates already | Projektet har allerede delte koordinater |
| Keep the project's coordinates / Give the project the address's coordinates (ETRS89/UTM32) | Behold projektets koordinater / Giv projektet adressens koordinater (ETRS89/UTM32) |
| Keep the project's origin / Move the project's origin to the new address | Behold projektets nulpunkt / Flyt projektets nulpunkt til den nye adresse |
| off / on (i noter) | slået fra / slået til |

## 4. Uoversat

**Revits egne navne:** Survey Point, Project Base Point, Internal Origin, Toposolid, Generic Models, Filled Region,
Properties, Edit Type, Edit Sketch, Edit Boundary, Detail Line, Insert > Import CAD, Origin to Origin, Comments, Text
Size, Size, Undo, Rotate Project North, Align Selected Line or Plane, Orientation, True North, Project North, Reload
Latest; filnavne og stier; Shift (i Shift+klik); HTTP, GeoTIFF, JSON, DXF, CSV, API.

**Navne i modellen fra `Config`** (`Jega.Core\Config.cs`), som teksterne nævner i anførselstegn, så brugeren finder dem:
"Jega Line", "Jega Line (elevations only)", "Jega Cross", "Jega Terrain", "Jega Lot Boundary", niveauet "Terrain",
"Jega Grid", "Jega Edge", "Jega 3mm". Sprogene "Dansk" og "English" står hver på sit eget sprog.

**Egennavne og standarder:** Datafordeleren, Adressevælger, Danmarks Adresseregister, Matriklen, GeoDanmark, Danmarks
Højdemodel, Klimadatastyrelsen, Geodatastyrelsen, DVR90, ETRS89/UTM32, EPSG:25832, System 34, GPL-3.0, Jega.

## 5. Tal

- Pladsholdere formateres af Jega efter sproget (komma på dansk). Tal skrevet direkte i teksten får dansk komma:
  "±0,1 m", "0,5 til 10 mm".
- "to" i intervaller = "til": "10 til 5000 mm". Tusindtal uden punktum, som i den engelske (5000, 10000).
- Undtagelse: `Grid_NotNumbers` siger, at CSV-filen bruger '.' som decimaltegn. Det er filformatet.
