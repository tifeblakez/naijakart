using System.Collections.Generic;
using NaijaKart.Core.Config;
using NaijaKart.Core.Economy;
using NaijaKart.Core.Race;

namespace NaijaKart.Core.Lastma
{
    public enum LastmaPhase
    {
        None,
        Warning,
        Pursuit,
        FinePending,
        Escaped,
        Resolved,
        Arrested
    }

    /// <summary>Live LASTMA event against one racer.</summary>
    public sealed class LastmaEvent
    {
        public string Id;
        public string TargetPlayerId;
        public LastmaPhase Phase;
        public float PhaseTimeRemaining;
        /// <summary>0..1 — at 1 the racer is caught.</summary>
        public float Pressure;
        public bool BailRequested;
        public string BailPaidBy;
        public long FineAmount;
    }

    /// <summary>
    /// LASTMA pursuit system (PRD §30–§35). Targets any racer by configurable position weights, warns
    /// ("PULL OVER!"), pursues with a pressure model the player can influence (boost, shortcuts, clean
    /// driving), then either escapes or is pulled over. Pulled over: pay fine → continue; call for bail
    /// → a friend pays → continue; nobody pays → arrested → eliminated. Coins never come from real money
    /// inside this system; the wallet is the server ledger.
    /// </summary>
    public sealed class LastmaSystem
    {
        private readonly IRaceContext _ctx;
        private readonly LastmaConfig _cfg;
        private readonly IWallet _wallet;
        private readonly List<LastmaEvent> _events = new List<LastmaEvent>();
        private readonly Dictionary<string, float> _cooldownUntil = new Dictionary<string, float>();
        private float _nextTriggerAt;
        private int _eventCounter;

        public IReadOnlyList<LastmaEvent> Events => _events;

        public LastmaSystem(IRaceContext ctx, IWallet wallet)
        {
            _ctx = ctx;
            _cfg = ctx.Config.lastma;
            _wallet = wallet;
            _nextTriggerAt = _cfg.firstTriggerMinSeconds + ctx.Rng.Range(0f, _cfg.maxIntervalSeconds - _cfg.minIntervalSeconds);
        }

        public LastmaEvent EventFor(string playerId)
        {
            foreach (var e in _events) if (e.TargetPlayerId == playerId) return e;
            return null;
        }

        public void Tick(float dt)
        {
            if (!_cfg.enabled) return;
            UpdateEvents(dt);
            if (_ctx.RaceTime >= _nextTriggerAt && ActiveCount() < _cfg.maxActiveEvents)
            {
                _nextTriggerAt = _ctx.RaceTime + _ctx.Rng.Range(_cfg.minIntervalSeconds, _cfg.maxIntervalSeconds);
                var target = PickTarget();
                if (target != null) Trigger(target);
            }
        }

        /// <summary>Forces an event on a racer (tests, event modes such as "LASTMA Madness").</summary>
        public LastmaEvent Trigger(RaceParticipant target)
        {
            var e = new LastmaEvent
            {
                Id = _ctx.RaceId + ":lastma:" + (++_eventCounter),
                TargetPlayerId = target.PlayerId,
                Phase = LastmaPhase.Warning,
                PhaseTimeRemaining = _cfg.warningSeconds,
                FineAmount = _cfg.fineAmount
            };
            _events.Add(e);
            _cooldownUntil[target.PlayerId] = _ctx.RaceTime + _cfg.targetCooldownSeconds;
            target.Telemetry.LastmaTargeted++;
            _ctx.Emit(RaceEventType.LastmaWarning, target.PlayerId, null, e.Id, 0, _cfg.warningSeconds);
            return e;
        }

        /// <summary>Called by the simulation when the target crosses a shortcut gate during pursuit.</summary>
        public void NotifyShortcut(string playerId)
        {
            var e = EventFor(playerId);
            if (e != null && e.Phase == LastmaPhase.Pursuit)
            {
                e.Pressure = System.Math.Max(0f, e.Pressure - _cfg.shortcutPressureRelief);
            }
        }

