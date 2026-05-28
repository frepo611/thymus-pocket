# Copilot Instructions

## Project Guidelines

- Development diary rule: files in `docs/utvecklar-dagbok/YYYY-MM-DD.md` may only be modified on the matching calendar date.

## SMF Action Source Of Truth

- For all work involving SMF URL parameters (`index.php?action=...`), use `docs/smf/action-catalog.json` as the primary source of truth.
- Do not invent or assume unsupported action names.
- If an action is missing from the catalog, treat it as unknown until verified in the target SMF `index.php`.
- Account for version differences: the catalog is version-bound and may differ from production forum behavior.
