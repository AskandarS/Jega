# Data i Jega

Jega henter åbne data direkte fra de offentlige tjenester med **din egen nøgle**. Intet går andre steder hen: ingen
andre end tjenesterne får din nøgle, dine adresser eller dine data. Nøglen ligger krypteret på din egen computer
(`%APPDATA%\Jega\settings.json`, beskyttet til din Windows-bruger).

Datafordeleren tager nøglen i adressen på hvert kald (`apiKey=…`), og Adressevælger tokenet (`token=…`). Over HTTPS
er de skjult for andre på nettet, men en kontor-proxy, der åbner HTTPS-trafik (TLS-inspektion), kan se og logge dem.

## Kilder

| Hvad | Kilde | Tjeneste |
|---|---|---|
| Grunde (matrikelskel, matrikelnumre, areal) | Matriklen, Geodatastyrelsen | Datafordeleren, WFS |
| Bygninger, vejkanter, søer, vandløb, grøfter, træer, trægrupper, skov, hegn, brønddæksler, nedløbsriste | GeoDanmark | Datafordeleren, WFS |
| Terræn (og overflade til tagkoterne og nabohusenes højder) | Danmarks Højdemodel, Klimadatastyrelsen | Datafordeleren, WCS |
| Adresser og adressepunkter | Danmarks Adresseregister (DAR), Klimadatastyrelsen | Adressevælger, adressevaelger.dk |
| Vejnavne og husnumre omkring grunden | Danmarks Adresseregister (DAR), Klimadatastyrelsen | Datafordeleren, GraphQL (graphql.datafordeler.dk, med nøglen) |

## Vilkår og licens

Klimadatastyrelsens data (Danmarks Højdemodel, GeoDanmark og Danmarks Adresseregister) og Geodatastyrelsens Matriklen
må alle bruges, deles og tilpasses, også erhvervsmæssigt, under
[CC BY 4.0](https://creativecommons.org/licenses/by/4.0/deed.da), når kilden krediteres (se nedenfor). Vilkårene står
hos [Klimadatastyrelsen](https://www.klimadatastyrelsen.dk/om-klimadatastyrelsen/vilkaar-og-priser) og
[Geodatastyrelsen](https://gst.dk/data-og-kort/koeb-af-data-og-kort/koeb-af-matrikeldata-og-kort/vilkaar-for-brug-af-frie-geografiske-data-fra-geodatastyrelsen),
Matriklens også hos [Datafordeleren](https://datafordeler.dk/vejledning/brugervilkaar/ejendomsoplysninger-ebr-og-mat/).
Datafordelerens egne [brugervilkår](https://datafordeler.dk/vejledning/brugervilkaar/) gælder for din nøgle. Du er
selv ansvarlig for din nøgle og for at overholde Datafordelerens og Adressevælgers vilkår.

## Kreditering

Dataenes vilkår kræver, at kilden nævnes. Jega skriver det på situationsplanen, i meddelelserne efter **Hent fra
adresse**, **Nabobygninger** (når de hentes) og **Eksportér** og i **Om Jega**:

> Indeholder data fra Klimadatastyrelsen (Danmarks Højdemodel, GeoDanmark, Danmarks Adresseregister) og
> Geodatastyrelsen (Matriklen) via Datafordeleren og Adressevælger.

Lad teksten stå, når du bruger tegningen videre.

## Vejledende, ikke opmåling

Koter, skel og andre data er vejledende: koterne ca. ±0,1 m, skellene som på matrikelkortet. De erstatter ikke
opmåling. Kontrollér mod opmåling og officielle kilder, før der projekteres, søges byggetilladelse eller bygges efter
dem. Jega leveres uden garanti (GPL-3.0, afsnit 15 og 16 i `LICENSE`).

---

## Data in Jega (English)

Jega fetches open data straight from the public services with **your own key**; nothing goes anywhere else.
Sources: Matriklen (Geodatastyrelsen), GeoDanmark and Danmarks Højdemodel (Klimadatastyrelsen) via Datafordeleren,
addresses from Danmarks Adresseregister (Klimadatastyrelsen) via Adressevælger, and the street names and house numbers
round the lot from Danmarks Adresseregister via Datafordeleren's GraphQL (with the key). You are responsible for your key
and for the services' terms. Klimadatastyrelsen's data (Danmarks Højdemodel, GeoDanmark, Danmarks Adresseregister) and
Geodatastyrelsen's Matriklen are all under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/); the terms are at
[Klimadatastyrelsen](https://www.klimadatastyrelsen.dk/om-klimadatastyrelsen/vilkaar-og-priser) and
[Geodatastyrelsen](https://gst.dk/data-og-kort/koeb-af-data-og-kort/koeb-af-matrikeldata-og-kort/vilkaar-for-brug-af-frie-geografiske-data-fra-geodatastyrelsen),
Matriklen's also at [Datafordeleren](https://datafordeler.dk/vejledning/brugervilkaar/ejendomsoplysninger-ebr-og-mat/),
and Datafordeleren's own [terms of use](https://datafordeler.dk/vejledning/brugervilkaar/) cover your key. The key goes
in the address of every call (`apiKey=…`), the Adressevælger token likewise: hidden by HTTPS, but an office proxy that
inspects HTTPS can see and log them. Required credit, kept on the site plan, in the summaries and in About: *Contains
data from Klimadatastyrelsen (Danmarks Højdemodel, GeoDanmark, Danmarks Adresseregister) and Geodatastyrelsen
(Matriklen) via Datafordeleren and Adressevælger.* Levels and boundaries are indicative (levels about ±0.1 m), not a
survey; no warranty (GPL-3.0, sections 15 and 16).
