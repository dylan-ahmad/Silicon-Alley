# Changelog

All notable changes to **Silicon Alley** are recorded here. The version is the mod manifest version
(`Assets/Mods/SiliconAlley/SiliconAlley.asset` → `Version:`) and the source of truth; merging a manifest
version bump to `main` auto-creates the matching `vX.Y.Z` git tag + GitHub Release
(see `.github/workflows/auto-tag.yml`). Format loosely follows [Keep a Changelog](https://keepachangelog.com).

## [Unreleased] — 0.5.0 (epic #121)

### Added
- **Save-compat groundwork** for the 0.5.0 gameplay-loop release (#122): the per-building save record gains
  two trailing fields — `milestoneMask` (which mid-project milestone decisions are resolved; per-project) and
  `contractFocus` (how much staff effort an active contract diverts; defaults to the legacy full divert) —
  and each release-history row now records the ship's reputation/market/demand/cleanliness multipliers so
  the ship report can survive a reload. Pure trailing appends: **old saves load unchanged** (nothing reads
  the new fields yet), pre-0.5.0 history rows show "—" where the multipliers weren't recorded.
- **Milestone decisions** (#123): four decision windows now open mid-project — two during Development
  (30% / 55%), two during Testing (80% / 92%), finally giving the long build stretch real choices. Each
  window surfaces one of two events (fixed per save — reloading cannot reroll it) with two options trading
  progress, quality, bugs, marketing buzz and cash: scope creep, a middleware offer, a tech-debt reckoning,
  a conference demo slot, a public-beta call, crunching the QA backlog, announcing a gold date, or promising
  a day-one patch. A clickable toast announces each window; ignoring it is always safe — the window quietly
  closes with zero effect, which is also why legacy in-flight projects play unchanged. (The in-screen
  decision card arrives with the 0.5.0 UI work; until then the toast + this changelog are the paper trail.)

### Changed
- **Shipping is now the headline payoff** (#124). The launch payout is multiplied by a launch scale fed by
  the launch installed-base jump — so marketing, reviews, sequels and IP reputation, extra platforms,
  segment volume and market fit all now grow the money a release pays, not just the support trickle. An
  unmarketed Standard ship that paid ≈$750 now pays ≈$3,300; a marketed one ≈$7,200; a big Ambitious sequel
  can clear $15–25k (capped). Support, hosting, updates and publisher bonuses are unchanged. To match,
  **phone contracts pay less** (fee ×1.5–2.5 of scope, was ×2.5–4): a contract is a bridge between
  releases now, not the meta. Old saves: forward economics only — recorded history keeps its old numbers.
- Release-history rows now record the ship's reputation/market/demand/cleanliness multipliers at ship time
  (#122), so the upcoming history screen can explain every launch after a reload.

## [0.4.1] — 2026-07-28

### Added
- **Server infrastructure** — a new gameloop. **Server** racks are buyable, placeable furniture (Mr. Scott's
  Office Supplies, $15,000); place them in a studio office and assign each one a role from the **F8** Studio
  Dashboard, which lists every placed Server with a 3-button role selector and a live summary that refreshes
  on the 1-second tick. Each role pulls a different lever:
  - **Infrastructure** — faster project progress and fewer bugs during development/testing (diminishing
    returns, capped).
  - **Backend** — self-hosts part of your cloud-backend dependency, cutting that vendor royalty out of launch
    *and* support income, proportional to how much of your installed base the servers cover. Fully
    reversible, and a no-op if you already built that component in-house.
  - **Hosting** — flat passive income every hour, independent of the installed base, with sub-dollar amounts
    carried between hours so nothing is lost.

  Every placed Server charges **daily upkeep** whatever its role, so over-provisioning is punished and
  right-sizing rewarded; the dashboard shows upkeep, hosting net, backend coverage and infrastructure
  bonuses. Infrastructure strength, hosting income, daily upkeep and backend capacity per server are all
  options sliders. Role assignments persist per server. **Old saves are unaffected** — no servers means
  nothing changes.
- **Server Infrastructure help page** explaining the loop, the three roles, scaling and upkeep, cross-linked
  from the Silicon Alley overview.
- **Abandon project** button in the design window (F9) footer — a permanent escape hatch out of any project
  you no longer want. Press twice to confirm; the studio returns to **Idle**, ready to start something new.
  You lose that project's progress, bugs, marketing and design picks, but keep reputation, installed base,
  version number, self-built tools and components, an active publisher deal and any accepted contract.
- **Design wizard Summary** now reviews the whole product before you commit. The card is grouped into
  **Product** / **Cost** / **Market**, and two things it never mentioned are now stated outright: a
  **Components** row showing how many parts of your stack you built versus licensed, and a **Market fit**
  row showing what your feature allocation is worth against current demand — in revenue and quality, signed
  against an even split, matching the Market step's own readout. A project with fewer than two features
  reads "not targeted" rather than a misleading +0%.

### Fixed
- **Design wizard Summary cost labels** said "owned tools" and "licensed tool(s)" while the figures had
  included build-or-buy components since they were added, under-reporting what you were being charged for.
  The "Dependencies" row was also named for a different concept than the components step it sat next to —
  it has always shown feature→tool coverage, and is now labelled **Feature coverage**.
- **Design lock dead end** — shrinking a project mid-Design (un-ticking features/platforms, or dropping the
  scope from Ambitious to Quick) could permanently hide **Start development**: the screen showed
  *"Design (locked)"* with no Back/Next row and no Development card, so the studio could never ship and every
  publisher deal and contract lapsed — the *"it says design lock and wont do anything, i miss all deadlines"*
  report. The wizard was gated on the *derived* phase, recomputed from your live design picks, instead of the
  project's actual stage; a smaller project pushed already-earned progress past the Design band and the only
  way out disappeared. **Saves already stuck in this state recover on load, with no player action.** Progress
  now also clamps back into the current stage when a project shrinks, instead of being stranded above it.
- **macOS support** — the mod now ships a **Mac AssetBundle** alongside the Windows one. Previously the
  manifest targeted Windows only, so on macOS the game looked for `AssetBundles/Mac/siliconalley.unity3d`,
  found nothing, and the mod loaded but registered **no content at all** — no items, business types,
  Server or UI. The bundle-missing diagnostic now also names the path for the platform you are actually
  running on instead of always reporting the Windows one. (Windows installs are unaffected.)
- **Getting Started help** now clarifies that Silicon Alley offices are furnished **manually** (place a
  Computer Workstation + bathroom) and that the base-game **Interior Installation Firm** reports *"no
  designs available"* for these custom business types — expected, not a bug (the *product* design is the
  separate **F9 Design Wizard**). Response to a subscriber report.
- **Publisher Deals and Contracts help** now explain that deadlines run on **calendar days** while work only
  happens during hours the studio is **open** with someone actually **at a workstation** — so short opening
  hours or thin staffing burn a deadline without moving the build. Both pages also warn that a queued
  release is only carried out on the next *open* hour (so releasing after closing time on the deadline day
  still misses the deal) and that taking a contract pauses your product, which is close to a guaranteed
  deal miss. Deadlines behaved this way all along; nothing said so.

## [0.4.0] — 2026-06-30

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
