# NAIJA KART — Comprehensive Product Requirements & Game Build Specification

**Version:** 1.0 · **Status:** Build Specification · **Platform:** iOS + Android · **Orientation:** Landscape
**Engine:** Unity · **Language:** C# · **Primary Development Agent:** Claude Code
**Game Type:** Multiplayer mobile arcade racing · **Target Players:** Nigerian and African mobile gamers, with global accessibility
**Core Multiplayer:** Up to 8 players · **Race Duration:** 3 to 5 minutes

> This file is the product contract (PRD §105). It is reproduced from the original specification.
> Engineering status against it lives in `VERTICAL_SLICE.md` and `ROADMAP.md`.

## 1. Product vision
Naija Kart is a polished, stylized 3D Nigerian multiplayer arcade racing game where players compete against friends and other players through chaotic Nigerian environments. It combines fast arcade racing, tilt-based mobile controls, drifting and boosting, items and power-ups, Nigerian road chaos, multiplayer competition, rivalries, progression, customization, rankings, tournaments, seasons and social sharing. The game must feel like a **real racing game first** and a Nigerian game second; the Nigerian identity should fundamentally influence gameplay rather than simply changing the visual skin.

**Core principle: Don't Nigerianize the skin. Nigerianize the gameplay.**

## 2. Product thesis
The fundamental kart-racing loop is powerful: Race → compete → win/lose → progress → customize → challenge → race again. Naija Kart differentiates by making the environment, events, items, vehicles, enforcement systems, social language, competition and progression distinctly Nigerian. Intended experience: *"I'm racing my friends through Nigeria, trying to outdrive them, outsmart them, survive the chaos and become the biggest Naija Kart champion."*

## 3. Design north star
1. **Immediately fun** — a new player understands the basic game within the first race.
2. **Skill matters** — winning depends substantially on steering, drifting, track knowledge, boost timing, item timing, positioning, risk management, reaction speed.
3. **Chaos creates stories** — LASTMA catches you in the final lap; your friend bails you out; you hit someone with Pure Water; you take a risky shortcut; an Okada crosses the track; you recover from 7th and win; someone steals your item; you escape LASTMA with one second remaining.
4. **Social competition creates retention** — players constantly have reasons to compete with specific people.
5. **Nigerian identity must be authentic** — recognizable from roads, vehicles, infrastructure, traffic, weather, architecture, signage, sounds, language, characters, events and gameplay systems.

## 4. Target experience
First race: "This is actually fun." Several races: "I can beat these guys." A few days: "I need to beat my friend." A week: "I want to rank up." A month: "I'm known for this."

## 5. Target retention loop
PLAY → RACE → CHAOS / COMPETITION / STORY → WIN / LOSE → EARN XP + COINS + RANK PROGRESS → UNLOCK / CUSTOMIZE / IMPROVE → BUILD STATUS → CHALLENGE FRIENDS → RIVALRY → REMATCH → TOURNAMENT → SEASON → NEW CONTENT / EVENTS → RACE AGAIN. The social layer surrounds this loop: FRIENDS ↔ RIVALRIES ↔ CHALLENGES ↔ SHARING ↔ TOURNAMENTS.

## 6. Quick Race
Up to 8 players, matchmaking, 3 laps, random or selected available track, XP rewards, Coin rewards, no significant rank penalty, rematch available. Purpose: low-pressure competition, learning, social play, daily activity.

## 7. Ranked Race
Up to 8 players, skill-based matchmaking, ranked rating, Nigerian rank tiers, leaderboards, match history, rank progression, competitive statistics. Initial rank names: 1 Newbie, 2 Sharp Guy, 3 Correct Guy, 4 No Dull, 5 Omo!, 6 Na You Sabi, 7 Odogwu, 8 Chairman, 9 Wahala Dey, 10 Untouchable. Thresholds are configurable from server-side data. **Do not hardcode rank thresholds into gameplay code.**

## 8. Private Room
Invite friends, up to 8 players, select track, random track, configure laps, enable/disable items, optional special rules, room code, shareable invite, WhatsApp-friendly invitation ("Tife created a Naija Kart room. Join am.").

