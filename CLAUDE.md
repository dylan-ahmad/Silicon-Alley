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
