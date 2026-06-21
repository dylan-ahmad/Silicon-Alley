# CLAUDE.md — Silicon Alley (Big Ambitions mod)

A pack of IT-company business types for **Big Ambitions**, built with the official BA Mod SDK
(Unity **2022.3.62f2**). The mod lives in `Assets/Mods/SiliconAlley/` and ships as assembly
`SiliconAlley`, AssetBundle `siliconalley.unity3d`, Mod Id `SiliconAlley`, identifier prefix
`siliconalley:`.

## What it adds
- **3 office IT businesses** (founded in `ba:buildingtype_office`, staffed by `ba:skill_programmer`):
  Software Engineering Studio, Cyber Security Firm, Game Developer Studio.
- A **custom `BusinessSimulator`** (Software-Inc.-flavoured): programmers at workstations accrue
  project progress → projects complete with a quality score → revenue is credited via the normal
  order path → scaled by **reputation** and **neighbourhood competition** → an installed base earns
  recurring **support income**. State **persists in the save**.
- An in-game **options panel** (sliders) and a phone **"Silicon Alley Clients"** contact.

## Layout
- `Assets/Mods/SiliconAlley/`
  - `*.asset` (+ `.meta`) — 3 BusinessTypes, 3 product Items, 3 BusinessRequirements
    (DesktopWorkstation / BathroomStall / Sink), the `SiliconAlley.asset` ModManifest, `SiliconAlley.asmdef`.
  - `Scripts/` — `SiliconAlleyMod` (init: register items+businesses, assign simulator, register
    options), `SiliconAlleyOfficeSimulator` (the project simulator), `SiliconAlleyState` (per-building
    state + save serialization), `SiliconAlleyOptions`, `SiliconAlleyClient` (+ dialog),
    `SiliconAlleyPersistence`.
  - `Locales/en.json` — **all in-game text is English** (other languages fall back to `en`).
- `docs/` — `CAPABILITIES.md` (what the mod API allows, with decompiled citations) + `DESIGN.md`.
- `decompiled/` — gitignored ILSpy dump of the game DLLs (the API source of truth). Regenerate with
  `ilspycmd` fetched as a NuGet tool and run on the installed .NET 8 runtime (no SDK needed).

