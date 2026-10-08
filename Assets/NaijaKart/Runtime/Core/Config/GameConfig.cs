using System;

namespace NaijaKart.Core.Config
{
    /// <summary>
    /// Root of all tunable gameplay values. Loaded from StreamingAssets/NaijaKart/Config/game-config.json
    /// on the client and from the same file on the server. Nothing in here may be hard-coded elsewhere
    /// (PRD §69, §87). Plain public fields keep it compatible with both Unity's JsonUtility and
    /// System.Text.Json (IncludeFields).
    /// </summary>
    [Serializable]
    public sealed class GameConfig
    {
        public int schemaVersion = 1;
        public SimulationConfig simulation = new SimulationConfig();
        public DrivingConfig driving = new DrivingConfig();
        public DriftConfig drift = new DriftConfig();
        public BoostConfig boost = new BoostConfig();
        public TiltConfig tilt = new TiltConfig();
        public RaceRulesConfig raceRules = new RaceRulesConfig();
        public CollisionConfig collision = new CollisionConfig();
        public ItemSystemConfig items = new ItemSystemConfig();
        public RoadEventsConfig roadEvents = new RoadEventsConfig();
        public LastmaConfig lastma = new LastmaConfig();
        public EconomyConfig economy = new EconomyConfig();
        public ProgressionConfig progression = new ProgressionConfig();
        public AntiCheatConfig antiCheat = new AntiCheatConfig();
        public LiveEventsConfig liveEvents = new LiveEventsConfig();
    }

    /// <summary>
    /// Wahala Calendar (PRD §59): a weekly rhythm of rule tweaks for public races. Entries are matched
    /// by UTC weekday (0 = Sunday … 6 = Saturday). Missing weekday = normal rules. Replaceable by a
    /// live-ops feed later; the shape is the contract.
    /// </summary>
    [Serializable]
    public sealed class LiveEventsConfig
    {
        public bool enabled = true;
        public WeekdayRule[] weekdayRules =
        {
            new WeekdayRule { weekday = 1, id = "no_item_race", displayName = "No Item Race", itemsEnabled = false },
            new WeekdayRule { weekday = 2, id = "lastma_madness", displayName = "LASTMA Madness", lastmaIntervalMultiplier = 0.5f },
            new WeekdayRule { weekday = 3, id = "danfo_madness", displayName = "Danfo Madness", allowedItemIds = new[] { "danfo", "okada", "generator_shield", "no_wahala", "jollof_boost" } },
            new WeekdayRule { weekday = 4, id = "drift_night", displayName = "Drift Night", driftBoostMultiplier = 1.25f },
            new WeekdayRule { weekday = 5, id = "night_rush", displayName = "Night Rush", preferredTrackId = "third_mainland_rush" },
            new WeekdayRule { weekday = 6, id = "city_cup", displayName = "City Cup", rankedXpMultiplier = 1.5f },
            new WeekdayRule { weekday = 0, id = "chill_race", displayName = "Chill Race", lastmaEnabled = false },
        };

        public WeekdayRule RuleFor(int weekday)
        {
            if (!enabled || weekdayRules == null) return null;
            foreach (var r in weekdayRules) if (r.weekday == weekday && !r.disabled) return r;
            return null;
        }
    }

    [Serializable]
    public sealed class WeekdayRule
    {
        public int weekday;
        public string id;
        public string displayName;
        public bool disabled;
        public bool itemsEnabled = true;
        public bool lastmaEnabled = true;
        /// <summary>Restrict the item roll to these ids (null/empty = all).</summary>
        public string[] allowedItemIds;
        /// <summary>Scales LASTMA min/max trigger intervals (0.5 = twice as often).</summary>
        public float lastmaIntervalMultiplier = 1f;
        /// <summary>Scales drift-release boost duration.</summary>
        public float driftBoostMultiplier = 1f;
        /// <summary>Scales XP for ranked races on this day.</summary>
        public float rankedXpMultiplier = 1f;
        /// <summary>If set and loaded, public races prefer this track.</summary>
        public string preferredTrackId;
    }

    [Serializable]
    public sealed class SimulationConfig
    {
        /// <summary>Authoritative simulation ticks per second.</summary>
        public int tickRate = 30;
        public int maxPlayersPerRace = 8;
        /// <summary>Snapshots sent to clients per second (may be lower than tick rate).</summary>
        public int snapshotRate = 15;
        /// <summary>While a room shows results, re-send them this often so no client misses them.</summary>
        public float resultsResendSeconds = 2f;
    }