## 9. Friend Challenge
Formats: 1v1, Best of 3, Best of 5. Special UI: **RUN AM BACK** after a race. Persistent rivalry information: wins, losses, best track, fastest time, last winner, current streak (e.g. TIFE vs CHUKS — Tife 7 wins, Chuks 5 wins, current streak Tife +2).

## 10. Tournaments
Daily Cups, Weekend Cups, City Championships, Special Events, Seasonal Championships (e.g. LAGOS CHAMPIONSHIP, 2048 players: 2048→512→128→32→8→4→Champion). Rewards may include Coins, XP, cosmetics, titles, badges, exclusive vehicles, exclusive character cosmetics.

## 11. Season system
Seasons last approximately 6 to 8 weeks (Season 1 Lagos, 2 Abuja, 3 Port Harcourt, 4 Ibadan). Each season can introduce a new track, character, vehicle, cosmetics, events, challenges, tournament, seasonal leaderboard, limited-time items, themed progression.

## 12. Core race flow
HOME → SELECT MODE → MATCHMAKING → PLAYER LOBBY → CHARACTER / VEHICLE → COUNTDOWN → RACE → FINAL LAP → FINISH → RESULTS → REWARDS → RANK / XP / COINS → REMATCH / HOME.

## 13. Race state machine
Explicit states: Waiting → Lobby → Matchmaking → Loading → Countdown → Racing → FinalLap → Finish → Results → Rematch / Lobby / Home. Exceptional states: Disconnected, Reconnecting, ServerError, PlayerEliminated, RaceCancelled, Timeout. **Race state must be authoritative on the server.**

## 14. Race structure
Default 8 players, 3 laps, 3 to 5 minutes, checkpoints, lap validation, finish order, position tracking. The system must prevent checkpoint skipping, invalid lap completion, teleport-based cheating, impossible speeds, duplicate rewards, client-side result manipulation.

## 15. Mobile control system — PRIMARY CONTROL MODEL: TILT
Tilt is the default control system. The player holds the phone horizontally: tilt left → steer left, tilt right → steer right. The vehicle automatically accelerates; the player does not need to hold an accelerator.

## 16. On-screen controls
Left/right: phone movement. Drift: dedicated button. Item: dedicated button. Boost: dedicated button when boost is available. Look Back: optional. Horn: optional. The screen should remain visually clean; controls must not obscure important race information.

## 17. Tilt calibration
Support calibration, sensitivity, steering dead zone, inversion, optional button controls. Recommended default: approximately 10 to 20 degrees of phone tilt maps to the primary steering range. These values must be configurable.

## 18. Vehicle system
Every vehicle has configurable stats: Speed, Acceleration, Handling, Drift, Weight, Boost, Traction. Stats are data-driven (e.g. Danfo — Speed 72, Acceleration 82, Handling 61, Drift 55, Weight 88, Boost 65; illustrative, tuned through playtesting).

## 19. Vehicle design
Vehicles must be **kart-like in footprint and gameplay, but recognizable as the underlying Nigerian vehicle.** Do not create generic go-karts with Nigerian decals. Danfo: stylized Danfo body on a compact kart chassis. Keke: recognizable Keke structure. Okada: recognizable motorcycle/rider silhouette. Corolla-inspired and Camry-inspired sedans with exaggerated arcade design. Others: SUV, Sports Car, Food Truck, Generator Kart, Delivery Bike, Police Van. Use fictionalized designs rather than copying protected logos/branding.

## 20. Initial vehicle roster (10)
Keke, Danfo, Okada, Compact Sedan, Executive Sedan, SUV, Sports Car, Food Truck, Generator Kart, Delivery Bike. Each should have a distinct gameplay identity.

## 21. Character system (8 fictional characters)
Unique appearance, personality, voice lines, animations, strengths, weaknesses, customization options. No celebrity likenesses. Examples: Tunde (Danfo Driver, acceleration, confident), Amaka (Hustler, balanced), Musa (Strategist, handling), Bisi (Socialite, top speed), Emeka (Mechanic, weight/durability).

