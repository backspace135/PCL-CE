# horizon-core

Horizon's portable core crates, split out of the Horizon repository (`backspace135/horizon` @ `85b5bcca7c`).
They hold vanilla Minecraft: Java Edition 26.3's rules and data with no window, GPU, sound, gamepad,
clipboard or HTTP client in them, so they build on any target.

| Crate | Owns |
|---|---|
| `horizon-core` | Simulation primitives: Java `Random`s, `jmath` (HotSpot-exact `Math`), positions, noise, density, paletted storage, the pinned version |
| `horizon-world` | World data with no gameplay loop: NBT, regions, chunks, `datafix/`, `worldgen/`, `level/` (block behaviour, ticks, redstone), entity physics, effects, food, damage |
| `horizon-protocol` | Network protocol 777: codecs, framing, encryption, packets |
| `horizon-items` | Blocks and items: block facts, item stacks and components, enchantments, cooldowns, bundles |
| `horizon-command` | Command parsing (Brigadier): the vanilla tree, argument types, selectors, suggestions |
| `horizon-client-core` | Text, languages, SNBT, NBT paths, components, scoreboard, boss bars, game rules |
| `horizon-assets` | Resource decoding: block-state and item models, baking, atlases |
| `horizon-threads`, `horizon-guest-stack` | Worker scheduling, sampling and stack reservations |
| `horizon-java-properties` | Java-properties files |
| `horizon-mod-runtime` | Engine-independent mod storage and attachment indexes (`horizon-protocol` uses it) |

## Differences from the Horizon repository

- `horizon-items` reads the enabled-mods snapshot (`horizon-enabled-mods.json`) with its own
  `enabled_mods` module instead of depending on `horizon-mod-package`, whose build pulls in the mod
  API generator and the game's sources. The file format is the same.
- `horizon-client-core` embeds the translation files from `crates/horizon-client-core/assets/horizon/lang/`
  (in Horizon: `crates/horizon-client/assets/horizon/lang/`).
- `horizon-guest-stack`'s worker audit checks only `horizon-threads`; the game's workers are audited in Horizon.
- `horizon-client-core`'s lang key test checks the `horizon.*` keys named in its own sources; Horizon checks the
  game's client crates.

## Build and test

```sh
cargo check --workspace --all-targets
cargo test --workspace
```

Tests that need vanilla data look for it in `.local/horizon-client/26.3` (prepared from your own
Minecraft install with Horizon's `horizon-prepare`). Never commit Mojang's files.

## License

LGPL-3.0-or-later, see [LICENSE](LICENSE).
