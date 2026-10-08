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