## 22. Driving system
Automatic acceleration, steering, braking/deceleration, traction, drift, boost, collisions, recovery, air time, slopes, surface changes. **Fun over simulation.**

## 23. Drift system
Steer → press Drift → enter drift → maintain → charge boost → release → boost. Levels: blue sparks, orange sparks, purple sparks; each stronger. Thresholds configurable.

## 24. Boost
Visual effect, sound effect, camera response, FOV response, exhaust effect, speed increase. Powerful, but never so strong that driving skill becomes irrelevant.

## 25. Item system
Items create controlled chaos. Server authoritative, data-driven, configurable, position-aware, balanced. Players receive items through item boxes/zones.

## 26. Initial item set (~10)
BOOST: **Jollof Boost** (large speed boost), **Suya Burst** (very short, powerful burst). DEFENSE: **Generator Shield** (blocks one attack), **No Wahala** (removes negative effects). ATTACK: **Pure Water** (temporarily disrupts opponent visibility), **Egg** (temporarily destabilizes steering), **Oil** (slippery patch). CHAOS: **Danfo** (crosses the racing line), **Okada** (rapidly crosses the track and can disrupt racers). UTILITY: **Sharp Guy** (automatically avoids the next major obstacle).

## 27. Future item library
Fuel, Agege Bread, Energy Drink, Power Bank, Umbrella, Helmet, Gele, Area Boys, NEPA Meter, Pepper Spray, Tomato, Garri, Stone, Traffic Cone, Broken Glass, Indomie, Keke, Police, Go-Slow, Road Construction, Flood, Generator Smoke, Naija Connection, Aunty's Advice, Inside Info, 419, Omo Shift!, Who Send You?, E Don Happen, Wahala Dey. Do not put every item into every race.

## 28. Item balancing
Probabilities depend partially on player position. 1st place: more defensive/utility items and moderate boosts, fewer extreme comeback items. 5th–8th: higher probability of comeback items, disruption, speed boosts. **Never guarantee a comeback. Skill must remain important.**

## 29. Dynamic Nigerian road system
The environment is a gameplay system: Danfo crossing, Keke, Okada, traffic, potholes, flood, road construction, police checkpoint, LASTMA, generator smoke, NEPA blackout, pedestrians, road diversions, sudden congestion. Events should be partially predictable but not perfectly predictable; the player learns to read the environment.

## 30. LASTMA system
LASTMA is a signature mechanic. It must not target only first place; any racer can be targeted.

## 31. LASTMA flow
LASTMA EVENT → target selected → "PULL OVER!" → pursuit → escape window → Escape OR Caught. If caught: fine required → enough Coins? YES: pay fine, continue. NO: CALL FOR BAIL → friend can pay → continue. No bail → ARRESTED → eliminated.

## 32. LASTMA design requirements
Funny, stressful, strategic, unpredictable, fair. It must not feel like the game randomly deletes a player; the player has agency. Escape can involve driving skill, shortcuts, obstacles, defensive items, route choice, traffic, other racers.

## 33. LASTMA fine
Example initial fine: 2,000 Coins (configurable). The game should never require real-money payment as the only way to survive a LASTMA event; Coins can be earned through normal play.

## 34. Bail system
When a player cannot pay, display CALL FOR BAIL. Friends receive "Tife needs bail!"; a friend can pay the fine and the player continues. Future statistics: People I've Bailed, People Who Bailed Me, Total Bail Given.

## 35. Elimination
If caught and unable to pay or receive bail, the player is eliminated: "YOU'RE LOCKED UP!" and can spectate.

## 36. Coin economy
Currency: Naija Coins, non-cashable initially. Earn through races, wins, challenges, achievements, tournaments, daily activities, season progression, optional rewarded advertising, sponsored activities. Spend on cosmetics, customization, progression, LASTMA fines, selected non-competitive features.

## 37. Economic safety
No pay-to-win. Do not sell stronger weapons, faster competitive vehicles, guaranteed wins or exclusive competitive advantages. Paid content provides identity, cosmetics, convenience, status, optional progression.

