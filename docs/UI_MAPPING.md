# UI → code mapping

How each designed screen is wired. Share a new screen and the matching presenter/data gets added here.

| Screen | Presenter | Data it reads | Intents it sends |
|---|---|---|---|
| Home | `HomePresenter` | Welcome (coins, rank), LiveEvent | JoinQueue, CreateRoom, JoinRoom, practice |
| Garage (Karts / Racers / Style) | `GaragePresenter` | `ProfileDto` (owned ids, level, coins, premium), vehicle/character definitions (stats, price, unlock level) | SelectLoadout, PurchaseVehicle, PurchaseCharacter |
| How you wan drive? (onboarding step 3) | `ControlChoicePresenter` | Accelerometer presence | none (local setting `SteeringMode`) |
| Lobby | `LobbyPresenter` | `RoomStateDto` | Ready, StartRoom, SelectLoadout |
| Race HUD | `RaceHudPresenter`, `MinimapPresenter`, `TouchControlsPresenter`, `TouchSteerZone` | `RaceSnapshot` (position/total, lap, time, speed, boost charges, 4 item slots, standings), `RaceEvent`s (ticker, shortcut banner) | Input frames (steer, drift, item+slot, boost) |
| PULL OVER! | `LastmaPromptPresenter` | `LastmaCaught` event + `LastmaOptions` message (fine, friends online, fines/bail allowed) | TakePenalty, PayFine, RequestBail; PayBail from friends |
| Results | `ResultsPresenter` | `RaceResults`, rewards, highlights, challenge completions | VoteRematch, LeaveRoom |
| Social | (next) | Rivalries, friends leaderboard, bail stats | AddFriend, GetRivalries, GetLeaderboard |
| Profile | (next) | `ProfileDto` | GetProfile |

Copy lives in `NaijaCopy`. Speeds are shown in km/h (`m/s × 3.6`). Positions are 1-based ordinals.

## Gameplay environment reference (board shared 2026-10-08)

Second reference for the Third Mainland Rush look: hero kart in Naija Kart green livery, heavy
danfo traffic, the "Third Mainland Bridge / Lagos" gantry, a "LAGOS · No Stress" billboard,
cable-stay pylons, orange "?" item boxes and coins, lagoon to the right, dense skyline. Feature
list: iconic Lagos locations, dynamic traffic, interactive environment, power-ups & shortcuts,
day & night cycles. Thumbnails: bridge view, heavy traffic, road diversion (cones, chevrons),
city skyline, potholes & road wear, night version.

Mapping:
* Colour palette → `WorldBuilder` constants (Lagos Yellow, Danfo Blue, Road Grey, Sky Blue, Green,
  Building Beige, Ocean Blue, Night Purple) and the previewer colour grade.
* Road diversion → `BuildConstruction` (striped barriers, cones, chevrons, warning lamps, crane) at
  the chicane, and the `cone_row` hazard template's "GO SLOW" board.
* Heavy traffic → `roadEvents.kindWeights` (traffic, danfo and okada crossings) and the parked
  danfos/bus stops in `BuildRoadsideLife`; traffic is always a server hazard, never decoration that
  karts drive through.
* Potholes & road wear → pothole hazards plus the asphalt shader (grain, patches, tar lines).
* Day & night → `WorldBuilder.Build(track, seed, theme: "night")`, `export-world --theme night`:
  Night Purple sky with stars, lit windows, glowing lamps and signs, hero-kart headlights.
* Item boxes → orange hologram cube with a gold "?" on two faces.
* HUD → the Race HUD PDF remains the layout of record; this board's stacked item counts are a
  variant to revisit when the inventory UI is finalised.

## Design canvas: every screen → system (canvas "Naija Kart Game Screens", 34 artboards, read 2026-10-08)

The canvas is the layout of record (844×390 pt phone frame, Lilita One + Nunito, navy glass
plates, danfo yellow for "you" and the one primary action). The previewer HUD is built from its
`RaceHUD`, `Countdown`, `LastmaChase`, `Caught` and `Results` artboards using the same tokens and
classes, so the video shows the designed UI over the generated world.

