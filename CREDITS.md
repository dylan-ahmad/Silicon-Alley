# Credits & third-party asset attribution — Silicon Alley mod

Attribution for third-party art bundled with the **Silicon Alley** mod. Tracked in git (the `docs/`
folder is gitignored, so it can't live there). When you upload the mod, also surface these credits in the
Steam Workshop description.

## UI icon set (issue #55)

The mod resolves a per-concept icon for every feature / tool / platform / segment / phase / business type
/ scope / stat / milestone event / publisher / product dependency / server role from
`Assets/Mods/SiliconAlley/UI/Icons/` (loaded at runtime by `SiliconAlleyTheme`). The icon
file name is the concept's `NameKey` minus the `siliconalley:` prefix (e.g. `feature_office_cloudsync.png`);
a missing icon falls back to a per-category placeholder, then to no icon.

### Concept icons — game-icons.net (CC BY 3.0)

The bundled per-concept icons are from **[game-icons.net](https://game-icons.net)**, licensed under
**[CC BY 3.0](https://creativecommons.org/licenses/by/3.0/)**. They were recolored to white-on-transparent
and rasterized to 128px (the mod tints them to the theme). Credit by author:

- **Lorc** — https://lorc.itch.io — _processor, conversation, gears, radar-sweep, cracked-shield, padlock,
  maze, magnifying-glass, cog, world, medal, hammer-nails, test-tubes, rocket, lightning-arc, mountains,
  fluffy-cloud, hourglass, laurels, cubes, on-target, cracked-disc, scarab-beetle, sticking-plaster,
  rocket-flight, anvil, guarded-tower, circuitry, locked-fortress, key, radar-dish, gear-hammer,
  linked-rings._
- **Delapouite** — https://delapouite.com — _cyber-eye, game-console, cloud-upload, puzzle, id-card,
  checklist, sparkles, spring, share, stack, database, window, control-tower, palette, smartphone,
  public-speaker, bank, shopping-cart, pencil-ruler, speedometer, round-star, check-mark, coins,
  receive-money, save-arrow, expand, plug, podium, megaphone, compact-disc, briefcase, brick-wall,
  cloud-download, gamepad, server-rack, factory-arm, radio-tower, plain-arrow._
- **Skoll** (game-icons.net) — _siren, combination-lock, sound-waves, pc._
- **Sbed** (game-icons.net) — _wrench._

Summary review-card stat icons (issue #58): `stat_quality`=delapouite/round-star,
`stat_coverage`=delapouite/check-mark, `stat_cost`=delapouite/coins, `stat_royalty`=delapouite/receive-money,
`stat_market`=lorc/world.

Icon-pack completion (issue #145): every remaining requested stem — the ETA/reputation/installed stat
rows, the eight milestone decision events, the four publishers, the nine product dependencies and the
three server roles — plus `stat_components` (lorc/cubes) and `stat_fit` (lorc/on-target), which replace
the borrowed category placeholders on the wizard summary. Authored via `tools/generate-icons.ps1` from
`tools/icon-manifest.txt` (the reproducible pipeline this issue introduced).

UI component glyphs (issue #146): `ui_check` (delapouite/check-mark — the checkbox tick) and
`ui_chevron_down` (delapouite/plain-arrow — the collapsible-section chevron), same pipeline.

All of the above are available at game-icons.net under CC BY 3.0.

<details><summary>Per-file mapping (concept icon → game-icons source)</summary>

| icon file | game-icons.net source |
|---|---|
| `businesstype_softwarestudio` | lorc/processor |
| `businesstype_cybersecurity` | delapouite/cyber-eye |
| `businesstype_gamestudio` | delapouite/game-console |
| `feature_office_cloudsync` | delapouite/cloud-upload |
| `feature_office_pluginapi` | delapouite/puzzle |
| `feature_office_collab` | lorc/conversation |
| `feature_office_automation` | lorc/gears |
| `feature_office_enterprise` | delapouite/id-card |
| `feature_security_threatfeed` | lorc/radar-sweep |
| `feature_security_compliance` | delapouite/checklist |
| `feature_security_pentest` | lorc/cracked-shield |
| `feature_security_zerotrust` | lorc/padlock |
| `feature_security_incident` | skoll/siren |
| `feature_game_graphics` | delapouite/sparkles |
| `feature_game_physics` | delapouite/spring |
| `feature_game_multiplayer` | delapouite/share |
| `feature_game_procedural` | lorc/maze |
| `feature_game_modsupport` | sbed/wrench |
| `tool_office_appframework` | delapouite/stack |
| `tool_office_database` | delapouite/database |
| `tool_office_uitoolkit` | delapouite/window |
| `tool_security_scanengine` | lorc/magnifying-glass |
| `tool_security_cryptolib` | skoll/combination-lock |
| `tool_security_siem` | delapouite/control-tower |
| `tool_game_engine` | lorc/cog |
| `tool_game_artsuite` | delapouite/palette |
| `tool_game_audio` | skoll/sound-waves |
| `platform_*_desktop`, `platform_game_pc` | skoll/pc |
| `platform_*_web` | lorc/world |
| `platform_*_mobile` | delapouite/smartphone |
| `platform_*_cloud` | lorc/fluffy-cloud |
| `platform_security_server` | delapouite/database |
| `platform_game_console` | delapouite/game-console |
| `segment_broad` | delapouite/public-speaker |
| `segment_enterprise` | delapouite/bank |
| `segment_prosumer` | lorc/medal |
| `segment_consumer` | delapouite/shopping-cart |
| `phase_design` | delapouite/pencil-ruler |
| `phase_development` | lorc/hammer-nails |
| `phase_testing` | lorc/test-tubes |
| `phase_release` | lorc/rocket |
| `projecttype_quick` | lorc/lightning-arc |
| `projecttype_standard` | delapouite/speedometer |
| `projecttype_ambitious` | lorc/mountains |
| `stat_eta` | lorc/hourglass |
| `stat_reputation` | lorc/laurels |
| `stat_installed` | delapouite/save-arrow |
| `stat_components` | lorc/cubes |
| `stat_fit` | lorc/on-target |
| `ms_scope_creep` | delapouite/expand |
| `ms_middleware` | delapouite/plug |
| `ms_tech_debt` | lorc/cracked-disc |
| `ms_conference` | delapouite/podium |
| `ms_beta_call` | delapouite/megaphone |
| `ms_crunch_qa` | lorc/scarab-beetle |
| `ms_go_gold` | delapouite/compact-disc |
| `ms_dayone_patch` | lorc/sticking-plaster |
| `publisher_indielabel` | lorc/rocket-flight |
| `publisher_pixelforge` | lorc/anvil |
| `publisher_officeworks` | delapouite/briefcase |
| `publisher_sentinel` | lorc/guarded-tower |
| `dep_office_osruntime` | lorc/circuitry |
| `dep_office_appframework` | delapouite/brick-wall |
| `dep_office_cloudbackend` | delapouite/cloud-download |
| `dep_security_hardenedos` | lorc/locked-fortress |
| `dep_security_crypto` | lorc/key |
| `dep_security_threatintel` | lorc/radar-dish |
| `dep_game_runtimeos` | delapouite/gamepad |
| `dep_game_framework` | lorc/gear-hammer |
| `dep_game_onlinesdk` | lorc/linked-rings |
| `server_role_infrastructure` | delapouite/server-rack |
| `server_role_backend` | delapouite/factory-arm |
| `server_role_hosting` | delapouite/radio-tower |
| `ui_check` | delapouite/check-mark |
| `ui_chevron_down` | delapouite/plain-arrow |

</details>

### Category placeholders — _no attribution required_

`cat_feature`, `cat_tool`, `cat_platform`, `cat_segment`, `cat_phase`, `cat_businesstype`,
`cat_projecttype`, `cat_stat`, `cat_ms`, `cat_publisher`, `cat_dep`, `cat_server` are **procedurally
generated** by `Assets/Editor/SiliconAlleyUI/SiliconAlleyIconPlaceholderGenerator.cs` (own work —
public-domain geometric glyphs). They are the fallback tier when a concept has no dedicated icon.