## 38. Real-money competition
Explicitly **not part of the initial build**. Architecture should not prevent future experimentation, but no betting or cash-out system in the MVP. Any future implementation requires separate legal review, regulatory review, fraud model, KYC, responsible gaming design, economic model.

## 39. Player progression
Level 1 → 50+. XP from completing races, finishing high, winning, challenges, tournaments, achievements, events. Level unlocks characters, vehicles, cosmetics, emotes, titles, features.

## 40. Rank vs level
Separate systems: Level = overall progression; Rank = competitive skill (e.g. Level 27, Chairman).

## 41–43. Challenges and achievements
Daily: complete 3 races, drift 20 times, escape LASTMA twice, overtake 15 racers, finish top 3, use 10 items. Weekly: win 10 races, beat 3 friends, finish top 3 five times, win on 3 tracks, escape LASTMA 5 times. Achievements: LASTMA Survivor (escape 10 times), No Dull (win without using an item), Area Boy (hit 5 opponents with attack items), Chairman (win a tournament), Omo! (win after starting the final lap in 8th).

## 44. Profile
Name, avatar, level, rank, title, wins, races, win rate, favourite vehicle, favourite track, tournament wins, LASTMA escapes, streak, achievements, rivals. The profile is a player's racing reputation.

## 45. Customization
Character: clothes, shoes, hairstyles, accessories, glasses, helmets, emotes, animations. Vehicle: paint, wheels, decals, exhaust, spoiler, horns, trails, effects. Identity: banner, badge, title, victory animation, nameplate, loading-screen identity.

## 46. Initial track roster (6)
1. **Third Mainland Rush** — heavy traffic, Danfos, potholes, bridge, construction, shortcuts, Lagos skyline. 2. **Yaba After Dark** — narrow roads, nightlife, vendors, Okadas, junctions, pedestrians, neon. 3. **Lagos-Ibadan Express** — high speed, trucks, long straights, diversions, overtaking. 4. **Abuja Airport Road** — wide roads, roundabouts, clean planning, checkpoints, fast. 5. **Port Harcourt Rain Run** — heavy rain, slippery roads, standing water, flooding, reduced visibility. 6. **Ibadan Hills** — steep climbs, downhills, hairpins, narrow roads, dense older urban environment.

## 47. Track design
Every track includes a main route, risk route, shortcuts, hazards, item zones, recovery zones, overtaking opportunities, skill sections. Track design rewards learning: a player who knows the track has an advantage over a player with a faster vehicle.

## 48–51. Visual direction
North star: **Recognizable Nigeria + polished stylized 3D + exaggerated arcade energy.** Lagos Danfo-level quality is the minimum benchmark. Do not intentionally make the game low-poly because it is mobile; target polished stylized mobile 3D. Principles: recognizably Nigerian, stylized (not photoreal), premium, readability first, exaggerated game feel (camera shake, particles, VFX, animation, FOV, sound, speed effects), regional identity (Lagos ≠ Abuja ≠ Port Harcourt). Vehicles: strong silhouette, recognizable source vehicle, exaggerated proportions, polished materials, readable colours, expressive details. Characters: stylized, expressive, recognizable, slightly exaggerated, culturally grounded, strong silhouettes.

## 52. Game feel
Boost: speed lines, FOV increase, camera movement, engine sound, exhaust VFX. Collision: camera shake, impact sound, vehicle reaction, sparks. Pure Water: screen splash, sound, short visual disruption. LASTMA: siren, flashing lights, pursuit music, UI warning. Final lap: music escalation, UI change, announcer.

## 53. Audio
Engine, tyres, collisions, items, environment, traffic, rain, sirens, crowd, Nigerian voice lines, UI, race music. Voice lines short and repeatable: "Omo!", "Wahala!", "Run am!", "No wahala!", "Move!", "Dem don catch you!".

## 54. Language
Ranked Match → Who Get Mouth?; Rematch → RUN AM BACK; Victory → You Scatter Am; Defeat → Dem Do You; Perfect Race → NO WAHALA; Personal Best → YOU DEY IMPROVE; Streak → YOU DEY HOT; Final Lap → LAST LAP, NO SHAKING; Overtaken → OMO! DEM DEY COME!. Understandable across Nigeria; avoid Lagos-only copy.

