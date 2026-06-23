# Credits & third-party asset attribution — Silicon Alley mod

Attribution for third-party art bundled with the **Silicon Alley** mod. Tracked in git (the `docs/`
folder is gitignored, so it can't live there). When you upload the mod, also surface these credits in the
Steam Workshop description.

## UI icon set (issue #55)

The mod resolves a per-concept icon for every feature / tool / platform / segment / phase / business type
/ scope from `Assets/Mods/SiliconAlley/UI/Icons/` (loaded at runtime by `SiliconAlleyTheme`). The icon
file name is the concept's `NameKey` minus the `siliconalley:` prefix (e.g. `feature_office_cloudsync.png`);
a missing icon falls back to a per-category placeholder, then to no icon.

### Concept icons — game-icons.net (CC BY 3.0)

The bundled per-concept icons are from **[game-icons.net](https://game-icons.net)**, licensed under
**[CC BY 3.0](https://creativecommons.org/licenses/by/3.0/)**. They were recolored to white-on-transparent
and rasterized to 128px (the mod tints them to the theme). Credit by author:

- **Lorc** — https://lorc.itch.io — _processor, conversation, gears, radar-sweep, cracked-shield, padlock,
  maze, magnifying-glass, cog, world, medal, hammer-nails, test-tubes, rocket, lightning-arc, mountains,
  fluffy-cloud._
- **Delapouite** — https://delapouite.com — _cyber-eye, game-console, cloud-upload, puzzle, id-card,
  checklist, sparkles, spring, share, stack, database, window, control-tower, palette, smartphone,
  public-speaker, bank, shopping-cart, pencil-ruler, speedometer._
- **Skoll** (game-icons.net) — _siren, combination-lock, sound-waves, pc._
- **Sbed** (game-icons.net) — _wrench._

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

</details>

### Category placeholders — _no attribution required_

`cat_feature`, `cat_tool`, `cat_platform`, `cat_segment`, `cat_phase`, `cat_businesstype`,
`cat_projecttype` are **procedurally generated** by
`Assets/Editor/SiliconAlleyUI/SiliconAlleyIconPlaceholderGenerator.cs` (own work — public-domain geometric
glyphs). They are the fallback tier when a concept has no dedicated icon.
