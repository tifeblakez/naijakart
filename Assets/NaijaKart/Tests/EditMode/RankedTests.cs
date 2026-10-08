using System;
using NaijaKart.Core.Config;
using NaijaKart.Core.Progression;
using NUnit.Framework;

namespace NaijaKart.Tests.EditMode
{
    /// <summary>Ranked Points, divisions, Rush Hour and seasons (design screens 03.1, 10, 15; ADR-0010).</summary>
    public class RankedTests
    {
        private readonly GameConfig _cfg = new GameConfig();

        [Test]
        public void TiersHaveThreeDivisionsCountedDown()
        {
            var ladder = new RankLadder(_cfg.progression, _cfg.ranked);
            Assert.That(ladder.Label(0), Is.EqualTo("JJC III"));
            Assert.That(ladder.Label(99), Is.EqualTo("JJC III"));
            Assert.That(ladder.Label(100), Is.EqualTo("JJC II"));
            Assert.That(ladder.Label(250), Is.EqualTo("JJC I"));
            Assert.That(ladder.Label(300), Is.EqualTo("Sharp Sharp III"));
            Assert.That(ladder.Label(1588), Is.EqualTo("Chairman III"));
            Assert.That(ladder.Label(1688), Is.EqualTo("Chairman II"));
            Assert.That(ladder.RpInDivision(1688), Is.EqualTo(88));
            Assert.That(ladder.RpToNextDivision(1688), Is.EqualTo(12), "12 RP to Chairman I");
            Assert.That(ladder.TierFor(9000).id, Is.EqualTo("oga_patapata"));
            Assert.That(ladder.AfterSeasonReset(1688), Is.EqualTo(1388), "everyone drops one tier at reset, keeping division progress");
            Assert.That(ladder.AfterSeasonReset(50), Is.EqualTo(0));
        }

        [Test]
        public void RpTableSpreadsAcrossRealRacersAndAiNeverCounts()
        {
            var r = _cfg.ranked;
            Assert.That(RaceSettlementService.RpDeltaFor(1, 8, r, false), Is.EqualTo(24), "win: about +24");
            Assert.That(RaceSettlementService.RpDeltaFor(8, 8, r, false), Is.EqualTo(-12), "last: about -12");
            Assert.That(RaceSettlementService.RpDeltaFor(1, 4, r, false), Is.EqualTo(24), "four real racers still use the full table");
            Assert.That(RaceSettlementService.RpDeltaFor(4, 4, r, false), Is.EqualTo(-12));
            Assert.That(RaceSettlementService.RpDeltaFor(2, 4, r, false), Is.EqualTo(r.rpByPosition[2]));
            Assert.That(RaceSettlementService.RpDeltaFor(1, 8, r, true), Is.EqualTo(30), "Rush Hour +25%");
            Assert.That(RaceSettlementService.RpDeltaFor(8, 8, r, true), Is.EqualTo(-12), "Rush Hour never deepens a loss");
        }

        [Test]
        public void RushHourAndSeasonFollowLagosTime()
        {
            var r = _cfg.ranked;   // 19:00–22:00 at UTC+1
            Assert.That(SeasonClock.IsRushHour(r, new DateTime(2026, 10, 8, 18, 30, 0, DateTimeKind.Utc)), Is.True, "19:30 Lagos");
            Assert.That(SeasonClock.IsRushHour(r, new DateTime(2026, 10, 8, 17, 30, 0, DateTimeKind.Utc)), Is.False, "18:30 Lagos");
            Assert.That(SeasonClock.IsRushHour(r, new DateTime(2026, 10, 8, 21, 0, 0, DateTimeKind.Utc)), Is.False, "22:00 Lagos is over");
            Assert.That(SeasonClock.RushHourSecondsTo(r, new DateTime(2026, 10, 8, 17, 30, 0, DateTimeKind.Utc)), Is.EqualTo(30 * 60), "starts in 30 min");
            Assert.That(SeasonClock.RushHourSecondsTo(r, new DateTime(2026, 10, 8, 19, 0, 0, DateTimeKind.Utc)), Is.EqualTo(2 * 3600), "ends in 2 h");
            Assert.That(SeasonClock.DaysLeft(r, new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc)), Is.EqualTo(23));
            Assert.That(SeasonClock.SeasonEnded(r, new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc)), Is.True);
        }
    }
}
