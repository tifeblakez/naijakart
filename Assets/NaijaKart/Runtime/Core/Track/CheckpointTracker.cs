using System;
using NaijaKart.Core.Config;
using NaijaKart.Core.Math;

namespace NaijaKart.Core.Track
{
    public enum CheckpointResult
    {
        None,
        GatePassed,
        LapCompleted,
        RaceFinished,
        /// <summary>A lap was completed faster than physically possible and was rejected (anti-cheat).</summary>
        LapRejected
    }

    /// <summary>
    /// Server-side per-racer checkpoint and lap validation (PRD §14). Gates must be crossed strictly in
    /// order; alternatives inside a gate make shortcuts legal. Laps count only when gate 0 is crossed
    /// after every other gate. Progress is monotonic and used for position ranking.
    /// </summary>
    public sealed class CheckpointTracker
    {
        private readonly TrackDefinition _track;
        private readonly TrackGeometry _geometry;
        private readonly int _totalLaps;
        private readonly float _minLapSeconds;

        public int NextCheckpoint { get; private set; }
        public int LapsCompleted { get; private set; }
        public bool Finished { get; private set; }
        public float CurrentLapStartTime { get; private set; }
        public float BestLapSeconds { get; private set; } = float.MaxValue;
        public float LastLapSeconds { get; private set; }
        public int ShortcutsTaken { get; private set; }
        /// <summary>Gate position of the last validated checkpoint; used as the recovery respawn.</summary>
        public Vec3 LastGatePosition { get; private set; }
        public float LastGateHeading { get; private set; }
        public int CheckpointCount => _track.checkpoints.Length;
        public int TotalLaps => _totalLaps;

        public CheckpointTracker(TrackDefinition track, TrackGeometry geometry, int totalLaps, float minLapSeconds, Vec3 startPosition, float startHeading)
        {
            _track = track ?? throw new ArgumentNullException(nameof(track));
            _geometry = geometry;
            _totalLaps = System.Math.Max(1, totalLaps);
            _minLapSeconds = minLapSeconds;
            NextCheckpoint = 1 % CheckpointCount;
            LastGatePosition = startPosition;
            LastGateHeading = startHeading;
        }

        public void StartLapTimer(float raceTime) => CurrentLapStartTime = raceTime;

        /// <summary>Evaluates the racer's position against the next required gate only (strict order).</summary>
        public CheckpointResult Update(Vec3 position, float raceTime, out bool viaShortcut)
        {
            viaShortcut = false;
            if (Finished) return CheckpointResult.None;

            var cp = _track.checkpoints[NextCheckpoint];
            for (int g = 0; g < cp.gates.Length; g++)
            {
                var gate = cp.gates[g];
                if (Vec3.FlatDistance(position, gate.position) > gate.radius) continue;

                viaShortcut = gate.isShortcut;
                if (viaShortcut) ShortcutsTaken++;
                LastGatePosition = gate.position;
                LastGateHeading = _geometry != null ? _geometry.Project(gate.position).Direction.ToYaw() : LastGateHeading;

                if (NextCheckpoint == 0)
                {
                    float lapTime = raceTime - CurrentLapStartTime;
                    if (lapTime < _minLapSeconds)
                    {
                        // Physically impossible lap: reset to the lap start without credit.
                        NextCheckpoint = 1 % CheckpointCount;
                        return CheckpointResult.LapRejected;
                    }
                    LastLapSeconds = lapTime;
                    if (lapTime < BestLapSeconds) BestLapSeconds = lapTime;
                    LapsCompleted++;
                    CurrentLapStartTime = raceTime;
                    NextCheckpoint = 1 % CheckpointCount;
                    if (LapsCompleted >= _totalLaps)
                    {
                        Finished = true;
                        return CheckpointResult.RaceFinished;
                    }
                    return CheckpointResult.LapCompleted;
                }

                NextCheckpoint = (NextCheckpoint + 1) % CheckpointCount;
                return CheckpointResult.GatePassed;
            }
            return CheckpointResult.None;
        }

        /// <summary>
        /// Monotonic race progress used for live positions: laps + gates + fractional distance to the
        /// next gate. Fraction is clamped so overshooting a gate never counts as progress.
        /// </summary>
        public float Progress(Vec3 position)
        {
            if (Finished) return _totalLaps * CheckpointCount + 1f;
            int passedThisLap = NextCheckpoint == 0 ? CheckpointCount - 1 : NextCheckpoint - 1;
            var next = _track.checkpoints[NextCheckpoint];
            float bestFrac = 0f;
            for (int g = 0; g < next.gates.Length; g++)
            {
                float toNext = Vec3.FlatDistance(position, next.gates[g].position);
                float fromPrev = Vec3.FlatDistance(LastGatePosition, next.gates[g].position);
                float frac = fromPrev > 1e-3f ? MathUtil.Clamp01(1f - toNext / fromPrev) : 0f;
                if (frac > bestFrac) bestFrac = frac;
            }
            return LapsCompleted * CheckpointCount + passedThisLap + bestFrac * 0.99f;
        }

        public bool IsOnFinalLap => !Finished && LapsCompleted == _totalLaps - 1;
    }
}
