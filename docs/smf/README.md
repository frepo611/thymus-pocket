# SMF action-katalog

Denna mapp innehåller en tydlig definition av vilka `action`-parametrar SMF stöder via `index.php?action=...`.

## Syfte

- Ge team och AI-agenter en gemensam, versionerad källa.
- Minska risken att agenter hittar på actions som inte finns.
- Göra det enkelt att jämföra mellan SMF-versioner.

## Filer

- `action-catalog.json`: maskinläsbar katalog för automation och agenter.

## Viktiga begränsningar

- Katalogen kommer från SMF `index.php` och kan påverkas av `integrate_actions` hooks.
- Er produktionsforumversion kan avvika från den extraherade versionen.
- Vissa actions kräver särskilda rättigheter, sessionstillstånd eller formulärtoken.

## Rekommenderat arbetssätt

1. Slå alltid upp action i `action-catalog.json` innan implementation.
2. Om action saknas: markera som okänd och verifiera i aktuell `index.php`.
3. Vid versionsbyte av SMF: uppdatera katalogen i samma commit som versionsändringen.
