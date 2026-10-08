using System;
using NaijaKart.Core.Math;

namespace NaijaKart.Core.Config
{
    /// <summary>Vehicle stats on a 0..100 scale (PRD §18). Converted to physical ranges by DrivingConfig.</summary>
    [Serializable]
    public sealed class VehicleDefinition
    {
        public string id;
        public string displayName;
        /// <summary>Which real-world Nigerian vehicle the silhouette must read as (PRD §19).</summary>
        public string sourceVehicle;
        public string description;
        public int speed = 50;
        public int acceleration = 50;
        public int handling = 50;
        public int drift = 50;
        public int weight = 50;
        public int boost = 50;
        public int traction = 50;
        /// <summary>Player level required to unlock. 0 = starter.</summary>
        public int unlockLevel = 0;
        /// <summary>Price in earned Coins (0 = free once the level is reached). Never premium currency: no pay-to-win.</summary>
        public long priceCoins = 0;
        /// <summary>Addressable/Resources key for the view prefab. Presentation only.</summary>
        public string prefabKey;
        public string tagline;
    }

    [Serializable]
    public sealed class CharacterDefinition
    {
        public string id;
        public string displayName;
        public string archetype;
        public string personality;
        /// <summary>Stat the character favours. Small, data-driven bonus; never pay-to-win.</summary>
        public string strengthStat;
        /// <summary>Bonus added to the strength stat (0..100 scale) when paired with any vehicle.</summary>
        public int strengthBonus = 5;
        public string weaknessStat;
        public int weaknessPenalty = 3;
        public int unlockLevel = 0;
        public long priceCoins = 0;
        public string prefabKey;
        public string[] voiceLineKeys = Array.Empty<string>();
    }

    public enum ItemCategory { Boost, Defense, Attack, Chaos, Utility }

    /// <summary>
    /// Effect handlers are selected by this enum; new items are new data rows unless they need a new effect type.
    /// </summary>
    public enum ItemEffectType
    {
        SpeedBoost,
        Shield,
        Cleanse,
        BlindTarget,
        WobbleTarget,
        DropOilPatch,
        SpawnDanfoCrossing,
        SpawnOkadaCrossing,
        AutoAvoid,
        DropSpikeStrip
    }

    public enum ItemTargeting
    {
        Self,
        NearestAhead,
        Leader,
        Behind,
        Area
    }

    [Serializable]
    public sealed class ItemDefinition
    {
        public string id;
        public string displayName;
        public string flavourText;
        public ItemCategory category;
        public ItemEffectType effect;
        public ItemTargeting targeting = ItemTargeting.Self;
        public string rarity = "common";
        /// <summary>Effect duration in seconds where applicable.</summary>
        public float duration = 2f;
        /// <summary>Effect magnitude: boost multiplier, wobble amplitude, hazard radius, etc.</summary>
        public float magnitude = 1f;
        /// <summary>Seconds before this player can be granted the same item again.</summary>
        public float cooldown = 0f;
        /// <summary>Roll weight by race position (index 0 = 1st). Length is padded with the last value.</summary>
        public float[] positionWeights = { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f };
        /// <summary>If true the item is never rolled (kept in library for events).</summary>
        public bool disabled = false;
        public string vfxKey;
        public string sfxKey;
        public string iconKey;
    }

    [Serializable]
    public sealed class ItemLibrary
    {
        public ItemDefinition[] items = Array.Empty<ItemDefinition>();
    }

    [Serializable]
    public sealed class VehicleRoster
    {
        public VehicleDefinition[] vehicles = Array.Empty<VehicleDefinition>();
    }

    [Serializable]
    public sealed class CharacterRoster
    {
        public CharacterDefinition[] characters = Array.Empty<CharacterDefinition>();
    }

    /// <summary>
    /// One mandatory gate the racer must cross, in order. A gate may have several alternative positions
    /// (main route, risk route, shortcut). Crossing any alternative satisfies the gate; shortcuts are
    /// therefore legal by construction and skipping a gate is impossible (PRD §14, §47).
    /// </summary>
    [Serializable]
    public sealed class CheckpointDefinition
    {
        public string id;
        public CheckpointGate[] gates = Array.Empty<CheckpointGate>();
    }

    [Serializable]
    public sealed class CheckpointGate
    {
        public Vec3 position;
        public float radius = 6f;
        /// <summary>Marks the gate as a shortcut/risk-route alternative (used for highlights and LASTMA relief).</summary>
        public bool isShortcut;
        public string label;
    }

    [Serializable]
    public sealed class ItemBoxDefinition
    {
        public string id;
        public Vec3 position;
    }

    [Serializable]
    public sealed class RoadEventAnchor
    {
        public string id;
        /// <summary>Distance along the centreline where the event appears (metres).</summary>
        public float distanceAlongTrack;
        /// <summary>RoadEventKind names allowed at this anchor. Empty = any.</summary>
        public string[] allowedKinds = Array.Empty<string>();
    }

    /// <summary>An open secondary road (shortcut / risk route) with its own width. Karts on it are on-road.</summary>
    [Serializable]
    public sealed class ShortcutRoad
    {
        public string id;
        public Vec3[] points = Array.Empty<Vec3>();
        public float halfWidth = 4f;
        /// <summary>Surface modifiers for the shortcut (e.g. gravel under the bridge).</summary>
        public float gripMultiplier = 1f;
        public float speedMultiplier = 1f;
    }

    [Serializable]
    public sealed class TrackDefinition
    {
        public string id;
        public string displayName;
        public string city;
        public string description;
        /// <summary>Closed centreline polyline. Index 0 is the start/finish line.</summary>
        public Vec3[] centreline = Array.Empty<Vec3>();
        public float roadHalfWidth = 7f;
        public ShortcutRoad[] shortcutRoads = Array.Empty<ShortcutRoad>();
        public CheckpointDefinition[] checkpoints = Array.Empty<CheckpointDefinition>();
        public ItemBoxDefinition[] itemBoxes = Array.Empty<ItemBoxDefinition>();
        public RoadEventAnchor[] roadEventAnchors = Array.Empty<RoadEventAnchor>();
        /// <summary>Expected lap time for a competent racer (seconds). Used by anti-cheat and matchmaking.</summary>
        public float referenceLapSeconds = 65f;
        /// <summary>Global surface modifiers for weather tracks (Port Harcourt rain).</summary>
        public float baseGripMultiplier = 1f;
        public float baseSpeedMultiplier = 1f;
        public string sceneKey;
    }
}
