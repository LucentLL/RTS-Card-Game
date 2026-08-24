using System.Collections.Generic;
using SpawnRowDuel.Rules;

namespace SpawnRowDuel.Campaign
{
    /// <summary>
    /// End Turn: everyone grows, then each rival element gets ONE attempt at one neighbour.
    ///
    /// The rival war is auto-resolved - the player never plays defence, and losing a territory to
    /// an AI is not a duel. Rivals also never absorb capitals or gain allies, so an AI empire can
    /// never snowball the way you can; they simply drop out of the roster once they hold nothing.
    ///
    /// The ordering is load-bearing and slightly grubby: growth applies before anyone moves, and
    /// each element's heuristic reads garrisons that earlier elements THIS TURN may already have
    /// changed. That is order-dependent, and the order is shuffled - so it only reproduces if the
    /// shuffle comes off a seeded RNG. Which is the reason this takes one.
    /// </summary>
    public sealed class CampaignTurnResolver
    {
        public const int GarrisonCap = 24;

        /// <summary>The heuristic's floor: a rival will attack into a slightly stronger neighbour,
        /// but not a much stronger one.</summary>
        const int ScoreFloor = -2;

        /// <summary>How often a rival with a good target actually commits. It often does nothing.</summary>
        const int EngageNumerator = 7, EngageDenominator = 10;

        public IReadOnlyList<CampaignEvent> EndTurn(CampaignState s, IRandomSource rng)
        {
            var log = new List<CampaignEvent>();
            if (s == null || s.Map == null) return log;

            s.Turn += 1;

            // growth first, everyone including you
            for (int i = 0; i < s.Map.Territories.Length; i++)
            {
                var t = s.Map.Territories[i];
                bool capital = CampaignRules.CapitalDesignation(s.Map, t.Id) != Element.None;
                t.Garrison = Min(GarrisonCap, t.Garrison + (capital ? 2 : 1));
            }

            var roster = new List<Element>();
            for (int i = 0; i < CampaignRules.Majors.Length; i++)
            {
                var el = CampaignRules.Majors[i];
                if (el == s.Faction) continue;
                if (HoldsAnything(s, el)) roster.Add(el);
            }
            Shuffle(roster, rng);

            for (int r = 0; r < roster.Count; r++)
            {
                var el = roster[r];

                Territory from = null, to = null;
                int bestScore = 0;
                bool has = false;

                for (int i = 0; i < s.Map.Territories.Length; i++)
                {
                    var t = s.Map.Territories[i];
                    if (t.Owner != el) continue;
                    for (int a = 0; a < t.Adjacent.Length; a++)
                    {
                        var u = s.Map.Of(t.Adjacent[a]);
                        if (u == null || u.Owner == el) continue;
                        int sc = t.Garrison - u.Garrison;
                        if (!has || sc > bestScore)      // strict >, so the first max wins
                        {
                            has = true; bestScore = sc; from = t; to = u;
                        }
                    }
                }

                if (!has || bestScore <= ScoreFloor) continue;
                if (!Chance(rng, EngageNumerator, EngageDenominator)) continue;

                double aw = from.Garrison * (0.7 + 0.6 * Unit(rng));
                double dw = to.Garrison * (0.7 + 0.6 * Unit(rng));

                if (aw > dw)
                {
                    var loser = to.Owner;
                    to.Owner = el;
                    int mv = Max(2, from.Garrison / 2);
                    from.Garrison = Max(1, from.Garrison - mv);
                    to.Garrison = mv;

                    log.Add(CampaignEvent.Of(CampaignEventKind.AiCaptured, el, loser, to.Id,
                        CampaignRules.Name(el) + " overran " +
                        (loser == s.Faction ? "your" : CampaignRules.Name(loser) + "'s") + " territory."));
                }
                else
                {
                    from.Garrison = Max(1, (int)(from.Garrison * 0.8));
                    log.Add(CampaignEvent.Of(CampaignEventKind.AiRepulsed, el, to.Owner, to.Id,
                        CampaignRules.Name(el) + " was thrown back from " +
                        (to.Owner == s.Faction ? "your" : CampaignRules.Name(to.Owner) + "'s") + " border."));
                }
            }

            if (CampaignRules.PlayerTerritoryCount(s) == 0)
            {
                s.Lost = true;
                log.Add(CampaignEvent.Of(CampaignEventKind.Defeat, s.Faction, Element.None, -1,
                    "The last of your holdings is lost. The campaign is over."));
            }

            return log;
        }

        static bool HoldsAnything(CampaignState s, Element el)
        {
            for (int i = 0; i < s.Map.Territories.Length; i++)
                if (s.Map.Territories[i].Owner == el) return true;
            return false;
        }

        /// <summary>A uniform double in [0,1). The RNG deals in ints, and a battle roll wants a
        /// fraction; a million buckets is finer than any of these comparisons can notice.</summary>
        static double Unit(IRandomSource rng) { return rng.NextInt(1000000) / 1000000.0; }

        static bool Chance(IRandomSource rng, int num, int den) { return rng.NextInt(den) < num; }

        static void Shuffle(List<Element> list, IRandomSource rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.NextInt(i + 1);
                var tmp = list[i]; list[i] = list[j]; list[j] = tmp;
            }
        }

        static int Min(int a, int b) { return a < b ? a : b; }
        static int Max(int a, int b) { return a > b ? a : b; }
    }
}
