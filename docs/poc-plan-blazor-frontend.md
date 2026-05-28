# POC-plan: vägen till Blazor-frontend

## Syfte

Denna POC-plan beskriver hur vi går från nuvarande SMF-konsolklient (skrapning/parsing via `SmfHttpClient`) till en körbar Blazor-frontend med en skyddande BFF.

Målet är att validera arkitektur, säkerhetsmodell och användarflöden utan att ändra befintligt forum.

## Planstatus

- Status: Fastställd
- Beslutsdatum: 2026-05-28
- Inriktning: Blazor frontend via BFF med Minimal API i POC-fasen
- Scope för första vertikala slice: login, trådlista, trådvy, svar i tråd
- Nästa exekveringssteg: påbörja Fas 1 med extraktion till adapterbibliotek

## Utgångsläge (idag)

- Nuvarande POC (`src/Thymus.Poc`) kan:
  - Logga in mot SMF
  - Läsa trådar/inlägg
  - Skapa ämne/svara (demo-flaggor)
  - Hantera sessionscookies
- Klienten är en console-app, inte en webapp.
- Ingen publik JSON-API-kontrakt finns ännu mellan klient och backend.

## Målbild för POC

- En Blazor Web App som kan:
  - Logga in via BFF
  - Visa trådlista
  - Visa tråd med inlägg
  - Skapa svar (minst ett write-flöde)
- Forumcookies och foruminloggning stannar server-side i BFF.
- Browsern får endast en BFF-session (HttpOnly-cookie).

## Icke-mål i denna POC

- Full parity med hela forumets funktioner
- Push/realtime
- Komplett offline-stöd
- Färdig produktionshärdning av drift, observability och autoskalning

## Arkitektur för POC

- Blazor frontend
  - Rekommendation: Blazor Web App (.NET 10) med interaktiva komponenter
  - Server-rendering först, valfri WASM-hybrid senare
- BFF API (ASP.NET Core minimal API)
  - Normaliserar SMF HTML till JSON DTO:er
  - Exponerar endast de endpoints frontend behöver
- SMF adapterlager
  - Flytta/reuse logik från `SmfHttpClient` till återanvändbart bibliotek
- Session store
  - POC: in-memory eller filbaserat
  - Nästa steg: Redis

## Varför Minimal API i denna POC

Minimal API är ett bra val i just denna fas eftersom vi vill verifiera flöde och arkitektur snabbt, inte optimera för stor teamstruktur redan nu.

Argument:

- Snabbare time-to-first-slice
  - Färre lager och mindre boilerplate gör att vi snabbare får login -> trådar -> tråd -> svar i drift.
- Tydlig koppling mellan endpoint och SMF-adapter
  - Varje route kan mappas direkt till ett adapteranrop, vilket är praktiskt när vi fortfarande lär oss SMF:s beteenden.
- Enklare att iterera API-kontrakt
  - DTO:er och route-signaturer kan justeras snabbt när frontend-behoven klarnar.
- Lägre komplexitet i POC-drift
  - Mindre ramverksstruktur att hantera betyder enklare felsökning och kortare feedbackloop.
- Fullt kompatibelt med framtida härdning
  - Auth, cookies, rate limit, CSRF, OpenAPI och testbarhet fungerar bra även med Minimal API.

Trade-off att vara medveten om:

- När antalet endpoints och teammedlemmar växer kan Controller-baserad struktur ge bättre modularitet och styrning.

Beslutsregel framöver:

- Starta med Minimal API i POC.
- Om BFF passerar cirka 20-30 endpoints, flera versionsspår eller fler team som jobbar parallellt: utvärdera migrering till Controllers per funktionsområde.

## Faser och leverabler

### Fas 1: Extrahera domän- och adapterlogik

Leverabler:

- Nytt bibliotek, t.ex. `src/Thymus.SmfAdapter`
- Flytt av SMF-anrop/parsing från console-projekt till bibliotek
- Enhetstester för parsing av login, trådlista och tråd

