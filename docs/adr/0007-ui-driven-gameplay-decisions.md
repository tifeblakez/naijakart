# ADR-0007: Gameplay decisions taken from the UI designs

**Status:** accepted (UI designs supplied by the product owner are the UX specification)

The Garage, "How you wan drive?", "PULL OVER!" and Race HUD designs specify behaviour that the PRD
text left open or described differently. Each is implemented as configuration so the PRD variant
remains available for event modes.

| Screen | Decision | Where |
|---|---|---|
| PULL OVER! | Three choices on a short timer: **Take Penalty** (wait `penaltySeconds`, lose items; the default when time runs out), **Pay Fine** (`fineAmount` earned Coins, back in `resumeStunSeconds`), **Call for Bail** (a friend frees you early). **No arrest** unless `lastma.arrestEnabled`. In Ranked/Tournament everyone takes the penalty (`finesAllowedInRanked`, `bailAllowedInRanked` = false). | `LastmaConfig`, `LastmaSystem` |
| HUD shortcut banner | "Faster, but LASTMA dey watch": a shortcut raises LASTMA targeting weight (`shortcutHeatMultiplier` for `shortcutHeatSeconds`). Pressure relief during pursuit is off by default (`shortcutPressureRelief = 0`). | `LastmaSystem.NotifyShortcut` |
| HUD item row | Four item slots (`items.inventorySlots`), tap a slot to use (`PlayerInputFrame.ItemSlot`). Items beyond the PRD ten (Spike, Banana) are data rows. | `ItemInventory`, `items.json` |
| HUD speed gauge "3" | Boost is a banked charge: drift release stores charges (`drift.releaseMode = StoreCharge`, `levelStoredCharges`), the Boost button spends one (`boost.storedChargeDuration/Multiplier`, `maxStoredCharges`). `Immediate` keeps the PRD's release-to-boost. | `ArcadeVehicleModel` |
| How you wan drive? | Tilt (default) or **Touch = drag the left thumb**. Buttons remain an accessibility option. | `SteeringMode`, `TouchSteerZone` |
| Garage | Vehicles/characters are starter, **bought with earned Coins** (`priceCoins`) or level-locked (`unlockLevel`). A second currency **P (Premium)** exists for cosmetics only; it can never pay a fine or buy a vehicle. | `Currency`, `VehicleDefinition.priceCoins`, `PurchaseVehicle` |

Not changed: server authority, tilt as primary, no pay-to-win, no real-money betting.
