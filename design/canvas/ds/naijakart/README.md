Naija Kart is a premium Nigerian multiplayer kart racer. The look is **bright, sunlit Lagos rendered in polished stylized 3D, with a chunky arcade HUD on top**: navy glass plates, white outlined lettering, danfo yellow for you and your next action. **The race is the star; the HUD frames it and never covers it.** Visual statement: *Nigerian chaos, rendered beautifully.*

This system is the source of truth for every screen and for the Unity implementation. It follows Naija Kart Spec v1.1 (four-tab navigation, Tilt and Touch controls, the enforcer system, AI disclosure, Tier 1 baseline) and the in-race key art.

## Principles

1. **The world is bright; the HUD is frosted glass.** The 3D scene carries the colour (sky, danfo buses, billboards, lagoon). In a race, every HUD plate is frosted navy glass (`GlassPlate`) so the road stays visible through it; menus use solid `plate-*` navy.
2. **Arcade lettering on the world.** Any display text placed over the 3D scene is white `ink` in Lilita One with a dark `outline` stroke and a hard drop (`nk-outline`). This is what makes it read as a game, not an app.
3. **Danfo means you and your next move.** `danfo` marks the one primary action, your row in standings and leaderboards, and the active tab. Nothing else on a screen is yellow.
4. **Colour is a role.** Each gameplay colour has one job (table below). Item colours appear only in item slots; speed colours only in the speedometer.
5. **Never colour alone.** Every state also has a word, icon or shape: item rims have name labels, ready rings say Ready, drift stages change ring weight.
6. **Physical, not glossy.** Pressable things and HUD plates have a hard bottom edge (`edge-danfo`, `edge-plate`). No soft gradients on chrome, no blur.
7. **Nigerian through situation and language.** Danfo, keke, okada, LASTMA, Pidgin reactions, Lagos signage. No flags, no green-and-white palette.

## Colour

Single dark theme (`game`): the UI is navy plates over a bright world.

| Role | Token | Use it for | Never for |
| --- | --- | --- | --- |
| Ground | `plate-900` | Menu backdrop, Tier 1 HUD fallback | |
| Surface | `plate-800` | Cards, sheets, nav rail | |
| Raised | `plate-700` | Rows, tracks, secondary buttons | |
| Line | `plate-600` | Borders, dividers, empty segments | Text |
| HUD glass | `plate-glass` + `blur-glass`, `glass-edge`, `glass-sheen`; fallback `plate-glass-solid` | Every HUD plate in a race | Menus (use solid plates) |
| Outline | `outline` | Stroke and drop under white display text | Fills |
| Text | `ink`, `ink-muted` | Primary and secondary text | |
| Primary / you | `danfo` + `on-danfo` | The one action, your row, active tab | Warnings, decoration |
| Social | `lagoon` + `on-lagoon` | Friends, Online, Challenge, party, bail | Gameplay |
| Boost | `boost` + `on-boost` | Boost button, boosts held, BOOST! | Anything else |
| Items | `item-boost`, `item-attack`, `item-hazard`, `item-trap` | Item slot rims by type | Anything else |
| Drift | `drift-1`, `drift-2`, `drift-3` | Drift charge stages | Anything else |
| Speed | `speed-1` to `speed-5` | Speedometer arc | Anything else |
| Threat | `enforcer` (alias `danger`) | Enforcer chase, hazards aimed at you, errors | Celebration |
| Good | `success` | Escaped, complete, rank up, ready | Primary buttons |
| Caution | `warning` | Network, storage, data prompts | Buttons |
| Rank | `rank-*` + `on-rank` | Rank badges and names only | Anything else |

Contrast: on glass, contrast depends on the scene, so all text on glass carries the outline treatment. On solid plates, `ink` is at least 12:1 and `ink-muted` at least 6.5:1 on every navy plate. Every `on-*` pair is at least 5.9:1. `ink-faint` is for text 19px or larger only.

## Typography

Two Google Fonts, loaded by `components/bundle.css`:

- **Lilita One** (`--font-display`): the arcade face. Heavy, rounded and friendly, the style used across mobile arcade and party racers. Use it for everything read at speed: position, lap, timer, speed, callouts, CTAs, titles, rank names. It has one weight; never apply bold (faux bold smears it). Uppercase for HUD and CTAs; title case is fine for character names.
- **Nunito** (`--font-text`) at 700 to 900: a rounded sans that matches Lilita's softness for sentences, standings names, item labels, notifications and settings. Sentence case.