## 55–58. Social, rivalry, Wahala, sharing
Players add/invite/challenge friends, race together, view profiles, compare stats, create rivalries, bail friends, share results, use emotes, create private rooms. Rivalries are persistent entities (total races, wins, fastest laps, streak; "RUN AM BACK?"). After every race identify notable events (LASTMA Escape, Pure Water Hit, 3-player Overtake, Final Lap Comeback, Perfect Drift, Shortcut Master, Keke Collision, Biggest Comeback) as "YOUR WAHALA". Post-race cards are shareable to WhatsApp, Instagram, TikTok, X with a deep link where possible.

## 59. Wahala calendar
Monday No Item Race, Tuesday LASTMA Madness, Wednesday Danfo Madness, Thursday Drift Night, Friday Night Rush, Saturday City Cup, Sunday Chill Race. Configurable through live event data.

## 60. Leaderboards
Global, Nigeria, city, friends, track-specific, seasonal. Metrics: rank, wins, fastest lap, tournament wins, streak, rating.

## 61–63. Monetization, season pass, brand partnerships
Cosmetics, premium characters, premium vehicle cosmetics, season pass (free tier: Coins/XP/basic cosmetics/emotes; premium: exclusive cosmetics/vehicle/customization/victory animations/trails/badges; no competitive advantage), coin purchases, optional rewarded advertising, brand partnerships. Brand engine: Brand → Campaign → Track → Item → Challenge → Reward → Cosmetic → Tournament (e.g. MTN Yellow Rush). Brand integration should feel like gameplay, not advertising.

## 64. Technical architecture
Client: Unity + C#. Major systems: Core, Input, Vehicle, Race, Track, Items, Chaos, Characters, UI, Audio, VFX, Progression, Social, Multiplayer, Analytics.

## 65. Server authoritative architecture
Server authority: race membership, player IDs, countdown, checkpoints, laps, position, finish order, item grants, item effects, LASTMA, fines, bail, elimination, XP, Coins, rank, rewards. Client controls: local input, presentation, camera, local animation, VFX, audio, UI. **The client must never be trusted to determine competitive outcomes.**

## 66. Networking requirements
8 concurrent players per race, low-latency input, interpolation, client prediction, server reconciliation, disconnect handling, reconnect, race timeout, authoritative results. Test: excellent connection, average mobile connection, high latency, packet loss, temporary disconnect, reconnect, player leaving, server interruption.

## 67. Backend services
Authentication (account, guest, device identity, linking); Player (profile, level, XP, rank, inventory); Social (friends, rivalries, invites, private rooms); Matchmaking (casual, ranked, tournament); Progression (XP, achievements, challenges, rewards); Economy (Coins, transactions, purchases); Competition (rating, leaderboard, tournament); Live Operations (events, seasons, challenges, brand campaigns).

## 68. Economy transaction model
Coin changes are server-authoritative. Every transaction includes transaction_id, player_id, source, amount, balance_before, balance_after, timestamp, idempotency_key. Never modify Coins directly from client-side gameplay logic.

## 69. Data-driven design
Items, vehicles, characters, tracks, challenges and rewards are data-driven; avoid hardcoding balance values (e.g. ItemDefinition: id, name, rarity, category, duration, cooldown, effect, targeting, position_weight, visual_effect, audio_effect).

## 70–72. Performance, mobile targets, accessibility
Target stable 60 FPS on capable mid-range devices: LODs, mesh/texture optimization, batching, GPU instancing, pooling, controlled physics, limited overdraw, optimized particles/shaders, controlled draw calls. Push visual quality until the performance budget constrains it; measure before reducing. Android + iOS, landscape, touch + gyroscope, configurable quality levels (Low/Medium/High/Ultra). Accessibility: button steering alternative, tilt sensitivity/calibration, left/right UI layouts, readable/scalable text, audio settings, vibration settings, reduced camera shake. Tilt remains the primary intended experience.