    [Serializable]
    public sealed class DrivingConfig
    {
        /// <summary>Top speed (m/s) for a vehicle with Speed stat 0 and 100 respectively.</summary>
        public float minTopSpeed = 18f;
        public float maxTopSpeed = 30f;
        /// <summary>Forward acceleration (m/s^2) for Acceleration stat 0 and 100.</summary>
        public float minAcceleration = 6f;
        public float maxAcceleration = 14f;
        /// <summary>Deceleration when over target speed (m/s^2).</summary>
        public float coastDeceleration = 8f;
        public float brakeDeceleration = 20f;
        /// <summary>Yaw rate (rad/s) at full steer for Handling stat 0 and 100.</summary>
        public float minYawRate = 1.4f;
        public float maxYawRate = 2.6f;
        /// <summary>Speed (m/s) at which steering authority peaks; below it steering is scaled by speed/peak.</summary>
        public float steeringPeakSpeed = 8f;
        /// <summary>Fraction of steering authority retained at top speed (high-speed stability).</summary>
        public float highSpeedSteerFactor = 0.65f;
        /// <summary>Lateral slip damping (1/s) for Traction stat 0 and 100.</summary>
        public float minLateralGrip = 4f;
        public float maxLateralGrip = 9f;
        /// <summary>Speed multiplier when off the road surface.</summary>
        public float offroadSpeedMultiplier = 0.55f;
        public float offroadGripMultiplier = 0.6f;
        /// <summary>Seconds the car must be beyond the recovery margin before being reset to the last checkpoint.</summary>
        public float recoveryDelaySeconds = 1.5f;
        /// <summary>Recovery zone (PRD §47): continuous seconds offroad (even near the edge) before a reset to the last gate.</summary>
        public float offroadStuckSeconds = 6f;
        /// <summary>Extra distance beyond the road edge before recovery kicks in (metres).</summary>
        public float recoveryMarginMeters = 12f;
        /// <summary>Speed the car is reset to after recovery (fraction of top speed).</summary>
        public float recoverySpeedFraction = 0.3f;
        /// <summary>Seconds of reduced control after a recovery reset.</summary>
        public float recoveryStunSeconds = 0.8f;
    }

    [Serializable]
    public sealed class DriftConfig
    {
        /// <summary>Minimum speed (m/s) required to start/maintain a drift.</summary>
        public float minSpeed = 9f;
        /// <summary>Minimum |steer| to initiate a drift.</summary>
        public float minSteerToStart = 0.25f;
        /// <summary>Yaw multiplier while drifting for Drift stat 0 and 100.</summary>
        public float minYawMultiplier = 1.25f;
        public float maxYawMultiplier = 1.7f;
        /// <summary>Counter/inner steering range inside a drift: yaw = base * lerp(inner, outer, steerToward).</summary>
        public float innerSteerFactor = 0.55f;
        public float outerSteerFactor = 1.35f;
        /// <summary>Lateral slide velocity injected while drifting (m/s per second of drift).</summary>
        public float slideInjection = 6f;
        /// <summary>Grip multiplier while drifting.</summary>
        public float gripMultiplier = 0.45f;
        /// <summary>Speed retention while drifting (1 = none lost, lower = scrubs speed).</summary>
        public float speedRetention = 0.93f;
        /// <summary>Charge per second while drifting. Charge accumulates in seconds-equivalent.</summary>
        public float chargeRate = 1f;
        /// <summary>Charge accrues faster when steering into the drift (max bonus factor).</summary>
        public float steerIntoChargeBonus = 0.5f;
        /// <summary>Drift level thresholds in charge units. Index 0 = blue, 1 = orange, 2 = purple.</summary>
        public float[] levelThresholds = { 0.8f, 1.8f, 3.0f };
        /// <summary>Boost duration (seconds) awarded per drift level (blue, orange, purple).</summary>
        public float[] levelBoostDuration = { 0.6f, 1.1f, 1.8f };
        /// <summary>Boost speed multiplier per drift level (Immediate mode).</summary>
        public float[] levelBoostMultiplier = { 1.15f, 1.22f, 1.3f };
        /// <summary>Immediate: boost fires on release. StoreCharge: release banks charges spent with the Boost button (HUD gauge).</summary>
        public DriftReleaseMode releaseMode = DriftReleaseMode.StoreCharge;
        /// <summary>Charges banked per drift level in StoreCharge mode (blue, orange, purple).</summary>
        public int[] levelStoredCharges = { 1, 1, 2 };
    }

    public enum DriftReleaseMode { Immediate, StoreCharge }

