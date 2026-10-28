# Jega

**Fra en dansk adresse til situationsplan og terræn i Revit på et øjeblik.**

*[English version](README.en.md)*

Jega er et gratis Revit-plugin med åben kildekode til den danske byggebranche. Du skriver en adresse. Jega henter
grunden, naboerne, bygningerne, vejkanterne og terrænet fra Danmarks åbne data (Matriklen, Danmarks Højdemodel,
GeoDanmark og Danmarks Adresseregister) og lægger det ind i din model: situationsplanen som DXF i planen, terrænet
som en Toposolid på grunden, nabobygningerne som 3D-volumener med højder fra højdemodellen, og projektet
georefereret i rigtige koordinater.

Ingen server, ingen konto hos Jega, intet abonnement. Jega taler direkte med de offentlige tjenester med din egen
gratis nøgle.

> **Åben beta, version 0.9.0-beta.1.** Jega virker, men er nyt. Meld gerne fejl og idéer (se [Meld en fejl](#meld-en-fejl)).

**Sådan starter du:**

1. **Hent Jega:** kør `Jega-0.9.0-beta.1-setup.exe` fra [Releases](https://github.com/AskandarS/Jega/releases)
   med Revit lukket (se [Installation](#installation)).
2. **Lav en gratis nøgle** på [datafordeler.dk](https://datafordeler.dk): Log på Datafordeleren > Datafordeler
   Administration (første gang: Opret Bruger), så IT-systemer > Opret > API-Keys > Opret. Kopiér nøglen med det samme;
   den kan være op til 15 minutter om at virke (se [Det skal du bruge](#det-skal-du-bruge)).
3. **Kør i Revit:** fanen **Jega** > **Indstillinger** > sæt nøglen ind. Tryk **Hent fra adresse**, skriv adressen,
   og vent.

Trin for trin under [Kom i gang](#kom-i-gang).

https://github.com/user-attachments/assets/60babc87-a76d-4ff2-a366-48443cdaff46

## Hvad Jega gør

- **Situationsplanen** som en DXF, importeret i din planvisning: grunden med skelmål, nabogrunde og matrikelnumre,
  bygninger, vej- og stikanter, søer og vandløb, træer og hegn, brønddæksler og nedløbsriste med kote, vejnavne og
  husnumre, afstand til skel, koter, tagkoter, nordpil, målestok, signaturforklaring og noter. Hver slags ligger på
  sit eget lag.
  Bygningerne er GeoDanmarks tagflader, ikke facader. Teksterne er sat til tegningens målestok på A3; den står i rammen
  (MÅLESTOK 1:x).
- **Terrænet** fra Danmarks Højdemodel, gemt i modellen.
- **Virkelige koordinater:** projektet bliver georefereret (ETRS89/UTM32), og geografisk nord følger koordinaterne.
- **En Toposolid** på grunden, formet efter terrænet (**Opret terræn**).
- **Grunden** som en Filled Region præcis langs skellet, med koterne langs det (**Tegn grund**).
- **Terrænlinjer:** tegn en linje, og få koten i hvert knæk, fald, hældning og HØJ / LAV. Koterne følger med, når du
  retter linjen (**Terrænlinje**).
- **Nabobygningerne** som volumen (Generic Models) med højder fra Danmarks Højdemodel (**Nabobygninger**).
- **Eksport** af situationsplanen (DXF) og terrænet (CSV), fx til en landinspektør eller kommunen (**Eksportér**).

## Hvad Jega ikke gør

- Jega er ikke en opmåling. Alt er vejledende (se [Data og kreditering](#data-og-kreditering)).
- Jega tegner ikke den eksisterende bygning som model. Bygningen tegner du selv.
- Toposolid'en dækker kun grunden, ikke omgivelserne.
- Grunden er det jordstykke (matrikelnummer), adressepunktet ligger i. Består ejendommen af flere jordstykker, hentes
  kun det ene; de andre står som nabogrunde på situationsplanen.
- Jega henter ikke BBR-oplysninger, lokalplaner eller servitutter.
- Jega ved ikke, hvad der er under et brønddæksel (kloak, vand, varme). Ledningsoplysninger indhentes i LER før
  gravearbejde (LER-loven § 7).
- Kun danske adresser. Kun Revit på Windows. Revit LT kan ikke køre plugins.

## Det skal du bruge

- **Autodesk Revit 2025** (fuld Revit) på Windows. Har du også Revit 2026, indlæses Jega også dér. Det virker sandsynligvis,
  men er ikke officielt understøttet: meld gerne, hvordan det går.
- **Internet** til `adressevaelger.dk`, `wfs.datafordeler.dk`, `wcs.datafordeler.dk` og `graphql.datafordeler.dk`
  (HTTPS; alle med din nøgle eller Adressevælgers token).
- **En gratis bruger hos Datafordeleren og en API-nøgle.** Opret en bruger på [datafordeler.dk](https://datafordeler.dk),
  og lav en API-nøgle til din bruger. I Jega står linket **Hent en nøgle på datafordeler.dk** under
  **Indstillinger > Dataadgang**. Vejen på sitet: **Log på Datafordeleren > Datafordeler Administration**, log ind med
  e-mail eller MitID (første gang: **Opret Bruger**), så **IT-systemer > Opret > API-Keys > Opret**. Nøglen vises kun
  én gang, så kopiér den med det samme. Der kan gå op til 15 minutter, før en ny nøgle virker.

https://github.com/user-attachments/assets/06826247-5b5b-4243-8e1c-1eeed88d9d4e

Nøglen er din egen og knyttet til din bruger hos Datafordeleren. Du er selv ansvarlig for den og for at overholde
Datafordelerens vilkår; om én nøgle må deles på kontoret, afgør vilkårene, ikke Jega.

## Installation

1. Hent `Jega-0.9.0-beta.1-setup.exe` under [Releases](https://github.com/AskandarS/Jega/releases). Filen er ny og
   hentet af få endnu, så browseren advarer måske: "Jega-0.9.0-beta.1-setup.exe downloades sjældent". Vælg at beholde
   filen (i Edge: højreklik på filen > **Behold**).
2. Luk Revit.
3. Kør filen. Jega er ikke signeret endnu, så Windows siger måske "Windows beskyttede din pc". Klik på
   **Flere oplysninger** og så **Kør alligevel**.
4. Følg installeren. Den kræver ikke administratorrettigheder: Jega installeres kun for din egen Windows-bruger, i
   mappen `%APPDATA%\Autodesk\ApplicationPlugins\Jega.bundle`, hvor Revit 2025 og 2026 selv finder den.
5. Start Revit. Revit viser **Security - Unsigned Add-In** ("The publisher of this add-in could not be verified"),
   fordi Jega ikke er signeret endnu. Vælg **Always Load**; Revit spørger kun første gang.
6. Fanen **Jega** står nu i båndet.

**Ny version:** kør den nye installer. Den lægges oven på den gamle uden at afinstallere først. Er Revit åben, beder
installeren dig lukke den og trykke **Forsøg igen**.

### Afinstallation

1. Luk Revit.
2. Åbn Windows' **Indstillinger > Apps > Installerede apps**, find **Jega 0.9.0-beta.1 (Revit)**, og vælg
   **Afinstaller**.

Afinstallationen fjerner `Jega.bundle`. Din nøgle bliver, så du ikke skal indtaste den igen, hvis du installerer Jega
igen. Den ligger i mappen `%APPDATA%\Jega` sammen med loggen og kotekrydsets familie. Vil du også fjerne dem, så skriv
`%APPDATA%\Jega` i Stifinderens adresselinje, tryk Enter, og slet mappen. Slet også `%LOCALAPPDATA%\Jega` (den mappe,
du sidst indlæste en terræn-CSV fra), hvis den findes.

Det, Jega har lavet, bliver i modellerne som almindelige Revit-elementer. De åbner uden Jega og uden advarsel.
Terrænlinjernes koter følger bare ikke længere med.

## Kom i gang

### 1. Åbn et projekt

Åbn et projekt, eller opret et nyt fra din skabelon. Jegas knapper er grå, indtil et projekt er åbent.

### 2. Første gang: din nøgle

Tryk **Indstillinger** i fanen Jega. Første gang åbner vinduet på fanen **Dataadgang**. Sæt nøglen ind i
**Datafordeler-API-nøgle**, og tryk **Test**. Tre linjer med OK betyder, at kortdata, terræn og adresser svarer. Tryk
**OK**.

Glemmer du det, spørger **Hent fra adresse** efter nøglen og fortsætter, når den er gemt.

### 3. Hent fra adresse

Åbn en plantegning (Floor Plan eller Site Plan), og tryk **Hent fra adresse**. Skriv adressen, fx Vejle Rådhus,
`Skolegade 1, 7100 Vejle`. Vælg forslaget, og tryk **Hent** (eller Enter). Jega henter i baggrunden; **Annuller**
stopper, uden at noget lægges ind.

Situationsplanen og terrænet kommer ind. Nabobygningerne hentes som regel med og gemmes i modellen; du viser dem med
**Nabobygninger**.

Projektet får virkelige koordinater (ETRS89/UTM32). Projektets nulpunkt placeres på Project Base Point
(adressepunktet, hele meter), så koordinaterne er runde tal. Geografisk nord følger koordinaterne. Det hele fortrydes i ét trin.

- Er grunden over ca. 160 m, spørger Jega først, hvor fint terrænnettet skal være.
- Har projektet allerede koordinater, spørger Jega først (se [Georeference](#georeference)).
- Svarer Adressevælger ikke, eller afviser den tokenet, søger Jega i stedet i Danmarks Adresseregister på
  Datafordeleren med din nøgle og siger det under feltet, fx "Adressevælger kunne ikke bruges (400, ukendt token);
  søger nu via Datafordeleren." Den søgning tåler ikke stavefejl, så skriv vej, nummer og by.
- Kan naboskel, bygninger, veje, vand, træer og hegn, brønddæksler og riste, vejnavne og husnumre eller
  højdemodellens overflade (tagkoter og nabobygninger) ikke hentes, kommer situationsplanen uden dem, og meddelelsen
  siger det. Kun grunden og terrænet er nødvendige. Hent igen senere.

### 4. Få huset til at passe

Situationsplanen er placeret efter de virkelige koordinater og skal ikke drejes. Tegn huset på grunden. Er huset tegnet før, så flyt
det hen på grunden med Revits Move og Rotate.

Vil du arbejde lige efter huset, så tryk **Arbejdsretning**. Det er Revits Rotate Project North: vælg
**Align Selected Line or Plane**, og klik på en af husets vægge, linjer eller flader. Hele modellen drejer samlet; koordinaterne og
geografisk nord bliver. Når drejningen er færdig, kører Jega selv **Opdater** (sin egen Undo-post).

Situationsplanen tegnes for den visning, den står i: i Project North ligger kortet (grund, bygninger, veje og det
øvrige indhold) drejet som modellen, i True North nord-op. Rammen, titlen, noterne og signaturen står lige i begge, og
nordpilen peger mod nord med N'et opret. Ligger kortet drejet, siger en note på tegningen hvor mange grader. Jegas
**True / Project North** tegner den igen med det samme. Efter **Arbejdsretning** tegner Jegas **Opdater** den igen på
samme sted, og meddelelsen siger "Situationsplanen er tegnet igen efter visningens nord: ramme og tekster lige, stregerne
drejet med modellen."; terrænrammen, koterne og Toposolid'en følger som før. Skifter du visningens orientering i
Revits egenskaber, retter næste Jega-knap den op (Opdater kører først). Er planen importeret i alle visninger (**Kun i
den visning** slået fra), kan den kun stå lige i én slags visning ad gangen; den tegnes for den visning, du står i, når
den tegnes igen. **Eksportér** skriver altid nord-op. Vil du hellere have, at hele planen drejer som én, papiret med,
så slå **Papiret står lige i visningen** fra under Indstillinger > **Situationsplan og terræn**.

Arbejdsretning virker kun i en planvisning, der står i Project North. Skift med **True / Project North**.

Flytter eller drejer du situationsplanen i hånden, placerer **Opdater** den igen efter de virkelige koordinater.

### 5. Opdater

Tryk **Opdater**. Alt følger med: terrænrammen, terrænlinjernes koter, nabobygningerne (hvis de vises) og Toposolid'en
(hvis den er oprettet). Det fortrydes i ét trin.

Jegas andre knapper kører selv Opdater først, hvis situationsplanen er flyttet eller drejet siden sidst. Er
situationsplanen slettet, tilbyder Opdater at tegne den igen ud fra det, modellen har gemt.

### 6. Opret terræn

Tryk **Opret terræn**. Jega opretter en Toposolid på grunden, afgrænset af skellet og formet efter terrænet.
Toposolid'en placeres på sit eget niveau, "Terrain", som standard i terrænets laveste punkt. Et andet punkt vælger du i
Indstillinger > **Situationsplan og terræn** > **Terrænpunkt for niveauet Terrain**.

Toposolid'en får sin egen type, "Jega Terrain", kopieret fra modellens Toposolid-type. Har skabelonen ingen
Toposolid-type, siger Jega det; hent en med Manage > Transfer Project Standards.

Har du ændret Toposolid'en i hånden, spørger Jega, før den laves om. En kopi, du laver af den, er din: Jega rører den
ikke.

### 7. Terrænlinje

Tryk **Terrænlinje** i en planvisning, og tegn en linje langs huset. Revits Detail Line starter i linjestilen
"Jega Line (elevations only)": hvert knæk og hver ende får terrænkoten og et kryds.

Sæt linjen til stilen "Jega Line" i Properties for også at få fald og hældning på hvert stykke og HØJ / LAV. Flyt,
ret eller slet linjen, og koterne følger med.

### 8. Efter behov

- **Nabobygninger:** viser og skjuler nabobygningerne. **Shift+klik** henter dem igen.
- **Lag:** vælg, hvilke lag og tagkoter situationsplanen tegnes med. Ved OK tegnes den igen; intet hentes.
- **Relative koter:** Indstillinger > **Koter (±0,00)** > **Relative koter**, og skriv gulvets kote (se [Koter](#koter)).
- **Offset:** et tillæg i hele mm, der lægges til alle koter fra Terrænlinje og Tegn grund (kote = terræn + offset), fx
  en belægnings tykkelse (150) eller en udgravning (-900). Sæt det i Indstillinger > **Koter (±0,00)** > **Offset**,
  eller i skelkoternes vindue efter **Tegn grund**; det er samme værdi, og koterne på planen følger med.
- **Eksportér:** skriver situationsplanens DXF og terrænets CSV til en mappe, du vælger, som `<navn>-siteplan.dxf` og
  `<navn>-terrain.csv`; navnet er projektfilens. DXF'en er altid nord opad, som landinspektørens og kommunens planer.

## Båndet

Fanen **Jega** har fire paneler. Skifter du projektet til engelsk (se [Sprog](#sprog)), hedder knapperne som i den
engelske kolonne.

| Knap | Engelsk | Panel |
|---|---|---|
| Hent fra adresse | Load from Address | Grund |
| Indlæs terræn fra CSV (pilen under Hent fra adresse) | Load Terrain CSV File | Grund |
| Opdater | Refresh | Grund |
| Lag | Layers | Grund |
| Arbejdsretning | Work Direction | Grund |
| True / Project North | True / Project North | Grund |
| Opret terræn | Generate Terrain | Model |
| Tegn grund | Draw Lot Boundary | Model |
| Nabobygninger | Context Buildings | Model |
| Terrænlinje | Terrain Line | Terrænkoter |
| Indstillinger | Settings | Jega |
| Eksportér | Export | Jega |
| Om Jega | About | Jega |

Knapperne er grå uden et åbent projekt og i familieeditoren. **Arbejdsretning** og **True / Project North** er også grå
uden for planvisninger. **Om Jega** virker altid.

De knapper, der ikke er nævnt ovenfor:

- **Indlæs terræn fra CSV:** indlæser et terræn i Jegas eget format, sådan som **Eksportér** skriver det. Én linje pr.
  punkt, `X,Y,Z` i mm, ingen overskrift, punktum som decimaltegn. X og Y regnes fra nulpunktet (adressepunktet, hele
  meter), Z er DVR90. Nettet skal være regelmæssigt og uden huller. Filen kontrolleres, før noget gemmes.
- **Tegn grund:** tegner grunden som en Filled Region ("Jega Lot Boundary") præcis langs skellet og viser koterne langs
  det med højeste og laveste punkt. Opdater flytter den ikke: flytter situationsplanen sig, så tegn den igen.
- **True / Project North:** skifter den aktive planvisning mellem True North og Project North, som i Properties. En
  view template, der styrer orienteringen, bestemmer.
- **Om Jega:** versionen (nederst), licensen og hvor data kommer fra.

## Indstillinger

**Indstillinger** har fem faner. Det meste gemmes i projektet, så alle, der arbejder i det, ser det samme. Nøglen
gemmes kun på din pc. Standardværdierne står i vinduet.

- **Georeference:** hvordan grunden placeres. Fra start georefererer Jega projektet. **Manuel georeference** er
  nødudgangen (se [Georeference](#georeference)).
- **Koter (±0,00):** hvad Revits 0 er (se [Koter](#koter)), og terrænlinjernes **Offset** (se
  [Efter behov](#8-efter-behov)), **Størrelse på kotekryds** og **Tekststørrelse**.
- **Situationsplan og terræn:** om og hvor situationsplanen importeres, om papiret står lige (**Papiret står lige i
  visningen (kun kortet drejer)**, slået til som standard; slået fra drejer hele planen som én), **Margen om
  grunden**, **Afstand til skel**,
  Toposolid'ens **Tykkelse**, hvor niveauet "Terrain" lægges, og om nabobygningerne også tæller bygningerne på selve
  grunden (**Medtag bygningerne på grunden**). Under **Avanceret**: Toposolid'ens **Punktafstand inde på grunden** og
  **Punktafstand langs skel**.
- **Lag:** hvilke lag og tagkoter situationsplanen tegnes med (samme som knappen **Lag**), fx **Træer, trægrupper og
  skov (GeoDanmark; set fra luften, ingen art)**, **Hegn (GeoDanmark)**, **Brønddæksler med dækselkote (GeoDanmark;
  typen kendes ikke)**, **Nedløbsriste med kote (GeoDanmark)**, **Husnumre i bygningerne (Danmarks Adresseregister)**
  og **Vejnavne (Danmarks Adresseregister)**.
- **Dataadgang:** din Datafordeler-nøgle og **Test**. Under **Avanceret** et eget Adressevælger-token (normalt tomt).

### Sprog

Sproget vælger du øverst til højre: **Dansk** (standard) eller **English**. Det skifter, når du trykker OK, og gælder
båndet, vinduerne, meddelelserne og tegningens titel, noter og signaturforklaring. Lagnavnene (`GRUND`, `KOTER` …) er
altid danske.

Sproget gemmes i projektet. Et nyt projekt starter på dansk. Er der allerede en situationsplan, tilbyder Jega at
tegne den igen på det nye sprog.

### Koter

- **Absolutte koter** (standard): Revits 0 er 0,00 m DVR90. Terrænet ligger i sin virkelige højde.
- **Relative koter:** gulvet (±0,00) ligger i Revits 0. Skriv gulvets kote i m DVR90 i **Gulvkote (±0,00)**, fx
  `10,13`. Knappen **Brug terrænkoten langs bygningen** udfylder feltet med det højeste terræn langs den eksisterende
  bygning; læg selv soklen til. Ved OK flytter terrænet og Toposolid'en sig; bygningen bliver.
  Project Base Point får gulvkoten: koter målt fra Project Base Point viser højden over gulvet, koter målt fra Survey
  Point viser DVR90. Har projektet links, spørger Jega først. Med Manuel georeference gemmes gulvkoten kun i Jega.
- **Terrænpunkt på et af dine niveauer:** vælg et punkt i terrænet (fx det laveste) og et af dine niveauer. Terrænet
  flyttes op eller ned, så punktet ligger på niveauet. Tallet regnes ud ved OK; Indstillinger siger til, hvis det ikke
  passer længere.

Jegas egne koter på terrænlinjerne viser altid DVR90.

### Georeference

Fra start giver **Hent fra adresse** projektet virkelige koordinater (ETRS89/UTM32): projektets nulpunkt på Project
Base Point får adressens E og N i hele meter, og geografisk nord følger koordinaterne. Det ændrer projektets delte
koordinater, så links, der er placeret efter delte koordinater, flyttes med.

- **Projektet har allerede delte koordinater** (fx fra en skabelon eller en landinspektør): Jega spørger først og
  siger, hvor langt fra Project Base Point projektets koordinater placerer adressen. Passer de med adressen, vælger du
  **Behold projektets koordinater** (grunden placeres efter dem; intet ændres i projektet) eller **Giv projektet
  adressens koordinater (ETRS89/UTM32)** (grunden placeres på Project Base Point; projektets koordinater overskrives,
  og links placeret efter dem flytter med; Jega gemmer projektets tidligere koordinater, så de kan sættes tilbage, når
  du slår Manuel georeference til). Står grunden allerede (du slår Manuel georeference fra), siger hvert valg, hvor
  mange meter og grader grunden flytter sig. Passer de ikke, er valgene **Manuel georeference** eller **Giv projektet
  adressens koordinater (ETRS89/UTM32)**.
- **En ny adresse i samme projekt** erstatter den nuværende; der er én adresse ad gangen. Jega spørger, om du vil
  **Behold projektets nulpunkt** (den nye adresse placeres efter de virkelige koordinater, fx 850 m fra den
  nuværende) eller **Flyt projektets nulpunkt til den nye adresse** (den nye adresse placeres på nulpunktet). Er den
  nye adresse over 10 km fra Revits Internal Origin, kan nulpunktet ikke beholdes, og det andet valg er **Manuel
  georeference**.
- **Manuel georeference:** Jega ændrer ikke projektets koordinater (fx System 34 eller en landinspektørs). Adressen
  placeres ved det punkt, du vælger under **Placeres ved (i Revit)**: Project Base Point (standard), Survey Point
  eller Internal Origin. Du drejer og flytter selv situationsplanen på plads og trykker **Opdater**; terrænet følger
  med. Brug den kun, når adressen ikke kan følge projektets koordinater.
- **Tilbage fra Manuel georeference:** slå den fra i Indstillinger. Er projektets koordinater ikke adressens egne,
  spørger Jega som ved Hent fra adresse.

Adressepunktet er adressens officielle punkt i adresseregistret, ikke et opmålt fikspunkt. Koordinaterne passer med
grunden og terrænet, men er ikke gode nok til afsætning.

### Koordinaterne i projektet

- **Projektets nulpunkt** er Project Base Point. Det får adressepunktets E og N i hele meter (ETRS89/UTM32), fx
  E 595.129 / N 6.399.156. Alt, hvad Jega placerer, regnes fra det: situationsplanen, terrænet, grunden og
  nabobygningerne.
- **Survey Point** flytter Revit ud til koordinatsystemets 0,0, over 6.000 km væk. Det er ikke en fejl: sådan virker
  delte koordinater i Revit. Flyt det ikke: så flytter projektets koordinater med.
- **To kryds på situationsplanen:** det store kryds er tegningens 0,0, projektets nulpunkt (adressepunktet, hele
  meter). Det lille kryds er adressepunktet, adressens officielle punkt i adresseregistret, højst ca. 0,7 m derfra.
- **Andre modeller** (konstruktør, landskab, landinspektør) i ETRS89/UTM32 placeres samme sted, når du linker dem med
  **Auto – By Shared Coordinates**.
- **Eksportér:** DXF'en og CSV'en er i mm fra projektets nulpunkt, ikke i UTM32. Nulpunktets E/N står i noten på
  situationsplanen. Giv dem videre sammen med filerne.
- **Nord:** Revits True North, som Jega kalder geografisk nord, følger nettet i ETRS89/UTM32 (kortets nord), ikke den
  geografiske nordpol. Det gælder alle modeller, der er georefereret i ETRS89/UTM32, og det er det, der får dem til at
  passe sammen. Forskellen (meridiankonvergensen) er 0° ved 9° øst og vokser mod øst: ca. 1,3° i Skagen, ca. 3° i
  København og ca. 5° på Bornholm
  ([Klimadatastyrelsen](https://cdn1.gopublic.dk/klimadatastyrelsen/media/2917583/001-etrs89-utm.pdf),
  [meridiankonvergens](https://en.wikipedia.org/wiki/Transverse_Mercator_projection#Convergence)). Solstudier i Revit
  bruger True North: 3° svarer til ca. 12 minutter på solens gang. Projektets sted til solstudiet (Manage > Location)
  sætter Jega ikke. Det vælger du selv.
- **Afstande:** ETRS89/UTM32 er en kortprojektion. Afstande på kortet afviger fra afstande i marken, op til ca. 40 cm
  pr. km i zone 32 ([Klimadatastyrelsen](https://cdn1.gopublic.dk/klimadatastyrelsen/media/2917583/001-etrs89-utm.pdf)).
  På Bornholm, som Jega også regner i UTM32, er det over 1 m pr. km
  ([skalafaktor](https://en.wikipedia.org/wiki/Transverse_Mercator_projection#Point_scale)). Skelmålene er kortets. Til
  afsætning bruges en landinspektørs opmåling.

## Data og kreditering

Grundene kommer fra Matriklen (Geodatastyrelsen), bygninger, veje og vand fra GeoDanmark og terrænet fra Danmarks
Højdemodel, alle via Datafordeleren. Adresserne kommer fra Danmarks Adresseregister via Adressevælger, og vejnavne og
husnumre omkring grunden fra Danmarks Adresseregister via Datafordeleren. Kilder, vilkår og licenser (bl.a. CC BY 4.0)
står i [DATA.md](DATA.md).

Dataenes vilkår kræver, at kilden nævnes. Jega skriver det på situationsplanen, i meddelelserne efter **Hent fra
adresse**, **Nabobygninger** (når de hentes) og **Eksportér** og i **Om Jega**. Lad teksten stå, når du bruger
tegningen videre:

> Indeholder data fra Klimadatastyrelsen (Danmarks Højdemodel, GeoDanmark, Danmarks Adresseregister) og
> Geodatastyrelsen (Matriklen) via Datafordeleren og Adressevælger.

**Alt, hvad Jega tegner, er vejledende.** Koterne er gode til ca. ±0,1 m. Højdemodellen er fløjet for nogle år
siden, og et træ over et tag læses som tag. Skellene er matrikelkortets, ikke en opmåling. Kontrollér mod opmåling
og officielle kilder, før du projekterer efter, søger byggetilladelse med eller bygger efter noget. Jega leveres uden
garanti.

## Kendte begrænsninger i betaen

- Revit 2025 er prøvet; Revit 2026 er ikke. Kun danske adresser.
- Revit 2024 og tidligere kan ikke indlæse Jega (Jega er bygget til .NET 8, som Revit 2025 og 2026 bruger).
- Jega er ikke signeret endnu, så Windows og Revit spørger ved installationen og første start.
- Ét hul i højdemodellen i terrænnettet (grunden og 5 m omkring) stopper hentningen.
- En grund over ca. 2 km på tværs kan ikke hentes. Over ca. 160 m hentes terrænet i flere dele.
- Kotekrydset laves fra Revits skabelon "Metric Generic Annotation.rft". Mangler den, kommer koterne uden kryds. Jega
  siger det.
- Tjenesterne giver højst et vist antal objekter pr. kald. Med en meget stor **Margen om grunden** kan naboskel eller
  bygninger derfor mangle på situationsplanen; meddelelsen siger, hvad der mangler, men ikke hvorfor. Sæt margenen ned,
  og hent igen.
- Brønddæksler og riste er kun dem, der ses fra luften: i veje og på pladser, sjældent i haver; typen kendes ikke.
- Træer er kun de markante, der ses fra luften, uden art, højde eller kronestørrelse; en have fuld af småtræer ser tom
  ud.
- **Arbejdsretning** virker kun i en visning, der står i Project North. Jega siger det.
- Terrænlinjernes koter står efter visningens retning. Efter Arbejdsretning rettes de ved næste Opdater.
- En spejlet situationsplan kan terrænet ikke følge. Jega siger det.
- **Tegn grund** følger ikke med ved Opdater. Tegn den igen, når situationsplanen har flyttet sig.
- Terrænlinjernes koter laves om, når en terrænlinje i samme visning ændres, og ved Opdater og True / Project North.
  En kote, du har flyttet, slettet eller skjult, kommer da tilbage. Vil du have færre, så brug stilen "Jega Line
  (elevations only)", som kun viser koterne i knækkene.
- Én situationsplan ad gangen i et projekt.
- Delte modeller er stadig under udvikling; de er prøvet med to brugere. Lad én bruger hente adressen og sætte terrænet
  op, og synkronisér før Opdater (se [For BIM-ansvarlige](#for-bim-ansvarlige)).
- Adressesøgningen bruger Adressevælgers fælles token, som Klimadatastyrelsen anbefaler, indtil deres brugerstyring er
  klar. Kræver tjenesten senere et eget token, sætter du det i Indstillinger > Dataadgang > Avanceret. Svarer
  Adressevælger ikke, søger Jega via Datafordeleren med din nøgle (se [Hent fra adresse](#3-hent-fra-adresse)).
- Datafordeleren lægger sine tjenester om. Holder hentningen pludselig op med at virke, så se efter en ny udgave (se
  [Følg med](#følg-med)).

## Privatliv

**På din pc** (kun din Windows-bruger):
- `%APPDATA%\Jega\settings.json`: din Datafordeler-nøgle, krypteret til din Windows-bruger, og et eventuelt eget
  Adressevælger-token (ukrypteret). Nøglen kommer aldrig i projektfilen.
- `%APPDATA%\Jega\jega.log`: fejl og noter til en fejlrapport (højst ca. 1 MB, derefter `jega.old.log`). Samme mappe
  har kotekrydsets familie.
- `%LOCALAPPDATA%\Jega\last-folder.txt`: den mappe, du sidst indlæste en terræn-CSV fra.
- Under hentningen skrives situationsplanen kort i `%TEMP%\Jega\`. Den slettes efter importen; fejler importen, bliver
  den, så du kan importere den selv.

**I projektfilen:** indstillingerne, adressen og dens koordinater, terrænet, grunden, omgivelserne og
nabobygningerne. Er projektet ikke gemt, får Eksportérs filer adressen som navn. Tænk på det, når du sender modellen
eller filerne videre.

**Det sendes:**
- Det, du skriver i adressefeltet, mens du skriver, til Adressevælger (`adressevaelger.dk`); svarer den ikke, i stedet
  til Danmarks Adresseregister på Datafordeleren med din nøgle (`graphql.datafordeler.dk`).
- Et område omkring grunden (koordinater) og din nøgle til Datafordeleren (`wfs.datafordeler.dk`,
  `wcs.datafordeler.dk`).
- Et område omkring grunden (koordinater) og din nøgle til Danmarks Adresseregister på Datafordeleren
  (`graphql.datafordeler.dk`).
- Jegas navn og version (`User-Agent: Jega/0.9.0-beta.1`) til alle fire tjenester, så de kan se, at kaldet kommer fra
  Jega.

Alt går over HTTPS. Nøglen og tokenet står i adressen på hvert kald, så en kontor-proxy, der åbner HTTPS-trafik, kan
se dem. Jega sender kun til de fire tjenester herover. Jega sender ingen brugsdata.

## Meld en fejl

Meld fejl og idéer under [Issues](https://github.com/AskandarS/Jega/issues).

**Issues på GitHub er offentlige.** Skriv aldrig en klients adresse, og vis den aldrig på et skærmbillede. Skjul
adresse, koordinater (E/N) og filnavne. Prøv at vise fejlen med en offentlig adresse, fx Vejle Rådhus, Skolegade 1,
7100 Vejle, eller Skagens Museum, Brøndumsvej 4, 9990 Skagen.

Siger meddelelsen efter **Hent fra adresse** "Udeladt af situationsplanen, fordi tegningen af laget fejlede: …", er
det en fejl i Jega, ikke i dataene. Meld den med lagets navn.

Skriv gerne:
- Jegas version (**Om Jega** viser den nederst) og Revits version.
- Hvad du gjorde, hvad du så, og hvad du havde ventet.
- `jega.log` som vedhæftet fil. Den ligger i `%APPDATA%\Jega`; stopper Jega uventet, har beskeden knappen
  **Åbn mappen**. Se loggen igennem, før du vedhæfter den: den kan indeholde visningsnavne, stier og fejltekster.

Send aldrig din nøgle eller `settings.json`.

### Følg med

Nye udgaver kommer under [Releases](https://github.com/AskandarS/Jega/releases). Tryk **Watch > Custom > Releases** på GitHub for at få
besked.

## For BIM-ansvarlige

**Det lægger Jega i modellen:**
- Linjestilene "Jega Grid", "Jega Edge", "Jega Line" og "Jega Line (elevations only)", teksttypen "Jega 3mm" (navnet
  følger **Tekststørrelse**: "Jega 4mm" osv.), familien "Jega Cross", Toposolid-typen "Jega Terrain", Filled Region-typen "Jega Lot Boundary" og situationsplanens
  CAD-import.
- **Elementerne** (Toposolid'en, niveauet "Terrain", grunden, terrænrammen, nabobygningerne, situationsplanen og
  terrænlinjernes koter) og teksttypen finder Jega ved et mærke, så dem må du omdøbe.
- **Typerne, linjestilene og familien** finder Jega ved navnet. Omdøber du "Jega Grid", "Jega Edge", "Jega Terrain",
  "Jega Lot Boundary" eller "Jega Cross", laver Jega en ny med det gamle navn, næste gang den skal bruges. Omdøber du
  de to "Jega Line"-stile, følger Jega ikke længere linjerne. Skift gerne farve og tykkelse, men ikke navnet.
- Niveauet "Terrain" ("Jega Terrain", hvis navnet er taget) med sin planvisning, når du trykker Opret terræn.
- Data på skjulte DataStorage-elementer: indstillingerne, adressen, terrænet (som tekst, op til ca. 30 MB ved det
  største net på 1.000 × 1.000 punkter, så modellen vokser tilsvarende), grunden, omgivelserne og nabobygningerne.
  Aldrig din nøgle. Andre add-ins kan læse dem, men kun Jega kan ændre dem.

**Fjerne Jega fra en model:** slet situationsplanen, Toposolid'en, niveauet "Terrain", grunden, terrænrammen og
nabobygningerne; typerne ("Jega Terrain" osv.) kan derefter fjernes med **Manage > Purge Unused** som alle andre.
Dataene på DataStorage-elementerne bliver liggende: Purge Unused fjerner dem ikke. De gør ingen skade, men terrænet kan
fylde op til ca. 30 MB. En knap til at fjerne dem kommer efter betaen.

**Kopier er dine:** en kopi af Jegas Toposolid, niveau, grund, terrænramme eller nabobygninger flytter eller sletter
Jega ikke. En kopi af situationsplanen kan dog forsvinde, når Jega tegner situationsplanen igen (Hent fra adresse, Lag,
sprog, og Opdater efter Arbejdsretning); importér den igen, hvor du skal bruge den. En kopieret terrænlinje er stadig
en terrænlinje.

**Koordinater:** Hent fra adresse ændrer projektets delte koordinater og geografisk nord (se
[Georeference](#georeference)). Relative koter giver Project Base Point gulvkoten. Har projektet links, spørger Jega
først. Med Manuel georeference rører Jega ikke koordinaterne.

**Nabobygningerne** er DirectShapes i Generic Models med en kommentar ("Jega nabobygning: …" på dansk, "Jega context:
…" på engelsk). De kommer med i skemaer og IFC-eksport. Filtrér dem fra på **Comments**, eller slå dem fra med
Nabobygninger før eksporten.

**Worksets og faser:** første gang havner det, Jega laver, i det aktive workset og visningens fase. Laver Jega et
element igen, beholder det det gamles workset og faser. Undtagelse i betaen: slår du **Nabobygninger** fra og til
igen, laves de på ny i det aktive workset og visningens fase. Opdater beholder sorteringen. Vil du skjule dem uden at
miste den, så slå deres workset fra i visningen.

**Delt model** (stadig under udvikling): synkronisér og vælg Reload Latest, før du kører Opdater. Synkronisér også
efter Opdater: indtil da kan de andre ikke køre Opdater. Siger Opdater
"Situationsplanen er ikke i modellen", så vælg **Annuller**, synkronisér og prøv igen. Sker det igen, så send jega.log
med i fejlrapporten.

**Netværk:** kun `adressevaelger.dk`, `wfs.datafordeler.dk`, `wcs.datafordeler.dk` og `graphql.datafordeler.dk` over
HTTPS. En firewall eller
proxy skal lukke dem igennem. Jega bruger Windows' proxyindstillinger og logger på proxyen med brugerens Windows-login.
Afviser proxyen, siger meddelelsen, at det er kontorets netværk og ikke nøglen.

## For udviklere

Byg selv og bidrag: [CONTRIBUTING.md](CONTRIBUTING.md). Hvordan Jega er bygget: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).
De danske ord i brugerfladen: [docs/GLOSSARY.da.md](docs/GLOSSARY.da.md). Ændringer: [CHANGELOG.md](CHANGELOG.md).

## Licens

Jega er fri software under **GNU General Public License, version 3** (se [LICENSE](LICENSE)). Den leveres **uden
garanti**. [NOTICE](NOTICE) giver desuden lov til at køre den sammen med Revits API.

Autodesk og Revit er varemærker tilhørende Autodesk, Inc. Jega er ikke tilknyttet eller godkendt af Autodesk.

Lavet af Shamer Askandar.

Udarbejdet med AI-assistance.
