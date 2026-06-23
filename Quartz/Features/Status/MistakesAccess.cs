namespace Quartz.Features.Status
{
    internal static class MistakesAccess
    {
        internal static scrMistakesManager Get()
        {
            scrController ctrl = scrController.instance;
            return ctrl != null ? ctrl.mistakesManager : null;
        }

        internal static float PercentAcc(scrMistakesManager m)
        {
            return m != null ? m.percentAcc : 1f;
        }

        internal static float PercentXAcc(scrMistakesManager m)
        {
            return m != null ? m.percentXAcc : 1f;
        }

        internal static float PercentComplete(scrMistakesManager m)
        {
            return m != null ? m.percentComplete : 0f;
        }


        internal static int PlayerCount()
        {
            try { return scrPlayerManager.playerCount; }
            catch { return 1; }
        }

        internal static bool CoopMode()
        {
            try { return scrController.coopMode; }
            catch { return false; }
        }

        internal static scrMarginTracker Tracker(int playerID)
        {
            try
            {
                scrPlayerManager pm = ADOBase.playerManager;
                if (pm == null) return null;
                scrPlayer[] players = pm.players;
                if (players == null || playerID < 0 || playerID >= players.Length) return null;
                scrPlayer p = players[playerID];
                return p != null ? p.marginTracker : null;
            }
            catch { return null; }
        }

        internal static float PercentXAcc(int playerID)
        {
            scrMarginTracker t = Tracker(playerID);
            return t != null ? t.percentXAcc : 1f;
        }

        internal static float PercentAcc(int playerID)
        {
            scrMarginTracker t = Tracker(playerID);
            return t != null ? t.percentAcc : 1f;
        }

        internal static float PercentComplete(int playerID)
        {
            scrMarginTracker t = Tracker(playerID);
            return t != null ? t.percentComplete : 0f;
        }
    }
}
