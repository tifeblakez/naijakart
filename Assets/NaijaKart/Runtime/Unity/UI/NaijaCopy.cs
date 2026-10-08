using NaijaKart.Core.Race;

namespace NaijaKart.Unity.UI
{
    /// <summary>
    /// The game speaks Nigerian (PRD §54, §75). All player-facing strings go through here so copy can
    /// be tuned (and localised) without touching presenters. Keep it understandable across Nigeria.
    /// </summary>
    public static class NaijaCopy
    {
        public const string Ranked = "WHO GET MOUTH?";
        public const string QuickRace = "QUICK RACE";
        public const string Rematch = "RUN AM BACK";
        public const string Victory = "YOU SCATTER AM";
        public const string Defeat = "DEM DO YOU";
        public const string PerfectRace = "NO WAHALA";
        public const string PersonalBest = "YOU DEY IMPROVE";
        public const string Streak = "YOU DEY HOT";
        public const string FinalLap = "LAST LAP, NO SHAKING";
        public const string Overtaken = "OMO! DEM DEY COME!";
        public const string Overtake = "+1";
        public const string LastmaWarning = "PULL OVER!";
        public const string LastmaEscaped = "YOU ESCAPE!";
        public const string LastmaCaught = "DEM DON CATCH YOU";
        public const string PayFine = "PAY FINE";
        public const string CallForBail = "CALL FOR BAIL";
        public const string Arrested = "YOU'RE LOCKED UP!";
        public const string ItemHit = "OMO!";
        public const string Boost = "FULL TANK!";
        public const string Home = "HOME";
        public const string Searching = "DEY FIND PEOPLE...";
        public const string Countdown3 = "3";
        public const string Countdown2 = "2";
        public const string Countdown1 = "1";
        public const string Go = "GO!";
        public const string YourWahala = "YOUR WAHALA";

        public static string NeedsBail(string displayName) => displayName + " needs bail!";
        public static string RoomInvite(string displayName, string code) => $"{displayName} created a Naija Kart room. Join am: {code}";

        public static string Ordinal(int position)
        {
            if (position <= 0) return "DNF";
            int mod100 = position % 100;
            int mod10 = position % 10;
            string suffix = mod100 >= 11 && mod100 <= 13 ? "TH" : mod10 == 1 ? "ST" : mod10 == 2 ? "ND" : mod10 == 3 ? "RD" : "TH";
            return position + suffix;
        }

        public static string FormatTime(float seconds)
        {
            if (seconds <= 0f) return "--:--.--";
            int m = (int)(seconds / 60f);
            float s = seconds - m * 60f;
            return $"{m}:{s:00.00}";
        }

        public static string ForEvent(RaceEvent e, string localPlayerId)
        {
            bool mine = e.PlayerId == localPlayerId;
            bool targetsMe = e.TargetPlayerId == localPlayerId;
            switch (e.Type)
            {
                case RaceEventType.FinalLapStarted: return FinalLap;
                case RaceEventType.Overtake: return mine ? Overtake : null;
                case RaceEventType.LastmaWarning: return mine ? LastmaWarning : null;
                case RaceEventType.LastmaEscaped: return mine ? LastmaEscaped : null;
                case RaceEventType.LastmaCaught: return mine ? LastmaCaught : null;
                case RaceEventType.LastmaArrested: return mine ? Arrested : null;
                case RaceEventType.ItemHit: return targetsMe ? ItemHit : null;
                case RaceEventType.HazardHit: return mine ? ItemHit : null;
                case RaceEventType.BoostStarted: return mine ? Boost : null;
                case RaceEventType.CountdownTick: return e.IntValue.ToString();
                case RaceEventType.RaceStarted: return Go;
                default: return null;
            }
        }
    }
}
