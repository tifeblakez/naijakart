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
