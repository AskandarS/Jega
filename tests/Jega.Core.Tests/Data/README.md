# Test data

Real data about the test case, Skagens Museum, Brøndumsvej 4, 9990 Skagen (a public museum), fetched with Jega on
21 October 2026, so the tests run without a key:

| File | What | Source |
|---|---|---|
| `skagens-museum-terrain.csv` | the terrain grid, 0.4 m, mm from the project's origin, DVR90 | Danmarks Højdemodel (Klimadatastyrelsen) via Datafordeleren |
| `skagens-museum-surroundings.json` | lots, buildings, roads and water round the lot, and the roofs on it | Matriklen (Geodatastyrelsen), Danmarks Højdemodel (Klimadatastyrelsen), GeoDanmark, via Datafordeleren |
| `skagens-museum-context.json` | the neighbouring buildings with their heights | GeoDanmark and Danmarks Højdemodel via Datafordeleren |
| `skagens-museum-siteplan*.dxf` | the site plan Jega drew from the files above, in four variants (`SiteplanGoldenTests`) | drawn by Jega |

The address and its point come from Danmarks Adresseregister via Adressevælger.

Contains data from Klimadatastyrelsen (Danmarks Højdemodel, GeoDanmark, Danmarks Adresseregister) and Geodatastyrelsen
(Matriklen) via Datafordeleren and Adressevælger, under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) and the services'
terms (see `DATA.md` at the root). The data are indicative, not a survey. Replace them only with data about a public
building, never a client's address.
