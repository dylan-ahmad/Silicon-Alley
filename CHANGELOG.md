# Changelog

All notable changes to **Silicon Alley** are recorded here. The version is the mod manifest version
(`Assets/Mods/SiliconAlley/SiliconAlley.asset` → `Version:`) and the source of truth; merging a manifest
version bump to `main` auto-creates the matching `vX.Y.Z` git tag + GitHub Release
(see `.github/workflows/auto-tag.yml`). Format loosely follows [Keep a Changelog](https://keepachangelog.com).

## [0.4.1] — unreleased

### Added
- **Server furniture** base asset for the server-infrastructure epic: buyable/placeable item,
  prefab/model/material bundle assets, registry entry, locale, and save-compat ledger token.

### Fixed
- **Getting Started help** now clarifies that Silicon Alley offices are furnished **manually** (place a
  Computer Workstation + bathroom) and that the base-game **Interior Installation Firm** reports *"no
  designs available"* for these custom business types — expected, not a bug (the *product* design is the
  separate **F9 Design Wizard**). Response to a subscriber report.

## [0.4.0] — unreleased

The biggest update yet: plan every product in a Software-Inc.-scale **Design Wizard**, drive each project
through its lifecycle yourself, learn it all from a brand-new **in-game Help system**, and run your studios
through a fully **restyled UI**.

### Added
- **In-game Help** inside Big Ambitions' own Help System (no external wiki). The 3 studios appear under
  *Business Types* and the 3 products under *Goods and Services*, plus a dedicated **Silicon Alley** help
  category: **Getting Started**, **Design Wizard**, and six **Economy & market** guides (Contracts · Market
  Demand · Marketing · Publisher Deals · Product Lifecycle · Bugs & Reviews). Reachable from a **"How it
  works"** phone option, a configurable **hotkey** (default **F1**), and a one-time **first-run nudge**.
- **Design Wizard** (open with **F9**): Concept/scope (Quick win / Standard / Ambitious), product name +
  Polish↔Speed focus, **Features**, build-or-license **Editors & tools**, build-or-buy **Components**,
  feature→tool **coverage**, target **Platforms**, audience **Segment** (price↔volume), per-feature
  **allocation** scored against rotating **aspect demand**, and a **Summary** review before you commit.
- **Player-driven lifecycle** — projects move *Idle → Design → Development → Testing → Release*, and you push
  each stage forward yourself.
- **Contracts** — fixed-scope side jobs from the *Silicon Alley Clients* phone contact.
- **Dynamic market demand** — per-category demand cycles; time launches/updates for a peak.
- **Marketing–agency synergy** — owning a base-game Marketing Agency feeds your studios free awareness.
- **Per-concept icon set** (game-icons.net art, CC BY 3.0; attributed in `CREDITS.md`).

### Changed
- **Full UI overhaul** on a cohesive dark theme: shared 9-slice sprite kit + styled-component layer; wizard
  step indicator + page transitions; design-document cards; polished Summary review card; the **studio
  dashboard** (default **F8**) as status cards; restyled project-screen sections; hover/press scaling and
  animated values.

### Fixed
- Office business types now reliably appear in the Start-Business list.
- Walk-in customers no longer spawn at the office studios.
- Interior rating no longer gets stuck at 1.
- Dependency and build fixes (incl. a missing `using` in the help integration).

## [0.3.0] — 2026-06-21

Publishers & publishing deals, product lifecycle (aging, sequels / IP reputation), the go-to-market loop
(bugs, review score, marketing spend), the first-class money API for marketing, and a hardened, versioned
save format with the Save Compatibility Policy. Full diff: `git log v0.2.0..v0.3.0` (the `v0.2.0` tag is not
yet retroactively created — see git history before `v0.3.0`).