    [Serializable]
    public sealed class BoostConfig
    {
        /// <summary>Cap on simultaneous boost multiplier so stacked items cannot break the track.</summary>
        public float maxMultiplier = 1.6f;
        /// <summary>How quickly speed reaches boosted target (m/s^2).</summary>
        public float boostAcceleration = 40f;
        /// <summary>Boost stat 0..100 scales the duration of boosts the vehicle gets.</summary>
        public float minDurationScale = 0.85f;
        public float maxDurationScale = 1.2f;
        /// <summary>StoreCharge mode: maximum banked charges shown on the speed gauge.</summary>
        public int maxStoredCharges = 3;
        /// <summary>Duration and multiplier of one spent charge.</summary>
        public float storedChargeDuration = 1.2f;
        public float storedChargeMultiplier = 1.25f;
    }

    [Serializable]
    public sealed class TiltConfig
    {
        /// <summary>Phone roll (degrees) that maps to full steer. PRD §17: 10–20 degrees.</summary>
        public float fullSteerAngleDegrees = 15f;
        /// <summary>Roll below this (degrees) is ignored.</summary>
        public float deadZoneDegrees = 1.5f;
        /// <summary>Response curve exponent: 1 = linear, >1 = finer control near centre.</summary>
        public float responseExponent = 1.3f;
        /// <summary>Low-pass smoothing lambda (1/s) applied to the tilt reading. Higher = snappier.</summary>
        public float smoothingLambda = 18f;
        public float defaultSensitivity = 1f;
        public float minSensitivity = 0.5f;
        public float maxSensitivity = 2f;
    }

    [Serializable]
    public sealed class RaceRulesConfig
    {
        public int defaultLaps = 3;
        public int countdownSeconds = 3;
        /// <summary>Seconds the lobby waits for players to ready up before starting with whoever is present.</summary>
        public float lobbyReadyTimeoutSeconds = 45f;
        /// <summary>Minimum players required to start a public race.</summary>
        public int minPlayersToStart = 2;
        /// <summary>Seconds a Quick Race queue waits for a full grid before starting with minPlayersToStart.</summary>
        public float matchmakingWaitSeconds = 20f;
        /// <summary>Seconds after the first finisher before the race is force-finished.</summary>
        public float finishGraceSeconds = 30f;
        /// <summary>Absolute cap on race time; prevents stuck races (PRD §13 Timeout).</summary>
        public float maxRaceDurationSeconds = 420f;
        /// <summary>Seconds a disconnected player may reconnect before being marked DNF.</summary>
        public float reconnectWindowSeconds = 20f;
        /// <summary>Grid spacing between start positions (metres) along and across the track.</summary>
        public float gridRowSpacing = 4f;
        public float gridColumnSpacing = 2.5f;
        /// <summary>Seconds the results screen waits for rematch votes.</summary>
        public float rematchVoteSeconds = 20f;
    }

    [Serializable]
    public sealed class CollisionConfig
    {
        /// <summary>Collision radius used for kart-vs-kart and kart-vs-hazard tests (metres).</summary>
        public float vehicleRadius = 1.1f;
        /// <summary>Maximum fraction of speed lost on contact (reached at bumpFullLossClosingSpeed). Nudges cost proportionally less.</summary>
        public float bumpSpeedLoss = 0.2f;
        /// <summary>Closing speed (m/s) at which the full bumpSpeedLoss applies.</summary>
        public float bumpFullLossClosingSpeed = 10f;
        /// <summary>Lateral push (m/s) applied on contact, scaled by weight ratio.</summary>
        public float bumpLateralImpulse = 3f;
        /// <summary>Minimum closing speed before a contact counts as a "hit" event.</summary>
        public float hitEventMinClosingSpeed = 4f;
        /// <summary>Stun applied when a kart hits a hard hazard (Danfo, Okada).</summary>
        public float hardHazardStunSeconds = 1.2f;
        public float hardHazardSpeedRetention = 0.2f;
        /// <summary>Seconds of hazard immunity after any hazard hit (prevents re-hit loops behind traffic).</summary>
        public float hazardImmunitySeconds = 1.5f;
        /// <summary>Lateral shove (metres) applied when hitting a persistent hazard such as traffic, to clear its lane.</summary>
        public float trafficLateralShove = 2.5f;
    }

