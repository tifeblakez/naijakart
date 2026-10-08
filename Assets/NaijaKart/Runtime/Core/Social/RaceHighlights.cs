using System.Collections.Generic;
using NaijaKart.Core.Race;

namespace NaijaKart.Core.Social
{
    /// <summary>One Wahala highlight line shown on results and share cards (PRD §57, §58).</summary>
    public struct Highlight
    {
        public string Id;
        public string Label;
        public int Value;
    }

    public static class RaceHighlights
    {
        /// <summary>Builds the "YOUR WAHALA" list from telemetry. Labels are Naija copy (PRD §54).</summary>
        public static List<Highlight> Build(ParticipantStats t, int finishPosition, int positionAtFinalLap)
        {
            var list = new List<Highlight>();
            if (t.LastmaEscapes > 0) list.Add(new Highlight { Id = "lastma_escape", Label = "LASTMA ESCAPE", Value = t.LastmaEscapes });
            if (t.ItemHitsLanded > 0) list.Add(new Highlight { Id = "players_hit", Label = "PLAYERS HIT", Value = t.ItemHitsLanded });
            if (t.Overtakes > 0) list.Add(new Highlight { Id = "overtakes", Label = "OVERTAKES", Value = t.Overtakes });
            int comeback = t.WorstPosition - finishPosition;
            if (finishPosition > 0 && comeback >= 3) list.Add(new Highlight { Id = "comeback", Label = "BIGGEST COMEBACK", Value = comeback });
            if (finishPosition == 1 && positionAtFinalLap >= 4) list.Add(new Highlight { Id = "final_lap_comeback", Label = "FINAL LAP COMEBACK", Value = positionAtFinalLap });
            if (t.PurpleDrifts > 0) list.Add(new Highlight { Id = "perfect_drift", Label = "PERFECT DRIFT", Value = t.PurpleDrifts });
            if (t.ShortcutsTaken > 0) list.Add(new Highlight { Id = "shortcut_master", Label = "SHORTCUT MASTER", Value = t.ShortcutsTaken });
            if (t.BailGiven > 0) list.Add(new Highlight { Id = "bail_given", Label = "YOU BAILED A FRIEND", Value = t.BailGiven });
            if (finishPosition == 1 && t.ItemsUsed == 0) list.Add(new Highlight { Id = "no_dull", Label = "NO DULL (NO ITEMS)", Value = 1 });
            return list;
        }
    }
}
