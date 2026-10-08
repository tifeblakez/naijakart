namespace NaijaKart.Core.Config
{
    /// <summary>
    /// Provides deserialised configuration to the simulation. Implementations: JSON files on the server
    /// (System.Text.Json), StreamingAssets on the client (Newtonsoft/JsonUtility), and in-memory for tests.
    /// </summary>
    public interface IConfigSource
    {
        GameConfig Game { get; }
        ItemLibrary Items { get; }
        VehicleRoster Vehicles { get; }
        CharacterRoster Characters { get; }
        Challenges.ChallengeLibrary Challenges { get; }
        TrackDefinition GetTrack(string trackId);
        string[] TrackIds { get; }
    }

    public sealed class InMemoryConfigSource : IConfigSource
    {
        private readonly System.Collections.Generic.Dictionary<string, TrackDefinition> _tracks =
            new System.Collections.Generic.Dictionary<string, TrackDefinition>();

        public GameConfig Game { get; set; } = new GameConfig();
        public ItemLibrary Items { get; set; } = new ItemLibrary();
        public VehicleRoster Vehicles { get; set; } = new VehicleRoster();
        public CharacterRoster Characters { get; set; } = new CharacterRoster();
        public Challenges.ChallengeLibrary Challenges { get; set; } = new Challenges.ChallengeLibrary();

        public string[] TrackIds
        {
            get
            {
                var ids = new string[_tracks.Count];
                _tracks.Keys.CopyTo(ids, 0);
                return ids;
            }
        }

        public InMemoryConfigSource AddTrack(TrackDefinition track)
        {
            _tracks[track.id] = track;
            return this;
        }

        public TrackDefinition GetTrack(string trackId) =>
            _tracks.TryGetValue(trackId, out var t) ? t : null;
    }

    /// <summary>Validates loaded configuration so bad data fails fast at boot rather than mid-race.</summary>
    public static class ConfigValidator
    {
        public static System.Collections.Generic.List<string> Validate(IConfigSource source)
        {
            var errors = new System.Collections.Generic.List<string>();
            var g = source.Game;
            if (g == null) { errors.Add("GameConfig missing"); return errors; }
            if (g.simulation.tickRate <= 0) errors.Add("simulation.tickRate must be > 0");
            if (g.simulation.maxPlayersPerRace <= 0 || g.simulation.maxPlayersPerRace > 16) errors.Add("simulation.maxPlayersPerRace out of range");
            if (g.driving.maxTopSpeed < g.driving.minTopSpeed) errors.Add("driving top speed range inverted");
            if (g.drift.levelThresholds.Length != 3 || g.drift.levelBoostDuration.Length != 3 || g.drift.levelBoostMultiplier.Length != 3)
                errors.Add("drift level arrays must have exactly 3 entries (blue, orange, purple)");
            for (int i = 1; i < g.drift.levelThresholds.Length; i++)
                if (g.drift.levelThresholds[i] <= g.drift.levelThresholds[i - 1]) errors.Add("drift.levelThresholds must be increasing");
            if (g.tilt.fullSteerAngleDegrees <= g.tilt.deadZoneDegrees) errors.Add("tilt.fullSteerAngleDegrees must exceed deadZoneDegrees");
            if (g.raceRules.defaultLaps <= 0) errors.Add("raceRules.defaultLaps must be > 0");
            if (g.lastma.fineAmount < 0) errors.Add("lastma.fineAmount must be >= 0");
            if (g.lastma.positionWeights == null || g.lastma.positionWeights.Length == 0) errors.Add("lastma.positionWeights empty");
            if (g.progression.ranks == null || g.progression.ranks.Length == 0) errors.Add("progression.ranks empty");
            else
            {
                for (int i = 1; i < g.progression.ranks.Length; i++)
                    if (g.progression.ranks[i].minRating <= g.progression.ranks[i - 1].minRating)
                        errors.Add("progression.ranks must have strictly increasing minRating");
            }
            if (source.Items == null || source.Items.items == null) errors.Add("ItemLibrary missing");
            else
            {
                var seen = new System.Collections.Generic.HashSet<string>();
                foreach (var item in source.Items.items)
                {
                    if (string.IsNullOrEmpty(item.id)) errors.Add("item with empty id");
                    else if (!seen.Add(item.id)) errors.Add("duplicate item id " + item.id);
                    if (item.positionWeights == null || item.positionWeights.Length == 0) errors.Add("item " + item.id + " has no positionWeights");
                }
            }
            if (source.Vehicles == null || source.Vehicles.vehicles == null || source.Vehicles.vehicles.Length == 0) errors.Add("VehicleRoster empty");
            if (source.Challenges != null && source.Challenges.challenges != null)
            {
                var seenC = new System.Collections.Generic.HashSet<string>();
                foreach (var c in source.Challenges.challenges)
                {
                    if (string.IsNullOrEmpty(c.id)) errors.Add("challenge with empty id");
                    else if (!seenC.Add(c.id)) errors.Add("duplicate challenge id " + c.id);
                    if (c.target <= 0) errors.Add("challenge " + c.id + " target must be > 0");
                }
            }
            if (g.liveEvents != null && g.liveEvents.weekdayRules != null)
            {
                foreach (var r in g.liveEvents.weekdayRules)
                    if (r.weekday < 0 || r.weekday > 6) errors.Add("liveEvents rule " + r.id + " weekday out of range");
            }
            foreach (var trackId in source.TrackIds)
            {
                var t = source.GetTrack(trackId);
                if (t.centreline == null || t.centreline.Length < 3) errors.Add("track " + trackId + " centreline needs >= 3 points");
                if (t.checkpoints == null || t.checkpoints.Length < 2) errors.Add("track " + trackId + " needs >= 2 checkpoints");
                else
                {
                    foreach (var cp in t.checkpoints)
                        if (cp.gates == null || cp.gates.Length == 0) errors.Add("track " + trackId + " checkpoint " + cp.id + " has no gates");
                }
                if (t.roadHalfWidth <= 0) errors.Add("track " + trackId + " roadHalfWidth must be > 0");
            }
            return errors;
        }
    }
}