    [Serializable]
    public sealed class ItemSystemConfig
    {
        /// <summary>Seconds after pickup before the item can be used (roulette feel).</summary>
        public float rollDurationSeconds = 1.0f;
        /// <summary>Seconds an item box is unavailable after being taken.</summary>
        public float boxRespawnSeconds = 4f;
        public float boxPickupRadius = 2f;
        /// <summary>Attack items target the nearest racer ahead within this distance (metres).</summary>
        public float forwardTargetRange = 45f;
        /// <summary>Hazards dropped behind the kart are offset by this distance (metres).</summary>
        public float dropBehindDistance = 3f;
        /// <summary>Maximum simultaneously alive hazards per race (perf and chaos cap).</summary>
        public int maxActiveHazards = 24;
        /// <summary>Item slots per racer (HUD shows four).</summary>
        public int inventorySlots = 4;
        /// <summary>Spike strip hit: stun and speed retention.</summary>
        public float spikeStunSeconds = 0.8f;
        public float spikeSpeedRetention = 0.3f;
    }

    [Serializable]
    public sealed class RoadEventsConfig
    {
        public bool enabled = true;
        /// <summary>Minimum/maximum seconds between scheduled road events.</summary>
        public float minIntervalSeconds = 6f;
        public float maxIntervalSeconds = 14f;
        /// <summary>Seconds of telegraph (horn, siren, UI cue) before an event becomes dangerous.</summary>
        public float telegraphSeconds = 1.5f;
        /// <summary>Relative weights per RoadEventKind (traffic, pothole, okadaCross, danfoCross, goSlow, flood, checkpointStop).</summary>
        public float[] kindWeights = { 3f, 2f, 2.5f, 2f, 1f, 0.5f, 0.5f };
        /// <summary>Events are placed this many metres ahead of the chosen anchor racer along the track.</summary>
        public float placeAheadMeters = 35f;
        /// <summary>Lifetime of crossing hazards (seconds).</summary>
        public float crossingLifetimeSeconds = 3.5f;
        public float crossingSpeed = 9f;
        /// <summary>Lifetime of static hazards such as potholes/oil (seconds).</summary>
        public float staticLifetimeSeconds = 25f;
        public float potholeSpeedRetention = 0.6f;
        public float goSlowSpeedMultiplier = 0.5f;
        public float goSlowLengthMeters = 25f;
        public float floodGripMultiplier = 0.5f;
        public float floodSpeedMultiplier = 0.75f;
    }

    [Serializable]
    public sealed class LastmaConfig
    {
        public bool enabled = true;
        /// <summary>Race time (seconds) before the first LASTMA event may trigger.</summary>
        public float firstTriggerMinSeconds = 20f;
        public float minIntervalSeconds = 25f;
        public float maxIntervalSeconds = 45f;
        /// <summary>No new LASTMA events in the last N seconds of the leader's final lap, to keep finishes fair.</summary>
        public float noTriggerBeforeFinishSeconds = 10f;
        public int maxActiveEvents = 1;
        /// <summary>Targeting weight by race position (index 0 = 1st). PRD §30: never only first place.</summary>
        public float[] positionWeights = { 1.6f, 1.3f, 1.1f, 1f, 1f, 1f, 0.9f, 0.8f };
        /// <summary>Seconds a racer who was recently targeted cannot be targeted again.</summary>
        public float targetCooldownSeconds = 60f;
        /// <summary>"PULL OVER!" warning before the pursuit starts.</summary>
        public float warningSeconds = 2f;
        /// <summary>Pursuit duration. Survive it and you escape.</summary>
        public float pursuitSeconds = 12f;
        /// <summary>Pressure accumulates 0..1; at 1 the player is caught.</summary>
        public float pressureGainPerSecond = 0.06f;
        /// <summary>Pressure drains while boosting.</summary>
        public float pressureLossWhileBoosting = 0.25f;
        /// <summary>Pressure jumps on being stunned/offroad/hit (per second while in that state).</summary>
        public float pressureGainWhileStunned = 0.6f;
        public float pressureGainWhileOffroad = 0.2f;
        /// <summary>Taking a shortcut gate during pursuit drops pressure by this amount (0 = shortcuts do not help escape).</summary>
        public float shortcutPressureRelief = 0f;
        /// <summary>"Faster, but LASTMA dey watch": targeting weight multiplier after a shortcut, for shortcutHeatSeconds.</summary>
        public float shortcutHeatMultiplier = 2.5f;
        public float shortcutHeatSeconds = 30f;
        /// <summary>Below this fraction of top speed the pursuer gains extra pressure.</summary>
        public float slowSpeedFraction = 0.6f;
        public float pressureGainWhileSlow = 0.3f;
        /// <summary>Fine in Naija Coins (earned Coins only, never premium currency).</summary>
        public long fineAmount = 200;
        /// <summary>Seconds to choose: take the penalty, pay the fine, or call for bail. No choice = penalty.</summary>
        public float fineDecisionSeconds = 3f;
        /// <summary>Penalty option: wait this long at the roadside.</summary>
        public float penaltySeconds = 3f;
        /// <summary>Penalty option also empties the item slots.</summary>
        public bool penaltyLosesItems = true;
        /// <summary>If true, no choice within fineDecisionSeconds (and no bail) means arrest and elimination (PRD §35). Off: the penalty applies.</summary>
        public bool arrestEnabled = false;
        /// <summary>Ranked/tournament: everyone takes the same penalty; paying or bail is not allowed.</summary>
        public bool finesAllowedInRanked = false;
        public bool bailAllowedInRanked = false;
        /// <summary>Seconds a bailed-out racer waits before resuming (bail "frees you early" relative to the penalty).</summary>
        public float bailResumeSeconds = 0.5f;
        /// <summary>Speed multiplier while stopped by LASTMA (fine pending).</summary>
        public float pulledOverSpeedMultiplier = 0f;
        /// <summary>After paying the fine, "back in" this many seconds.</summary>
        public float resumeStunSeconds = 1f;
        /// <summary>A Generator Shield blocks the catch and ends the pursuit.</summary>
        public bool shieldBlocksCatch = true;
    }

