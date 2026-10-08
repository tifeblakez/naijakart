using NaijaKart.Core.Chaos;
using NaijaKart.Core.Config;
using NaijaKart.Core.Math;
using NaijaKart.Core.Race;
using NaijaKart.Core.Vehicle;

namespace NaijaKart.Core.Items
{
    /// <summary>
    /// Applies item effects on the server (PRD §25). Effects are selected by ItemEffectType so new items
    /// are data rows. Targeting respects shields (one block) and Sharp Guy (auto-avoid one hazard).
    /// </summary>
    public sealed class ItemEffectSystem
    {
        private readonly IRaceContext _ctx;
        private readonly RoadEventScheduler _roadEvents;

        public ItemEffectSystem(IRaceContext ctx, RoadEventScheduler roadEvents)
        {
            _ctx = ctx;
            _roadEvents = roadEvents;
        }

        public bool Use(RaceParticipant user, ItemDefinition item)
        {
            if (item == null) return false;
            var cfg = _ctx.Config;
            var model = _ctx.VehicleModel;
            _ctx.Emit(RaceEventType.ItemUsed, user.PlayerId, null, item.id);
            user.Telemetry.ItemsUsed++;

            switch (item.effect)
            {
                case ItemEffectType.SpeedBoost:
                {
                    bool was = user.State.IsBoosting;
                    model.ApplyBoost(ref user.State, item.duration * user.Stats.BoostDurationScale, item.magnitude);
                    if (!was) _ctx.Emit(RaceEventType.BoostStarted, user.PlayerId, null, item.id);
                    user.Telemetry.Boosts++;
                    return true;
                }
                case ItemEffectType.Shield:
                    user.State.ShieldTimeRemaining = System.Math.Max(user.State.ShieldTimeRemaining, item.duration);
                    return true;
                case ItemEffectType.Cleanse:
                    user.State.WobbleTimeRemaining = 0f;
                    user.State.WobbleAmplitude = 0f;
                    user.State.BlindTimeRemaining = 0f;
                    user.State.StunTimeRemaining = 0f;
                    user.State.ExternalGripMultiplier = 1f;
                    user.State.ExternalGripMultiplierTime = 0f;
                    if (user.State.ExternalSpeedMultiplier < 1f)
                    {
                        user.State.ExternalSpeedMultiplier = 1f;
                        user.State.ExternalSpeedMultiplierTime = 0f;
                    }
                    return true;
                case ItemEffectType.AutoAvoid:
                    user.State.AutoAvoidCharges += System.Math.Max(1, (int)item.magnitude);
                    return true;
                case ItemEffectType.BlindTarget:
                case ItemEffectType.WobbleTarget:
                {
                    var target = ResolveTarget(user, item.targeting);
                    if (target == null) return false;
                    if (TryBlockWithShield(target, user, item)) return true;
                    if (item.effect == ItemEffectType.BlindTarget)
                    {
                        target.State.BlindTimeRemaining = System.Math.Max(target.State.BlindTimeRemaining, item.duration);
                    }
                    else
                    {
                        target.State.WobbleTimeRemaining = System.Math.Max(target.State.WobbleTimeRemaining, item.duration);
                        target.State.WobbleAmplitude = item.magnitude;
                    }
                    RegisterHit(user, target, item);
                    return true;
                }
                case ItemEffectType.DropSpikeStrip:
                {
                    Vec3 pos = user.State.Position - user.State.Forward * cfg.items.dropBehindDistance;
                    var h = _ctx.Hazards.Spawn(HazardKind.SpikeStrip, pos, item.magnitude, item.duration, 0f, user.PlayerId);
                    if (h != null)
                    {
                        var e = _ctx.Emit(RaceEventType.HazardSpawned, user.PlayerId, null, h.Id);
                        e.Position = pos;
                    }
                    return h != null;
                }
                case ItemEffectType.DropOilPatch:
                {
                    Vec3 pos = user.State.Position - user.State.Forward * cfg.items.dropBehindDistance;
                    var h = _ctx.Hazards.Spawn(HazardKind.OilPatch, pos, item.magnitude, item.duration, 0f, user.PlayerId);
                    if (h != null)
                    {
                        var e = _ctx.Emit(RaceEventType.HazardSpawned, user.PlayerId, null, h.Id);
                        e.Position = pos;
                    }
                    return h != null;
                }
                case ItemEffectType.SpawnDanfoCrossing:
                case ItemEffectType.SpawnOkadaCrossing:
                {
                    var anchor = ResolveTarget(user, item.targeting) ?? user;
                    var kind = item.effect == ItemEffectType.SpawnDanfoCrossing ? RoadEventKind.DanfoCross : RoadEventKind.OkadaCross;
                    var h = _roadEvents.Spawn(kind, anchor, item.magnitude);
                    if (h != null) h.OwnerPlayerId = user.PlayerId;
                    return h != null;
                }
            }
            return false;
        }