Exit-kriterier:

- Console-projektet använder adapterbiblioteket utan funktionsförlust
- Minst 3 parsertester passerar stabilt

### Fas 2: BFF med minimalt API-kontrakt

Leverabler:

- Nytt projekt, t.ex. `src/Thymus.Bff`
- Endpoints:
  - `POST /api/auth/login`
  - `POST /api/auth/logout`
  - `GET /api/threads`
  - `GET /api/threads/{id}`
  - `POST /api/threads/{id}/replies`
- DTO-kontrakt för trådlista, tråd och svar
- Sessionhantering med HttpOnly-cookie (`thymus_session`)

Exit-kriterier:

- Endpoints kan anropas lokalt via HTTP-klient
- Inga forumcookies exponeras i API-svar
- Login/logout fungerar över flera requests

### Fas 3: Blazor frontend (första vertikala slice)

Leverabler:

- Nytt projekt, t.ex. `src/Thymus.Web`
- Sidor/komponenter:
  - Login
  - Trådlista
  - Trådvy
  - Svara i tråd
- API-klient i frontend mot BFF
- Baslayout mobil-först

Exit-kriterier:

- Användaren kan logga in och se trådar från riktig SMF-data
- Användaren kan öppna tråd och posta ett svar via BFF
- Flödet fungerar i mobil viewport

### Fas 4: Kvalitet, säkerhet och POC-avslut

Leverabler:

- Grundläggande felhantering (timeouts, auth-fel, parser-fel)
- Enkel rate limit i BFF
- CSRF-skydd för write-endpoints
- Demo-manus och checklista

Exit-kriterier:

- Definierat demo-scenario går att köra end-to-end utan manuella workaround
- Kända risker och nästa steg dokumenterade

## Föreslagen mappstruktur

- `src/Thymus.SmfAdapter`
- `src/Thymus.Bff`
- `src/Thymus.Web`
- `tests/Thymus.SmfAdapter.Tests`
- `tests/Thymus.Bff.Tests`

## API-kontrakt (POC-nivå)

Exempel på read-modell:

- `ThreadSummaryDto`:
  - `Id`
  - `Title`
  - `Board`
  - `LastPostBy`
  - `LastPostAt`
- `ThreadDetailsDto`:
  - `Id`
  - `Title`
  - `Posts[]` (Author, PostedAt, Body)

Exempel write:

- `ReplyRequestDto`:
  - `Subject`
  - `Message`

## Tekniska beslut (POC)

- Backend och frontend i .NET 10
- Parsing fortsatt via AngleSharp
- Session-cookie: HttpOnly + Secure + SameSite=Lax
- Feature flags för write-flöden (motsvarande nuvarande demo-flaggor)

## Risker och mitigering

- SMF HTML-struktur kan ändras
  - Mitigering: parsertester med sparade HTML-fixtures
- Inloggningsflöde känsligt för sessionsdetaljer
  - Mitigering: tydlig adapter-abstraktion + diagnostisk loggning
- För mycket scope i första UI-versionen
  - Mitigering: lås till 1 read-flöde + 1 write-flöde

## Tidsestimat (arbetsdagar)

- Fas 1: 1-2 dagar
- Fas 2: 2-3 dagar
- Fas 3: 2-3 dagar
- Fas 4: 1 dag
- Totalt: 6-9 dagar

## Definition of Done för hela POC

- End-to-end: login -> trådlista -> tråd -> svar -> logout
- Blazor-UI fungerar på mobil och desktop
- BFF döljer forumets cookies/credentials helt
- Grundtester finns för parser och kritiska API-flöden
- Dokumenterad lista med rekommenderade nästa produktionssteg

## Rekommenderat nästa steg efter denna plan

1. Skapa solution-projekt för `Thymus.SmfAdapter`, `Thymus.Bff` och `Thymus.Web`.
2. Flytta existerande SMF-logik till adapterbiblioteket och säkra med tester.
3. Bygg första vertikala slice i Blazor via BFF enligt fas 2-3.