Rules:
- Text over the 3D world: Lilita One, white, with `nk-outline` (2px outline, 5px hard drop) or `nk-outline--lg` (4px outline, 8px drop) at 72px and up. The outline is built from text shadows so it renders on every device; in Unity use a TextMeshPro outline plus underlay with the same values.
- Text on a solid plate needs no outline.
- Numerals use `font-variant-numeric: tabular-nums` where the font supports it; keep positions and timers in fixed-width boxes so they never jitter.
- Use `num-*` styles for numbers, `display-*` and `heading-*` for words. Body never below `body-s` (13px); `caption` (12px) for item names and chips only.

## Spacing, size and layout

- 4px base: `space-1` to `space-8`.
- **Safe areas:** everything at least `safe-inset` (24px) from every edge, plus device safe areas.
- **Touch targets:** at least `touch-min` (48px); primary CTA `button-lg` (72px); race buttons `hud-button` (88px) and `hud-button-drift` (112px).
- **Menus (landscape):** `NavRail` left, identity top-left, currency chips top-right, RACE NOW bottom-right under the right thumb.
- **Race HUD:** position and standings top-left, lap and timer top-right, minimap and portrait callout bottom-left, speedometer and items bottom-right, transient callouts upper-centre. The middle of the screen stays empty. See `HudStatus`.

## Shape and depth

- Radii: `radius-sm` standings rows and chips, `radius-md` buttons and HUD plates, `radius-lg` cards, `radius-pill` items, avatars, minimap and speedometer.
- Depth is a hard edge (`edge-danfo`, `edge-plate`). `lift` only on Tier 2 and 3 sheets.
- The speedometer arc and item cooldown sweep are the only multi-colour treatments in UI.

## Motion

`duration-fast` (120ms) for presses, items, +1 and boost; `duration-medium` (240ms) for cards, sheets and standings reorder; `duration-slow` (600ms) for rank-ups and results. `ease-snap` gives the arcade overshoot to callouts, badges and rewards; `ease-out` for arrivals; `ease-in` for exits. No slow motion during a race. Respect reduce-motion by dropping overshoot.

## Copy

Follow the Copy Guide (Spec v1.1, section 9):

- **Pidgin** for race reactions and banter: "OMO! DEM DEY COME!", "YOU DON PASS AM!", "LAST LAP. NO SHAKING.", "RUN AM BACK".
- **Plain English** for money, data, consent, settings and error details.
- In-race lines are 4 words or fewer per line.
- Item names are playful Nigerian English: Jollof Boost, Pure Water Attack, Tire Spike, Banana Trap.
- Enforcer copy comes from city copy keys, never hard-coded.

## Iconography

Bold, rounded, filled icons with a dark outline, like stickers: readable at 24px in menus and 38px in item slots. Item icons are full colour (the chili, the pure water sachet, the spiked tire, the banana). UI icons (home, garage, social, profile, drift, boost, horn) are single-colour in `ink` or the matching `on-*` token. The icons in previews are placeholders drawn for this system; the final set comes from the art team.

## Rank identity

Seven tiers, each with a `rank-*` colour: JJC, Sharp Sharp, No Dull, Sabi, Odogwu, Chairman, Oga Patapata. Use `RankBadge` wherever rank appears. Names are pending validation with Nigerian players and are never translated.

## Components

Actions: `Button`. Identity: `CurrencyChip`, `Tag`, `RankBadge`. Progress: `ProgressBar`, `StatMeter`. Race entry: `ModeCard`. Social: `PlayerCard`, `LeaderboardRow`, `NotificationCard`. Navigation: `NavRail`. Race HUD: `HudStatus` (full layout), `GlassPlate`, `RaceStandings`, `Speedometer`, `ItemSlot`, `HudControls`, `RaceCallout`, `PortraitCallout`, `EnforcerMeter`.

Components are CSS classes prefixed `nk-` in `components/bundle.css`. For Unity, map each token to a shared style asset and each component to a prefab with the same variant names. Lilita One and Nunito are both under the SIL Open Font License, so they can ship inside the game.

## Device tiers

Design for Tier 1 first: Tier 1 swaps glass to `plate-glass-solid` with no blur (`nk-tier1`); no `lift`; pre-rendered stills behind menus; static speedometer segments. Tiers 2 and 3 may add live 3D, `lift`, glows and richer motion, never new information.