    [Serializable]
    public sealed class EconomyConfig
    {
        public string currencyName = "Naija Coins";
        public string premiumCurrencyName = "P";
        public long startingBalance = 2500;
        public long startingPremiumBalance = 0;
        /// <summary>Hard cap per transaction to contain bugs/exploits.</summary>
        public long maxSingleTransaction = 1000000;
        /// <summary>Coins by finishing position (index 0 = 1st). Beyond the array, last value applies.</summary>
        public long[] coinsByPosition = { 850, 600, 450, 350, 300, 260, 230, 200 };
        public long coinsPerLapCompleted = 25;
        public long coinsDnf = 50;
        public long coinsLastmaEscapeBonus = 100;
    }

    [Serializable]
    public sealed class ProgressionConfig
    {
        public int maxLevel = 60;
        /// <summary>XP required to go from level L to L+1 = baseXp * L^exponent.</summary>
        public float levelBaseXp = 300f;
        public float levelExponent = 1.25f;
        /// <summary>XP by finishing position (index 0 = 1st).</summary>
        public int[] xpByPosition = { 420, 340, 290, 250, 220, 200, 180, 160 };
        public int xpPerLapCompleted = 20;
        public int xpDnf = 40;
        public int xpLastmaEscape = 60;
        public int xpPerOvertake = 4;
        public int xpPerItemHit = 6;
        /// <summary>Elo-style rating used for ranked play.</summary>
        public int ratingStart = 1000;
        public float ratingK = 24f;
        public float ratingSpread = 400f;
        /// <summary>Competitive ranks ordered from lowest. Thresholds are rating floors. Data, not code (PRD §7).</summary>
        public RankTierConfig[] ranks =
        {
            new RankTierConfig("newbie", "Newbie", 0),
            new RankTierConfig("sharp_guy", "Sharp Guy", 1050),
            new RankTierConfig("correct_guy", "Correct Guy", 1120),
            new RankTierConfig("no_dull", "No Dull", 1200),
            new RankTierConfig("omo", "Omo!", 1290),
            new RankTierConfig("na_you_sabi", "Na You Sabi", 1390),
            new RankTierConfig("odogwu", "Odogwu", 1500),
            new RankTierConfig("chairman", "Chairman", 1620),
            new RankTierConfig("wahala_dey", "Wahala Dey", 1750),
            new RankTierConfig("untouchable", "Untouchable", 1900),
        };
    }

    [Serializable]
    public sealed class RankTierConfig
    {
        public string id;
        public string displayName;
        public int minRating;

        public RankTierConfig() { }

        public RankTierConfig(string id, string displayName, int minRating)
        {
            this.id = id;
            this.displayName = displayName;
            this.minRating = minRating;
        }
    }

    [Serializable]
    public sealed class AntiCheatConfig
    {
        /// <summary>Lap faster than this (seconds) is impossible on any track and is rejected.</summary>
        public float absoluteMinLapSeconds = 20f;
        /// <summary>Lap must be at least this fraction of the track's reference lap time.</summary>
        public float minLapFractionOfReference = 0.55f;
        /// <summary>Tolerance multiplier on top speed when validating reported movement.</summary>
        public float maxSpeedTolerance = 1.15f;
        /// <summary>Maximum metres a kart may move in one tick beyond its speed budget before it is a teleport.</summary>
        public float teleportSlackMeters = 3f;
        /// <summary>Maximum input frames accepted per second per client (beyond this, inputs are dropped).</summary>
        public int maxInputFramesPerSecond = 120;
    }
}