        public bool PayFine(string playerId)
        {
            var e = EventFor(playerId);
            if (e == null || e.Phase != LastmaPhase.FinePending) return false;
            if (!_wallet.TryDebit(playerId, e.FineAmount, "lastma_fine", e.Id + ":fine")) return false;
            var p = _ctx.Find(playerId);
            if (p != null) p.Telemetry.LastmaFinesPaid++;
            Resolve(e, RaceEventType.LastmaFinePaid, playerId);
            return true;
        }

        public bool RequestBail(string playerId)
        {
            var e = EventFor(playerId);
            if (e == null || e.Phase != LastmaPhase.FinePending || e.BailRequested) return false;
            e.BailRequested = true;
            var p = _ctx.Find(playerId);
            if (p != null) p.Telemetry.BailRequested++;
            _ctx.Emit(RaceEventType.LastmaBailRequested, playerId, null, e.Id, 0, (float)e.FineAmount);
            return true;
        }

        /// <summary>A friend (in-race or not; the server validates the relationship) pays the fine.</summary>
        public bool PayBail(string payerId, string targetPlayerId)
        {
            var e = EventFor(targetPlayerId);
            if (e == null || e.Phase != LastmaPhase.FinePending || !e.BailRequested) return false;
            if (payerId == targetPlayerId) return false;
            if (!_wallet.TryDebit(payerId, e.FineAmount, "lastma_bail", e.Id + ":bail:" + payerId)) return false;
            e.BailPaidBy = payerId;
            var target = _ctx.Find(targetPlayerId);
            if (target != null) target.Telemetry.BailReceived++;
            var payer = _ctx.Find(payerId);
            if (payer != null) payer.Telemetry.BailGiven++;
            Resolve(e, RaceEventType.LastmaBailed, targetPlayerId, payerId);
            return true;
        }

        private void Resolve(LastmaEvent e, RaceEventType type, string playerId, string by = null)
        {
            e.Phase = LastmaPhase.Resolved;
            var p = _ctx.Find(playerId);
            if (p != null)
            {
                p.State.IsImmobilised = false;
                p.State.StunTimeRemaining = System.Math.Max(p.State.StunTimeRemaining, _cfg.resumeStunSeconds);
            }
            _ctx.Emit(type, playerId, by, e.Id, 0, (float)e.FineAmount);
            _events.Remove(e);
        }

        private void UpdateEvents(float dt)
        {
            for (int i = _events.Count - 1; i >= 0; i--)
            {
                var e = _events[i];
                var p = _ctx.Find(e.TargetPlayerId);
                if (p == null || !p.IsActiveRacer)
                {
                    _events.RemoveAt(i);
                    continue;
                }
                e.PhaseTimeRemaining -= dt;
                switch (e.Phase)
                {
                    case LastmaPhase.Warning:
                        if (e.PhaseTimeRemaining <= 0f)
                        {
                            e.Phase = LastmaPhase.Pursuit;
                            e.PhaseTimeRemaining = _cfg.pursuitSeconds;
                            e.Pressure = 0f;
                            _ctx.Emit(RaceEventType.LastmaPursuitStarted, p.PlayerId, null, e.Id, 0, _cfg.pursuitSeconds);
                        }
                        break;
                    case LastmaPhase.Pursuit:
                        UpdatePressure(e, p, dt);
                        if (e.Pressure >= 1f)
                        {
                            if (_cfg.shieldBlocksCatch && p.State.HasShield)
                            {
                                p.State.ShieldTimeRemaining = 0f;
                                _ctx.Emit(RaceEventType.ShieldBlocked, p.PlayerId, null, "lastma");
                                Escape(e, p, i);
                                break;
                            }
                            e.Phase = LastmaPhase.FinePending;
                            e.PhaseTimeRemaining = _cfg.fineDecisionSeconds;
                            PullOver(p);
                            _ctx.Emit(RaceEventType.LastmaCaught, p.PlayerId, null, e.Id, 0, (float)e.FineAmount);
                        }
                        else if (e.PhaseTimeRemaining <= 0f)
                        {
                            Escape(e, p, i);
                        }
                        break;
                    case LastmaPhase.FinePending:
                        if (e.PhaseTimeRemaining <= 0f)
                        {
                            e.Phase = LastmaPhase.Arrested;
                            _ctx.Emit(RaceEventType.LastmaArrested, p.PlayerId, null, e.Id);
                            _events.RemoveAt(i);
                            _ctx.Eliminate(p, "lastma_arrest");
                        }
                        break;
                }
            }
        }

