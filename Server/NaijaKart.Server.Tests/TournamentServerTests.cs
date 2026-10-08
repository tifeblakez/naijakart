using System.Linq;
using NaijaKart.Core.Net;
using NaijaKart.Core.Tournaments;
using NaijaKart.Server.Config;
using NUnit.Framework;

namespace NaijaKart.Server.Tests
{
    /// <summary>Tournaments (PRD §8, design 17): rounds, points, advancement, champion rewards.</summary>
    public class TournamentServerTests
    {
        [Test]
        public void TwoRoundTournamentCrownsAChampionAndPaysLooksOnly()
        {
            var h = new ServerHarness(g => { g.lastma.enabled = false; g.roadEvents.enabled = false; g.raceRules.lobbyReadyTimeoutSeconds = 2f; });
            ((JsonConfigSource)h.Content).Tournaments = new TournamentLibrary
            {
                tournaments = new[]
                {
                    new TournamentDefinition
                    {
                        id = "test_cup", name = "Test Cup", maxEntrants = 8, minRealPlayers = 2, fillWithAi = true, laps = 1, trackIds = new[] { h.Content.TrackIds[0] },
                        rounds = new[] { new TournamentRoundDefinition { name = "Round 1", races = 1, advance = 2 }, new TournamentRoundDefinition { name = "Final", races = 1, advance = 0 } },
                        pointsByPosition = new[] { 10, 8, 6 }, coinsPerRace = 200, championCosmeticId = "banner_eko_for_show", finalistCosmeticId = "banner_season_free", finalistsCount = 2, secondsBetweenRaces = 1
                    }
                }
            };
            var a = h.NewClient("a", 0.9f); var b = h.NewClient("b", 0.6f); var c = h.NewClient("c", 0.5f);
            a.Connect(); b.Connect(); c.Connect(); h.Run(0.1f, a, b, c);

            a.Send(new ClientEnvelope { Kind = ClientMessageKind.GetTournament });
            h.Run(0.1f, a, b, c);
            Assert.That(a.Tournament.Name, Is.EqualTo("Test Cup"));
            Assert.That(a.Tournament.Entered, Is.False);
            Assert.That(a.Tournament.Rounds.Select(r => r.Name), Is.EqualTo(new[] { "Round 1", "Final" }));
            Assert.That(a.Tournament.ChampionRewardName, Is.EqualTo("Eko for show"));

            foreach (var cl in new[] { a, b, c }) cl.Send(new ClientEnvelope { Kind = ClientMessageKind.EnterTournament, Text = "test_cup" });
            h.Run(0.1f, a, b, c);
            Assert.That(a.Tournament.Entrants, Is.EqualTo(3));
            Assert.That(a.Tournament.Entered, Is.True);

            // Round 1: everyone is seated in one tournament room (AI fills the rest), it auto-starts.
            Assert.That(h.RunUntil(() => a.Room != null && a.Room.Mode == Core.Simulation.RaceMode.Tournament, 5f, a, b, c), Is.True, "tournament race seats the entrants");
            Assert.That(a.Room.Members.Count(m => m.IsBot), Is.EqualTo(h.Content.Game.simulation.maxPlayersPerRace - 3));
            a.Send(new ClientEnvelope { Kind = ClientMessageKind.EnterTournament, Text = "test_cup" });
            h.Run(0.1f, a, b, c);
            Assert.That(a.Errors.Last(), Does.Contain("started"));
            long coinsBefore = h.Server.Ledger.GetBalance("a");
            Assert.That(h.RunUntil(() => a.Results != null, 300f, a, b, c), Is.True, "round 1 race finishes");
            h.Run(0.5f, a, b, c);
            Assert.That(h.Server.Ledger.GetBalance("a") - coinsBefore, Is.GreaterThanOrEqualTo(200), "coins every tournament race");
            var t = h.Server.Tournament("test_cup");
            Assert.That(t.State.RoundIndex, Is.EqualTo(1), "round 1 done");
            Assert.That(t.State.Alive().Count, Is.EqualTo(2), "top 2 advance");
            Assert.That(t.State.Standings()[0].Points, Is.EqualTo(10));
            Assert.That(a.Tournament.Standings.Length, Is.EqualTo(3));
            Assert.That(a.Tournament.Standings.Count(s => s.Note == "Out"), Is.EqualTo(1));

            // Final: the next race starts after the gap and crowns the champion; rewards are cosmetics only.
            foreach (var cl in new[] { a, b, c }) { cl.Send(new ClientEnvelope { Kind = ClientMessageKind.LeaveRoom }); }
            h.Run(0.2f, a, b, c);
            Assert.That(h.RunUntil(() => t.State.RacesInProgress.Count > 0, 10f, a, b, c), Is.True, "final seats the finalists");
            Assert.That(h.RunUntil(() => t.State.Finished, 300f, a, b, c), Is.True, "tournament finishes");
            h.Run(0.5f, a, b, c);
            Assert.That(t.State.ChampionId, Is.Not.Null);
            var champ = h.Server.Profiles.Get(t.State.ChampionId);
            Assert.That(champ.TournamentWins, Is.EqualTo(1));
            Assert.That(champ.OwnedCosmeticIds, Does.Contain("banner_eko_for_show"));
            var finalist = t.State.Standings().First(e => e.PlayerId != t.State.ChampionId && e.Alive);
            Assert.That(h.Server.Profiles.Get(finalist.PlayerId).OwnedCosmeticIds, Does.Contain("banner_season_free"));
            var viewer = new[] { a, b, c }.First(x => x.PlayerId == t.State.ChampionId);
            Assert.That(viewer.Tournament.Finished, Is.True);
            Assert.That(viewer.Tournament.Standings[0].Note, Is.EqualTo("Champion"));
        }
    }
}
