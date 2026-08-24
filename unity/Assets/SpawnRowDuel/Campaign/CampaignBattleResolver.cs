using System.Collections.Generic;
using SpawnRowDuel.Rules;

namespace SpawnRowDuel.Campaign
{
    /// <summary>
    /// What a finished duel does to the map.
    ///
    /// The coupling runs THIS way on purpose. In the JS the battle's own win check reached into
    /// the campaign layer and called it directly, which is why a multiplayer match had to remember
    /// to defensively clear the campaign's target before starting. Here the duel knows nothing: it
    /// produces an outcome, and whoever launched the battle brings it back.
    /// </summary>
    public sealed class CampaignBattleResolver
    {
        public IReadOnlyList<CampaignEvent> Resolve(CampaignState s, BattleOutcome outcome)
        {
            var log = new List<CampaignEvent>();
            if (s == null || s.Map == null || !s.TargetTerritory.HasValue) return log;

            int tid = s.TargetTerritory.Value;
            var t = s.Map.Of(tid);
            if (t == null) { s.TargetTerritory = null; return log; }

            if (outcome == BattleOutcome.Abandoned)
            {
                // the assault simply never happened - no ground changes, no garrison moves
                s.TargetTerritory = null;
                return log;
            }

            var defender = t.Owner;                       // captured BEFORE anything mutates

            if (outcome == BattleOutcome.PlayerWon)
            {
                var prize = CampaignRules.CapitalPrize(s, tid);

                t.Owner = s.Faction;
                t.Garrison = Max(3, t.Garrison / 2 + 2);

                if (prize != Element.None)
                {
                    var gained = new List<Element>();
                    int absorbed = Swallow(s, prize, gained);

                    // THE CASCADE. Absorbing one element's lands can hand you another element's
                    // throne, and without this that element lingers as a landless holdout no
                    // attack can ever reach - the campaign becomes unwinnable and looks like a bug
                    // in the victory check rather than in the absorb.
                    bool changed = true;
                    while (changed)
                    {
                        changed = false;
                        foreach (var kv in s.Map.Capitals)
                        {
                            var el = kv.Key;
                            if (el == s.Faction || s.Allies.Contains(el)) continue;
                            var seat = s.Map.Of(kv.Value);
                            if (seat == null || seat.Owner != s.Faction) continue;
                            absorbed += Swallow(s, el, gained);
                            changed = true;
                            break;                       // the dictionary just changed under us
                        }
                    }

                    for (int i = 0; i < gained.Count; i++)
                        log.Add(CampaignEvent.Of(CampaignEventKind.ElementAbsorbed, s.Faction, gained[i], tid,
                            CampaignRules.Name(gained[i]) + " bows to your banner."));

                    log.Insert(0, CampaignEvent.Of(CampaignEventKind.CapitalTaken, s.Faction, prize, tid,
                        "The " + CampaignRules.Name(prize) + " capital falls" +
                        (absorbed > 0 ? " — its " + absorbed + " remaining land" + (absorbed == 1 ? "" : "s")
                                        + " bow to you." : ".")));
                }
                else
                {
                    log.Add(CampaignEvent.Of(CampaignEventKind.TerritoryWon, s.Faction, defender, tid,
                        "Your banner rises over " + CampaignRules.Name(defender) + " ground."));
                }

                if (!s.Completed && CampaignRules.PlayerTerritoryCount(s) == s.Map.Territories.Length)
                {
                    s.Completed = true;
                    log.Add(CampaignEvent.Of(CampaignEventKind.RealmUnited, s.Faction, Element.None, tid,
                        "Every land is yours — the eight elements united under one throne."));
                }
            }
            else
            {
                // NOTE, and it is worth a raised eyebrow: a failed assault SOFTENS the target by
                // one. Ported as-is because it is the shipped balance, but it means losing helps.
                t.Garrison = Max(1, t.Garrison - 1);
                log.Add(CampaignEvent.Of(CampaignEventKind.AssaultRepelled, defender, s.Faction, tid,
                    CampaignRules.Name(defender) + " holds the line. Regroup and strike again."));
            }

            s.TargetTerritory = null;
            return log;
        }

        /// <summary>Surrender: the target is dropped and nothing on the map moves.</summary>
        public void Abandon(CampaignState s)
        {
            if (s != null) s.TargetTerritory = null;
        }

        /// <summary>An element joins you, and every acre it still holds comes with it.</summary>
        static int Swallow(CampaignState s, Element el, List<Element> gained)
        {
            s.Allies.Add(el);
            gained.Add(el);

            int absorbed = 0;
            for (int i = 0; i < s.Map.Territories.Length; i++)
            {
                var u = s.Map.Territories[i];
                if (u.Owner != el) continue;
                u.Owner = s.Faction;
                absorbed++;
            }
            return absorbed;
        }

        static int Max(int a, int b) { return a > b ? a : b; }
    }
}