## 73–77. UI
Home: Play, Ranked, Quick Race, Friends, Garage, Profile, Tournament, Season, Challenges, Shop, Settings (obvious primary CTA). Race HUD: position, lap, minimap, speed/boost state, item, race timer, relevant event notification; do not overcrowd. Event feedback (PULL OVER!, +1, LAST LAP NO SHAKING, OMO!, FULL TANK!) must be readable, fast, energetic, non-blocking. Lobby shows character, vehicle, level, rank, title, ready status; actions: change vehicle/character, emote, honk, inspect, invite. Results: position, time, XP, Coins, rank change, achievements, race highlights; primary action RUN AM BACK, secondary HOME.

## 78–81. Analytics, retention metrics, targets
Track acquisition, activation, engagement, competition, gameplay (drift, boost, item used/hit, overtake, shortcut, collision, LASTMA triggered/escaped/fine paid, bail requested/given, eliminated) and economy events. Retention D1/D3/D7/D14/D30/D60/D90 plus races/player, sessions/player, multiplayer races/player, friends added, rivalries created, rematch rate, tournament participation, challenge completion. Ambitious targets to validate: D1 40%+, D7 20%+, D30 10%+, D90 5%+. The most important metric is players who continue choosing to race. A D30-retained player should have 10+ races, multiple multiplayer sessions, a friend interaction, a rivalry, meaningful progression, an event/tournament, a recognizable identity, a reason to improve.

## 82. Initial content scope
8 characters, 10 vehicles, 6 tracks, 10 initial items (architecture supports 30+), 5 race modes, 10 ranks, level 1–50+, 8-player multiplayer, friends + rivalry + private rooms + rematch, ranked + tournaments, season architecture.

## 83–84. Production vertical slice and definition of done
Slice: Third Mainland Rush, 8-player architecture, 1 vehicle, 1 character, 8–10 items; tilt, auto acceleration, steering, drifting, boost, items, traffic, dynamic road events, LASTMA, Coins, XP, rank, results, rematch; production-quality visual target. Done when: 1 launch, 2 enter race, 3 others join, 4 countdown, 5 tilt driving, 6 drift, 7 boost, 8 receive items, 9 use items, 10 traffic affects racing, 11 road events occur, 12 LASTMA can pursue any racer, 13 escape, 14 fine, 15 request bail, 16 another player bails, 17 elimination, 18 race completes correctly, 19 authoritative finish order, 20 XP/Coins awarded, 21 rank changes correctly, 22 results screen, 23 rematch, 24 repeat, 25 disconnects do not corrupt results, 26 no obvious client-side exploit.

## 85. Development phases
0 Foundation; 1 Driving prototype (driving must be fun before multiplayer); 2 Track prototype (greybox Third Mainland Rush); 3 Race system; 4 Items; 5 LASTMA; 6 Multiplayer (8 players complete a race); 7 Progression; 8 Social; 9 Full content; 10 Polish; 11 QA.

## 86–89. Claude Code development principles
Behave as a senior game engineering team: understand architecture, inspect code, identify dependencies, propose, implement incrementally, test, fix, refactor, document, move on. Do not create duplicate systems because an existing one is hard to modify. **Claude must not:** randomly change architecture; replace systems without justification; hardcode balance values; put server-authoritative logic on clients; create fake multiplayer; create placeholder gameplay and call it complete; silently reduce scope; replace tilt with buttons; turn Nigerian vehicles into generic karts; make the game intentionally low-quality; implement pay-to-win; introduce real-money betting; create fake backend behaviour that is later impossible to replace. **Claude should:** prioritize playable systems, modular architecture, data-driven configuration, reusable components, tests, profiling, decoupled systems, tunable parameters, documented decisions, mobile performance, server authority, tools for rapid content creation. Code quality: clear naming, single responsibility, interfaces, DI where useful, events where useful, configuration assets, testable logic; avoid giant MonoBehaviours, deep coupling, magic numbers, duplicate logic, unnecessary global state, client-authoritative competitive logic.