        /// <summary>Called by the simulation when a kart touches an armed hazard. Returns true if the hazard applied.</summary>
        public bool ApplyHazardContact(RaceParticipant victim, Hazard h)
        {
            var cfg = _ctx.Config;
            var model = _ctx.VehicleModel;

            if (h.IsZone)
            {
                // Zones apply a continuous modifier while inside; refreshed each tick.
                switch (h.Kind)
                {
                    case HazardKind.GoSlowZone:
                        SetSpeedMul(ref victim.State, cfg.roadEvents.goSlowSpeedMultiplier, 0.2f);
                        break;
                    case HazardKind.FloodZone:
                        SetSpeedMul(ref victim.State, cfg.roadEvents.floodSpeedMultiplier, 0.2f);
                        SetGripMul(ref victim.State, cfg.roadEvents.floodGripMultiplier, 0.2f);
                        break;
                    case HazardKind.PoliceCheckpoint:
                        SetSpeedMul(ref victim.State, 0.35f, 0.2f);
                        break;
                }
                return true;
            }

            if (victim.State.HazardImmunityTime > 0f) return false;

            if (victim.State.AutoAvoidCharges > 0)
            {
                victim.State.AutoAvoidCharges--;
                _ctx.Hazards.Remove(h);
                _ctx.Emit(RaceEventType.ShieldBlocked, victim.PlayerId, h.OwnerPlayerId, "sharp_guy");
                return false;
            }
            if (victim.State.HasShield)
            {
                victim.State.ShieldTimeRemaining = 0f;
                _ctx.Hazards.Remove(h);
                _ctx.Emit(RaceEventType.ShieldBlocked, victim.PlayerId, h.OwnerPlayerId, h.Kind.ToString());
                return false;
            }

            switch (h.Kind)
            {
                case HazardKind.OilPatch:
                    SetGripMul(ref victim.State, 0.15f, 1.2f);
                    victim.State.LateralVelocity += (victim.State.LateralVelocity >= 0f ? 1f : -1f) * 6f + 4f * (_ctx.Rng.NextFloat() - 0.5f);
                    victim.State.IsDrifting = false;
                    victim.State.DriftCharge = 0f;
                    victim.State.DriftLevel = DriftLevel.None;
                    _ctx.Hazards.Remove(h);
                    break;
                case HazardKind.Pothole:
                    victim.State.Speed *= cfg.roadEvents.potholeSpeedRetention;
                    victim.State.BoostTimeRemaining = 0f;
                    break;
                case HazardKind.SpikeStrip:
                    model.ApplyStun(ref victim.State, cfg.items.spikeStunSeconds, cfg.items.spikeSpeedRetention);
                    _ctx.Hazards.Remove(h);
                    break;
                case HazardKind.DanfoCrossing:
                case HazardKind.OkadaCrossing:
                case HazardKind.TrafficCar:
                    model.ApplyStun(ref victim.State, cfg.collision.hardHazardStunSeconds, cfg.collision.hardHazardSpeedRetention);
                    if (h.Kind != HazardKind.TrafficCar)
                    {
                        _ctx.Hazards.Remove(h);
                    }
                    else
                    {
                        // Traffic stays on the road: shove the kart out of its lane so it can get past.
                        float side = Vec3.Dot((victim.State.Position - h.Position).Flat, victim.State.Right) >= 0f ? 1f : -1f;
                        victim.State.Position = victim.State.Position + victim.State.Right * (side * cfg.collision.trafficLateralShove);
                    }
                    break;
            }
            victim.State.HazardImmunityTime = cfg.collision.hazardImmunitySeconds;
            victim.Telemetry.HazardHits++;
            var e = _ctx.Emit(RaceEventType.HazardHit, victim.PlayerId, h.OwnerPlayerId, h.Kind.ToString());
            e.Position = h.Position;
            if (h.OwnerPlayerId != null && h.OwnerPlayerId != victim.PlayerId)
            {
                var owner = _ctx.Find(h.OwnerPlayerId);
                if (owner != null)
                {
                    owner.Telemetry.ItemHitsLanded++;
                    victim.Telemetry.ItemHitsTaken++;
                    _ctx.Emit(RaceEventType.ItemHit, owner.PlayerId, victim.PlayerId, h.Kind.ToString());
                }
            }
            return true;
        }

        private RaceParticipant ResolveTarget(RaceParticipant user, ItemTargeting targeting)
        {
            switch (targeting)
            {
                case ItemTargeting.Self: return user;
                case ItemTargeting.Leader:
                {
                    var leader = _ctx.Leader;
                    return leader == user ? null : leader;
                }
                case ItemTargeting.NearestAhead:
                    return _ctx.NearestAhead(user, _ctx.Config.items.forwardTargetRange);
                case ItemTargeting.Behind:
                {
                    var active = _ctx.ActiveByPosition;
                    for (int i = 0; i < active.Count; i++)
                        if (active[i] == user && i + 1 < active.Count) return active[i + 1];
                    return null;
                }
                default:
                    return _ctx.NearestAhead(user, _ctx.Config.items.forwardTargetRange);
            }
        }

        private bool TryBlockWithShield(RaceParticipant target, RaceParticipant attacker, ItemDefinition item)
        {
            if (!target.State.HasShield) return false;
            target.State.ShieldTimeRemaining = 0f;
            _ctx.Emit(RaceEventType.ShieldBlocked, target.PlayerId, attacker.PlayerId, item.id);
            return true;
        }

        private void RegisterHit(RaceParticipant attacker, RaceParticipant target, ItemDefinition item)
        {
            attacker.Telemetry.ItemHitsLanded++;
            target.Telemetry.ItemHitsTaken++;
            _ctx.Emit(RaceEventType.ItemHit, attacker.PlayerId, target.PlayerId, item.id);
        }

        private static void SetSpeedMul(ref VehicleState s, float mul, float seconds)
        {
            s.ExternalSpeedMultiplier = System.Math.Min(s.ExternalSpeedMultiplier <= 0f ? 1f : s.ExternalSpeedMultiplier, mul);
            s.ExternalSpeedMultiplierTime = System.Math.Max(s.ExternalSpeedMultiplierTime, seconds);
        }

        private static void SetGripMul(ref VehicleState s, float mul, float seconds)
        {
            s.ExternalGripMultiplier = System.Math.Min(s.ExternalGripMultiplier <= 0f ? 1f : s.ExternalGripMultiplier, mul);
            s.ExternalGripMultiplierTime = System.Math.Max(s.ExternalGripMultiplierTime, seconds);
        }
    }
}
