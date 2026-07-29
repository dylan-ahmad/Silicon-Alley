# Changelog

All notable changes to **Silicon Alley** are recorded here. The version is the mod manifest version
(`Assets/Mods/SiliconAlley/SiliconAlley.asset` → `Version:`) and the source of truth; merging a manifest
version bump to `main` auto-creates the matching `vX.Y.Z` git tag + GitHub Release
(see `.github/workflows/auto-tag.yml`). Format loosely follows [Keep a Changelog](https://keepachangelog.com).

## [Unreleased]

### Added
- **Hub redesign: the overview finally answers "who needs me?"** (#148, epic #142). A **triage strip**
  tops the hub: "N studios need you" with one clickable badge per needing studio — open **milestone
  decision**, **contract/publisher deadline** within 3 days, **ready to release**, **update due** —
  that deep-links straight into that studio, plus an aggregate totals line (studios · support $/day ·
  installed base · servers · upkeep) computed from the same shared formatter the cards use, so the
  figures always agree. Studio cards sit in a **2-column grid** (the 940px window finally earns its
  width); needs-action studios carry a red/amber badge and **sort first**. The **whole card is
  clickable** with hover elevation (the Open button is gone), every card states **what is being built**
  ("{product} · v{n}"), and quiet studios render compact (an Idle studio drops its progress bar and
  ship ETA). **Server groups now live directly under their studio's card** — the disconnected Servers
  block below is gone, the group header slims to a muted "Servers" caption, and the role buttons read
  Infra/Backend/Host at the new column width. Presentation only — no save surface.
- **UI component kit** (#146, epic #142). The window finally gets a **scrollbar** — themed, overlaying
  the right padding, alpha-fading out when the content fits (hysteresis over the live overflow, so the
  per-second refresh can't flicker it). A **tooltip system** on the mod's own canvas (0.1 s hover
  delay, cursor-following, edge-flipping/clamping; opt-in per graphic — first tip: the hub card's
  demand-trend pill explains its ▲/▼ and multiplier). New primitives with live call sites everywhere:
  **tabs/segmented control** (the wizard scope picker), a real **checkbox/toggle** (Overtime, Ad Spend,
  and the features/platforms picker cards — the 30% colour-lerp "selected" tint and its badge are
  gone), a **collapsible section** (Release history now folds behind its header, default collapsed),
  a standalone **badge** (the history review pill), a **before › after delta readout** (the ship
  report shows the review vs the previous release, sign-coloured), a **disabled-reason line** (an
  unaffordable milestone option now says "You have $2,400 of $6,000" instead of greying silently), and
  a **segmented distribution bar** (the wizard's demand page draws market-wants vs your-allocation as
  two colour-legended charts instead of stacked mini progress-bars). **Abandon project** is a real
  in-canvas confirm dialog now — scrim blocks everything behind it, the confirm button is red, and
  Esc/backdrop-click cancels (Esc closes the dialog first, the screen second). Two new icons
  (`ui_check`, `ui_chevron_down`) via the #145 pipeline. Presentation only — no save surface.
- **The icon pack is complete — no more empty gutters** (#145, epic #142). Twenty-nine new
  game-icons.net concept icons (CC BY 3.0, credited in `CREDITS.md`) fill every stem the UI requests
  but never had art: the ETA / reputation / installed-base stat rows, all eight milestone decision
  events, the four publishers, the nine build-or-buy product dependencies and the three server role
  buttons. The wizard summary's Components and Market-fit rows get their own `stat_components` /
  `stat_fit` icons instead of borrowed category placeholders, and five new procedural placeholder
  glyphs (`cat_stat`/`cat_ms`/`cat_publisher`/`cat_dep`/`cat_server`) guarantee any future unresolved
  stem lands on a placeholder instead of an empty gutter. The SVG→white-128px-PNG authoring step is
  now a committed, reproducible pipeline (`tools/generate-icons.ps1` + `tools/icon-manifest.txt`).
  Presentation only.

### Fixed
- **The hub and the detail view now show the same support-$/day** (#144, epic #142). The detail view's
  ship report used a private support-income formatter that forgot the market-demand multiplier the hub
  card applies, so the two screens disagreed for the same studio at the same moment. Every screen now
  routes through the one `SiliconAlleyFormat` table (the detail $/day figure changes: it is now
  demand-scaled, matching what the simulator actually credits).

### Changed
- **One format table** (#144): all user-visible number shapes are normalized — multiplication is always
  `×` (never `x`), reviews are always `7.4/10`, percentages always carry their own `%` (locale strings
  no longer append it), money always formats as `$1,234` / `-$1,234`, and the server-economy line uses
  `·` separators like everywhere else. ETAs split by meaning: throughput *estimates* keep the tilde
  (`~2d 6h`) while exact calendar deadlines (contract due, publisher deadlines, patch timer) are bare
  (`14d`). The hub's demand trend pill now shows the multiplier shape too (`▲ ×1.12`). Presentation only.

### Changed (0.6.0 UI)
- **Window behavior: one width, draggable, jank-free refresh** (#147, epic #142). The project window is
  **one 940px width everywhere** — entering/leaving the Design stage no longer teleports it 440px — and
  it now hangs from a **fixed top edge** (height changes grow downward instead of moving both edges).
  It is **draggable by the title row**, and the position **persists across open/close and game
  sessions** (machine-local; a stale or off-screen position self-repairs on the next open). The
  per-second refresh is split into a value path (updates text/colors/bars in place, never forces a
  layout pass) and a structure path (the one forced rebuild, run only when sections/pages/modes really
  change, preserving the scroll position) — the once-a-second window "breathing" and scroll nudges are
  gone, the wizard no longer blinks its active page every tick, and a **server-role click repaints just
  its own card** instead of rebuilding the whole screen. (The game's `DraggableWindow` component turned
  out unusable at runtime — private serialized fields, and its handle lets the ScrollRect scroll during
  the drag — so the mod ships its own ~50-line drag handler.) Presentation only.
- **Design-system foundation** (#143, epic #142). The theme gains real design tokens: spacing / control-height /
  corner-radius / elevation scales, a `Status` (13pt) type size for the muted status lines, and new
  `Danger` / `Info` / `Focus` / `Scrim` / `Shadow` colors plus named state blends (`CardSelected`,
  `CardLicensed`, `StepDone`) and one shared button/card interaction tint set. **Amber now means caution
  only** — the armed "Abandon project" confirm turns red (`Danger`). The sprite kit gains a true **capsule
  pill** (chips, badges, wizard step dots, progress/slider tracks and fills no longer stretch their corner
  radii at any width), a hairline **outline**, and a soft **drop shadow** so cards and the window visibly
  lift off the surface. Chips get a minimum width + ellipsis (no more squashing in narrow wizard cards),
  text supports all nine alignments, headers return their label (stylable), and every raw font size and
  off-palette color in the screens now routes through the theme. Presentation only — no save surface;
  pre-0.6.0 bundles fall back gracefully (flat chips, no shadows).

## [0.5.0] — 2026-07-29

The gameplay-loop release (epic #121): the long empty middle of every project now asks real questions,
shipping finally pays like the headline act, the numbers that always steered your launch are on screen,
contracts are honest side-work instead of the meta, and everything lives in one hub.

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
  closes with zero effect, which is also why legacy in-flight projects play unchanged. The **decision card**
  (#128) renders the open window in the studio's detail view: the event, both options with their effect
  summaries (paid options show their price and grey out when you can't afford them), and the progress mark
  at which the window decides itself. The window's toast deep-links straight to the card.

### Added (0.5.0 UI)
- **Release history** (#129): every studio's detail view now lists its shipped catalog — day, name and
  version, scope, a colour-graded review score, quality, net payout, launch units and publisher — newest
  first, straight from the save, so it's all still there after a reload. The newest entry also explains
  its payout (reputation × market × demand × office cleanliness); releases shipped before 0.5.0 read
  "breakdown not recorded". The transient ship report remains for the fresh-ship moment.
- **Contract staff-split dial** (#129): the contract card gains the #126 slider — "All on contract" to
  "Product first" — with a live "{n}% contract · {n}% product" readout.
- **The hidden numbers are on screen** (#130). The **Press Build timing window** — the campaign always
  landed at only ×0.4 outside 50–72% of the build, silently — now shows as a live line under its button,
  green in the sweet spot. Development and Testing gain a **Projected review** ("{n}/10 if shipped now",
  the exact math the release would use) plus a **quality gates** readout spelling out the three silent
  multipliers: design ceiling, office cleanliness and open bugs. Presentation only — no behaviour changed.
- **Help** (#131): a new **Milestone Decisions** guide, and the Contracts, Marketing, Publisher Deals,
  Product Lifecycle, Bugs & Reviews, Server Infrastructure and Getting Started pages updated for the
  0.5.0 loop (the hub, the staff-split dial, the visible Press Build window, the launch economy, the
  projected review and the release history).

### Changed
- **One hub, one window** (#127). The F8 studio dashboard and the F9 project window are now a single
  screen: **F9 (or F8) lands on the Overview** — every studio as a card (stage, progress, quality,
  reputation, installed base, support, ship ETA, demand trend) plus the full **Servers** section with role
  assignment — and clicking a studio's **Open** dives into its detail view (wizard, development, testing,
  marketing, publishers, contract). A **‹ Overview** button in the header takes you back. Toast
  notifications still deep-link straight to the studio they're about. The separate dashboard window is
  gone; the phone client's "View studios" opens the same hub.
- **Contracts no longer freeze your product** (#126). A studio holding a contract splits its staff between
  the contract and its own product: accepting still starts at 100% contract (exactly the old behaviour),
  but a new focus setting lets you dial the split anywhere down to product-first — the product then accrues
  progress, bugs and QA at its share of the effort while the contract advances at the rest. Reputation only
  decays for a *fully* diverted or unstaffed active project now. Old saves with a contract in flight keep
  the full divert until you touch the dial (the in-screen slider lands with the 0.5.0 UI work).
- **Contract offers are fixed, not farmable** (#125). The phone client's offer is now derived from the
  studio and the calendar — hanging up and redialing returns the **same terms** until the 3-day offer
  window rolls over, closing the reroll-until-rich exploit. The call now also lets you **choose which
  studio takes the job**: "Next studio" cycles every studio that's free, each with its own offer, and the
  accept button names the studio it hires. (Deadlines stay 14–30 calendar days.)
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