| # | Screen | Backs onto | Status |
| --- | --- | --- | --- |
| 01.1–01.5 | Onboarding: welcome, pick driver, choose controls, practice lap, ready | Client only; practice lap = `LocalPracticeHost`; AI disclosure from `RoomStateDto` bot tags | Client flow pending |
| 02 | Home | `Welcome`, `GetProfile`, `GetChallenges`, `LiveEvent` (Wahala today), friends online | Served |
| 03 | Choose mode | `JoinQueue` (QuickRace/Ranked), `CreateRoom` (PrivateRoom/Practice) | Served |
| 03.1 | Ranked hub | `GetProfile` (RP, division, RP to next, last 5), `GetSeason` (days left, Rush Hour, rules), `GetLeaderboard` "rp" | Served (ADR-0010) |
| 03.2 / 03.3 | Private room, invite friends | `CreateRoom`/`JoinRoom` by EKO-427 code (forgiving input), `SetRoomOptions` (track, laps, LASTMA Off/On/Madness, items Off/Boosts only/On, AI fill), `InviteFriend` → `RoomInvite`, `RoomState.Invited`, `ShareUrl` | Served |
| 04 | Matchmaking | `QueueStatus` every second: found / grid size / seconds until AI fills | Served |
| 05 | Lobby | `RoomState` members with Status (Host/Ready/Waiting/AI), `AiSeats`, `StartsInSeconds`, `Ready` | Served |
| 06 | Countdown | `RaceSnapshot` countdown, tilt calibration (client) | Served |
| 07 | Race HUD | `RaceSnapshot` (position, lap, items, boost charges, drift level), `RaceEvent` callouts | Served |
| 08 | LASTMA chase | `LastmaPhase/Pressure/TimeRemaining` in the snapshot | Served |
| 09 | Caught: recovery choice | `LastmaOptions`, intents `TakePenalty` / `PayFine` / `RequestBail` | Served |
| 10 | Results | `RaceResults` + `SettledRewardDto` (XP, coins, RP delta, division before/after), stats | Served |
| 11 | Network issue | reconnect by `Hello`; an AI stand-in drives the kart while away (`RoomState` member Status "AiDriving"), `ReconnectWindowSeconds`/`ReconnectAttempts`; DNF after the window = last | Served |
| 12 | Rivalry | `GetRivalries`: wins, streaks, last 5, bails given/received, since, best times on the shared track, rank labels | Served |
| 13–13.2 | Garage: karts, racers, style | `GetGarage`, `PurchaseVehicle/Character`; `GetShop`/`PurchaseCosmetic`/`EquipCosmetic` for colours, rims, trails, outfits (cosmetics.json; Owned/Equipped/Coins/Premium/Level/Pass states) | Served |
| 14 | Profile | `GetProfile`: level, RP/rank label, stats, achievements of total, title, home city, racer code, best times, top rivalries | Served |
| 15 | Leaderboards | `GetLeaderboard` metric rp/wins/streak/lastma/level/track with Scope friends/city/nigeria/global/track | Served |
| 16 | Social | `GetFriends` (presence: Online / Racing · lap N / Private Room / Seen 2h ago, joinable room, needs-bail), `AddFriend` → `FriendRequest` → `AcceptFriend`/`DeclineFriend`, `BailRequest` at the 150-coin bail price, invite code = referral code | Served (party pending) |
| 17 | Tournament | `GetTournament`/`EnterTournament`/`LeaveTournament` → `Tournament` (rounds with state, standings and notes, points, next race countdown, rewards, your room); server schedules rounds, seats grids with AI fill, awards points and cosmetics (tournaments.json, ADR-0011) | Served |
| 18 | Season and challenges | `GetChallenges` (daily/weekly), Wahala calendar (`liveEvents.weekdayRules`), `GetSeasonPass`/`ClaimPassTier`/`BuyPremiumPass` (season-pass.json, season XP from every race) | Served |
| 19 | Shop | `GetShop`: catalogue by kind, featured item with days left, Coins or P prices, level and pass locks; purchases are ledger debits with idempotency keys | Served |
| 20 | Settings: data and performance | client; content packs manifest | Pending |
| 21 | Save progress | `ClaimStart` / `ClaimVerify` / `ClaimWithProvider`, account bonus | Served (ADR-0009) |
| 22–23 | Phone number, enter code | `ClaimStart` (Text = number, Flag = WhatsApp), `ClaimVerify`, resend timer | Served |
| 24 | Create your racer | `CheckName`, `SetRacer` (name, look, home city) | Served |
| 25 | Account ready | `Account` (referral code), `ApplyReferral`, referral bonus after first race | Served |