        /// <summary>Moves the kart to the right-hand roadside and immobilises it ("PULL OVER!").</summary>
        private void PullOver(RaceParticipant p)
        {
            var proj = _ctx.Track.Project(p.State.Position);
            var right = Math.Vec3.Cross(Math.Vec3.Up, proj.Direction);
            float edge = _ctx.Track.Definition.roadHalfWidth + 1.5f;
            p.State.Position = proj.Point + right * edge;
            p.State.Heading = proj.Direction.ToYaw();
            p.State.Speed = 0f;
            p.State.LateralVelocity = 0f;
            p.State.IsImmobilised = true;
            p.State.IsDrifting = false;
            p.State.DriftCharge = 0f;
            p.State.DriftLevel = Vehicle.DriftLevel.None;
        }

        private void Escape(LastmaEvent e, RaceParticipant p, int index)
        {
            e.Phase = LastmaPhase.Escaped;
            p.Telemetry.LastmaEscapes++;
            _ctx.Emit(RaceEventType.LastmaEscaped, p.PlayerId, null, e.Id);
            _events.RemoveAt(index);
        }

        private void UpdatePressure(LastmaEvent e, RaceParticipant p, float dt)
        {
            float gain = _cfg.pressureGainPerSecond;
            var s = p.State;
            if (s.IsStunned) gain += _cfg.pressureGainWhileStunned;
            if (s.IsOffroad) gain += _cfg.pressureGainWhileOffroad;
            if (s.Speed < p.Stats.TopSpeed * _cfg.slowSpeedFraction) gain += _cfg.pressureGainWhileSlow;
            if (s.IsBoosting) gain -= _cfg.pressureLossWhileBoosting;
            e.Pressure = System.Math.Max(0f, System.Math.Min(1f, e.Pressure + gain * dt));
        }

        private int ActiveCount()
        {
            int n = 0;
            foreach (var e in _events) if (e.Phase == LastmaPhase.Warning || e.Phase == LastmaPhase.Pursuit || e.Phase == LastmaPhase.FinePending) n++;
            return n;
        }

        private RaceParticipant PickTarget()
        {
            var active = _ctx.ActiveByPosition;
            if (active.Count == 0) return null;

            // Fairness: no new events once the leader is about to finish.
            var leader = active[0];
            if (leader.Checkpoints.IsOnFinalLap)
            {
                float remaining = _ctx.Track.LapLength * (1f - FractionOfLap(leader));
                float eta = remaining / System.Math.Max(1f, leader.Stats.TopSpeed);
                if (eta < _cfg.noTriggerBeforeFinishSeconds) return null;
            }

            var weights = new float[active.Count];
            bool any = false;
            for (int i = 0; i < active.Count; i++)
            {
                var p = active[i];
                bool onCooldown = _cooldownUntil.TryGetValue(p.PlayerId, out float until) && _ctx.RaceTime < until;
                bool busy = EventFor(p.PlayerId) != null;
                float w = _cfg.positionWeights[System.Math.Min(i, _cfg.positionWeights.Length - 1)];
                weights[i] = onCooldown || busy || p.Status != ParticipantStatus.Connected ? 0f : w;
                any |= weights[i] > 0f;
            }
            if (!any) return null;
            int idx = _ctx.Rng.WeightedIndex(weights);
            return idx >= 0 ? active[idx] : null;
        }

        private float FractionOfLap(RaceParticipant p)
        {
            float progress = p.Checkpoints.Progress(p.State.Position);
            float perLap = p.Checkpoints.CheckpointCount;
            return (progress % perLap) / perLap;
        }
    }
}