## 90. Testing
Race: invalid checkpoint, lap completion, finish order, timeout. Items: acquisition, consumption, target validation, cooldown, effect duration. LASTMA: valid target, pursuit, escape, catch, fine, insufficient Coins, bail, no bail, elimination. Economy: transaction, duplicate transaction, insufficient balance, disconnect during transaction. Multiplayer: disconnect, reconnect, packet loss, latency, player leaving, server timeout.

## 91. Anti-cheat
Server validates speed, position, acceleration, checkpoint order, lap completion, item grants/effects, Coins, XP, rewards. The client should never be able to tell the server "I finished first"; it sends inputs/state information and the server determines the result.

## 92–95. Persistence, failure states, onboarding
Persist profile, level, XP, rank, Coins, vehicles, characters, cosmetics, achievements, challenges, friends, rivalries, race history, tournament history. Every major system defines network failure, invalid data, missing asset, disconnect, server failure, timeout, corrupted state, duplicate transaction, interrupted purchase, interrupted race with understandable feedback. Onboarding: launch → choose name → starter character → short control tutorial → practice → first race → results → unlock → invite friend; teach through gameplay (tilt, steer, drift, boost, item, avoid traffic, finish), avoid a long tutorial.

## 96–99. Psychology, content, chaos, fairness
Create competence, competition, identity, status, belonging, surprise, mastery. Every new track/item/vehicle/character/event must create a new strategic or social possibility. Chaos must be controlled, readable and recoverable. Comeback mechanics create opportunities, never guarantee victory; leaders have defensive tools but stay vulnerable to mistakes.

## 100–103. Future expansion, metrics, risk, first build priority
Architecture supports more cities, vehicles, characters, tracks, items, live events, brand campaigns, tournaments, clans/crews, spectator, creator mode, esports, ranked seasons, cross-region; build foundations, not these systems. Primary metric D30 retention. Primary risk: "Will people still want to race after the novelty wears off?" — validate driving feel, multiplayer fun, social competition, rivalries, progression, replayability before content quantity. First goal: **make one race so fun that players immediately want another race** (1 track, 1 character, 1 vehicle, 8-player architecture, tilt, drift, boost, 10 items, traffic, LASTMA, results, XP, Coins, rank, rematch), then test aggressively.

## 104. Claude execution protocol
1 Inspect repository → 2 Unity project structure → 3 architecture foundation → 4 driving prototype → 5 greybox Third Mainland Rush → 6 race state machine → 7 items → 8 LASTMA → 9 multiplayer architecture → 10 progression → 11 social foundations → 12 production assets → 13 optimize → 14 test → 15 package builds.

## 105. Claude agent rule
Treat this PRD as the product contract. When ambiguous: prefer existing architecture, configurable systems, future extensibility, gameplay fairness, mobile performance; do not silently reduce scope; document material decisions. If a decision could materially change gameplay, architecture or monetization, stop and present it before implementing.

## 106. Build milestone gates
Gate 1 Driving: is driving fun? Gate 2 Race: is a complete race fun? Gate 3 Chaos: do items and road events create memorable moments? Gate 4 LASTMA: does LASTMA create stories rather than frustration? Gate 5 Multiplayer: is racing real people significantly more fun? Gate 6 Retention: do players voluntarily want another race? If not, do not solve it by adding content.

## 107–109. Final product definition, non-negotiables, north star
Success: "I downloaded this because it looked Nigerian" → "I kept playing because racing is fun" → "I came back to beat my friends" → "I stayed to become Chairman" → "Naija Kart is my game." Non-negotiables: multiplayer is core; tilt is primary; Nigerian identity influences gameplay; vehicles retain recognizable Nigerian structures; skill matters; chaos is controlled; LASTMA is core; social competition is the retention engine; no pay-to-win; no real-money betting in the initial build; server authority; stylized 3D; mobile performance is a hard constraint; premium not cheaply "Nigerianized"; do not constrain visual quality to low-poly; gameplay quality over content quantity; architectural integrity over rapid prototyping; every race can create a story; every story can create a rivalry; every rivalry creates a reason to race again.

**NAIJA KART — Race. Survive. Compete. Run Am Back.**