## Build & test
Unity → **Big Ambitions → Mod Builder → Build + Install** (installs to the game's `ModsLocal`).
Import the game DLLs first via the Welcome window. **Fully restart the game** after installing so
mod locales load. Logs: `%LocalLow%/Hovgaard Games/Big Ambitions/Player.log` — search `[SiliconAlley]`.
To staff a business in-game: place a **Computer Workstation** and hire **Programmers from City
Workforce Inc.** (other agencies don't offer them).

## Conventions & gotchas (hard-won)
- **All in-game text is English** in `en.json`; do **not** add a `nl.json`.
- The ModManifest's **LocalesFolder must reference THIS mod's `Locales`** — a wrong GUID silently
  packaged the example mod's locale (the original "names show raw keys" bug).
- The custom simulator is **assigned in code** (`ScriptableObject.CreateInstance` in `OnLoadAsync`),
  not shipped as a bundled ScriptableObject.
- **No loose files in the mod folder** — the packager sweeps them into the AssetBundle. Keep docs in
  top-level `docs/`, decompiled code in top-level `decompiled/`.
- Use **`CultureInfo.InvariantCulture`** for any float serialization (dev machine is nl-NL).
- The **decompiled code is the source of truth** — verify every game type/method/enum there; never
  assume an API.
- **Not moddable** (confirmed): new employee skills, new building types, custom Factory recipes, AI
  running modded businesses. Reuse existing game systems instead.

## Save Compatibility Policy (ENFORCED — overrides convenience)

Subscribers install new mod versions **on top of existing savegames**. A save must NEVER break because
of a mod update. These are hard rules, not guidelines: **check every one of them on every change**, even
when a shortcut looks harmless. They override convenience. If a change cannot satisfy them, it must be
done via migration (below) or not at all.

### 1. Persisted state (`GameInstance.modData`) is forward-compatible only
- The serialized blob carries an explicit **schema version** (`~schema|<n>` header in the
  `"SiliconAlley"` value; see `SiliconAlleyState.Serialize/LoadFrom`). A save **without** the header is
  the **v1 baseline** — do not treat its absence as an error.
- Loading must handle **every shipped version**: missing fields **default** to sensible values;
  unknown/extra fields (and unknown `~`-headers) are **ignored**; deserialization of **each record is
  wrapped in try/catch** so one bad/old entry is defaulted/skipped instead of crashing the save load.
- **Never change the meaning or format of an existing key/field.** To change semantics, add a **NEW**
  key/field, bump `CurrentSchemaVersion`, and add an explicit old→new step in `Migrate()`.
- When adding new per-project/per-business state, **default it for old saves** to a value that maps the
  old state sensibly (e.g. an in-flight project with accrued progress ⇒ `ProjectKind.Standard`, as
  `EnsureProjectTypeLocked` already does — don't suddenly rescale a legacy project).
- Simple flags follow the same rule by being absence-tolerant, e.g. `SiliconAlley.ClientWelcomeSent`
  (absent ⇒ "not sent"). `PlayerPrefs`/options are machine-local and out of scope (not in the save).

### 2. Enum / identifier values are append-only
- Once an enum value or registered identifier has **shipped in a release**, **never rename, remove, or
  reorder it**. Saves reference content by its hashed/string value and persisted enum **ordinals**, so
  renaming/renumbering breaks them. Deprecate by leaving it in place and not using it for new content.
- This covers: `businessTypeName`, `itemName`, the `ModEnumHash.GetSafeHash(...)` string, and any
  **persisted enum ordinal** (`ProjectKind {Quick=0,Standard=1,Ambitious=2}`). Derived, non-persisted
  enums (`ProjectPhase`, computed from `Progress`) are exempt.
- **Display names / localization may change freely**; the underlying identifier/ordinal may NOT.

### 3. Release gate (run before anything ships)
Verify the change does **not**:
- (a) rename / remove / reorder a shipped enum value or identifier (§2),
- (b) change an existing `modData` key's format or meaning (§1),
- (c) remove registered content (business type / item) a save could reference.

If a change requires any of (a)–(c), it **must** be handled via a schema bump + `Migrate()` step (and the
ledger below updated), or the change must be avoided.

### SHIPPED_ENUMS — append-only ledger (immutable once listed)

**Current save schema version: `1`.** Add to this list when new content ships; never edit or remove a
line that has shipped.

- **Business types** (`businessTypeName`):
  - `siliconalley:businesstype_softwarestudio`
  - `siliconalley:businesstype_cybersecurity`
  - `siliconalley:businesstype_gamestudio`
- **Items** (`itemName`):
  - `siliconalley:itemname_softwarelicense`
  - `siliconalley:itemname_securityaudit`
  - `siliconalley:itemname_videogame`
- **CallDialogType** (minted via `ModEnumHash.GetSafeHash`): `siliconalley_clientdialog`
- **Publisher ids** (epic #17 / #22; `SiliconAlleyPublishers.Roster` — the persisted token is the **array
  ordinal**, APPEND-ONLY: never rename/reorder/remove, add new publishers only by appending):
  - `0` `siliconalley:publisher_indielabel` (focus Any, tier 1)
  - `1` `siliconalley:publisher_pixelforge` (focus Games, tier 2)
  - `2` `siliconalley:publisher_officeworks` (focus Office, tier 2)
  - `3` `siliconalley:publisher_sentinel` (focus Security, tier 2)
  - (`PublisherFocus { Any, Games, Office, Security }` is NOT persisted — only the publisher ordinal +
  the deal fields are; the enum is free to change.)
- **Persisted enum ordinals** (inside the `"SiliconAlley"` blob): `ProjectKind { Quick=0, Standard=1,
  Ambitious=2 }`; publisher ordinals (see above) persisted as the active-deal `dealPublisher`; the
  design-wizard `segmentId` ordinal + `featureMask`/`platformMask`/`ownedToolsMask`/`usedToolsMask` bitmasks
  (per-bit/ordinal = the reserved enum families directly below).
- **Design-wizard reserved enum families** (epic #34 / #40; APPEND-ONLY — once a bit/ordinal ships, never
  rename/reorder/remove; add new members only by appending). The bit/ordinal members are minted by the owning
  sibling when it needs them for gameplay; #40 reserves the family names (and `SegmentId`'s ordinals). The
  bitmask/ordinal int is the persisted token, so each per-bit assignment is load-bearing:
  - `FeatureId` — per business type; bit positions in `featureMask` (#26, **SHIPPED** — `SiliconAlleyFeatures`,
    APPEND-ONLY by `Bit`, never reorder/remove). Office (`softwarestudio`): `0` Cloud Sync · `1` Plugin API ·
    `2` Collaboration Suite · `3` Automation Scripting · `4` Enterprise SSO. Security (`cybersecurity`): `0` Threat
    Intel Feed · `1` Compliance Reporting · `2` Automated Pen-Testing · `3` Zero-Trust Module · `4` Incident
    Response. Game (`gamestudio`): `0` Advanced Graphics · `1` Physics Engine · `2` Multiplayer Netcode ·
    `3` Procedural Generation · `4` Mod Support. (Display names live in `en.json` and may change freely.)
  - `ToolId` — per business type; bit positions in `ownedToolsMask` / `usedToolsMask` (#36)
  - `PlatformId` — per business type; bit positions in `platformMask` (#37, **SHIPPED** — `SiliconAlleyPlatforms`,
    APPEND-ONLY by `Bit`). Office (`softwarestudio`): `0` Desktop · `1` Web · `2` Mobile · `3` Cloud/SaaS.
    Security (`cybersecurity`): `0` Desktop · `1` Server · `2` Cloud · `3` Mobile. Game (`gamestudio`): `0` PC ·
    `1` Console · `2` Mobile · `3` Web. (Share weight + scope cost are tunable data, NOT persisted; names in `en.json`.)
  - `SegmentId` — value of `segmentId` (#38): `0=Broad, 1=Enterprise, 2=Prosumer, 3=Consumer`
- **modData keys:** `SiliconAlley` (versioned state blob), `SiliconAlley.ClientWelcomeSent` (bool flag)
- **Reserved `"SiliconAlley"` blob headers** (`~`-prefixed, position-independent, unknown ones ignored for
  forward-compat): `~schema|<n>`, `~global|<projectTypeIndex>`, and `~publishers|r0,r1,…` — the player's
  per-publisher reputation (epic #22), comma-separated by publisher ordinal (append-only; absent ⇒ all 0).
- **Appended `"SiliconAlley"` per-building fields** (trailing, schema v1, absent ⇒ default; never reorder):
  `…|overtime|hold` then `|bugCount|awareness|hype|adSpend` — go-to-market loop (epic #16: bugs #19;
  marketing awareness/hype/ad-spend #21) — then `|supportFreshDay|version|ipReputation` — product lifecycle
  (epic #18: aging #25 supportFreshDay; sequels/IP #24 version + ipReputation) — then
  `|dealPublisher|dealDeadlineDay|dealPayout` — publisher deals (epic #17 / #23). Go-to-market fields default
  `0` ⇒ a legacy launch has no bugs/awareness and adds exactly **+1** installed. Lifecycle defaults: `supportFreshDay
  0` ⇒ the catalog anchors to **full freshness** on first support accrual (no retroactive decay); `version`
  defaults **1** (a debut, so no sequel bonus); `ipReputation 0`. Deal defaults: `dealPublisher` **-1**
  (NOT 0 — 0 is a valid publisher ordinal) ⇒ no active deal ⇒ old saves ship freely with no deadline;
  `dealDeadlineDay`/`dealPayout` 0. The review score #20, the launch jump and the support age-factor are
  derived at ship/tick; review is stored only in the transient ship-report snapshot, not persisted. Then the
  **design-wizard block** (epic #34, **frozen order reserved up front by #40** before any sibling ships):
  `|featureMask|platformMask|ownedToolsMask|usedToolsMask|segmentId` — all `int`, all default `0`
  (absent ⇒ default ⇒ legacy unchanged). `featureMask 0` ⇒ no extra features (scope ×1.0, quality ceiling
  unchanged) — **#26 SHIPPED**: real bits = `FeatureId`, each set bit raises `EffectiveProjectSize` and the
  design quality ceiling; **per-project** (reset to `0` in `OnProjectCompleted`); `platformMask 0` ⇒ a single
  "home" platform (reach ×1.0, scope ×1.0 — the no-op is **1 platform, never 0 reach**) — **#37 SHIPPED**: real
  bits = `PlatformId`, each raises `EffectiveProjectSize` (porting) and the launch reach (Σ share weights, on the
  installed-base jump only — `MarketFactor`/payout untouched); **per-project** (reset in `OnProjectCompleted`);
  `ownedToolsMask`/`usedToolsMask 0` ⇒ no owned/licensed tools
  (`ownedToolsMask` is **studio-level** and survives `OnProjectCompleted`; `usedToolsMask` is **per-project**
  and resets on completion, #36); `segmentId 0` = Broad ⇒ segment factor ×1.0 (#38). #40 reserves these as
  no-ops in code (`SiliconAlleyState` BusinessState + Serialize/LoadFrom); each sibling fills in ITS field's
  gameplay at ITS reserved index and **must hold the frozen positions** — write the neutral `0` for any
  earlier reserved slot not yet implemented, and read each with a `parts.Length > <index>` guard inside the
  existing per-record `try/catch` (`InvariantCulture` for any non-int parse). **Every sibling PR
  (#26 / #36 / #37 / #38 / #39) claims its reserved slot and reviewers gate on this order** (#39 Dependencies
  appends no field but must not disturb it). Pure trailing append throughout (go-to-market, lifecycle, deals,
  and this wizard block) ⇒ **no schema bump**, no new `~`-header.
- **BusinessRequirement assets** reference **base-game** ids (not ours, also immutable):
  `DesktopWorkstation` → `ba:itemname_itemgroupdesktopworkstation`, `BathroomStall` →
  `ba:itemname_toiletstall`, `Sink` → `ba:itemname_sink`.

> Ledger lives here (not in a mod-folder file) on purpose: the packager sweeps loose files in
> `Assets/Mods/SiliconAlley/` into the AssetBundle, and a functional `Enums.txt` could alter
> build/registration. `CLAUDE.md` is where future sessions look and is the single source of truth.
