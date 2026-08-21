using System;
using System.Collections.Generic;

namespace SpawnRowDuel.Rules
{
    /// <summary>
    /// Stable sorting for the core. C#'s List.Sort is an UNSTABLE introsort while every JS sort
    /// the specs describe is stable - and the AI and focusFire lean on tie order in six places.
    /// Belt and braces: decorate with the original index and make ties explicit.
    /// </summary>
    public static class Sorting
    {
        public static void StableSort<T>(List<T> list, Comparison<T> compare)
        {
            var keyed = new List<KeyValuePair<int, T>>(list.Count);
            for (int i = 0; i < list.Count; i++) keyed.Add(new KeyValuePair<int, T>(i, list[i]));
            keyed.Sort(delegate (KeyValuePair<int, T> a, KeyValuePair<int, T> b)
            {
                int c = compare(a.Value, b.Value);
                return c != 0 ? c : a.Key.CompareTo(b.Key);
            });
            for (int i = 0; i < list.Count; i++) list[i] = keyed[i].Value;
        }
    }

    /// <summary>
    /// The pre-v3 damage engine (spec 03 s8) - STILL LIVE for exactly the cases the spec names:
    /// worker-stack strikes in v3's misc step, provoked face-downs that flip into creatures,
    /// and (later) the MP path. Named unmistakably; unifying it with the tiered v3 engine is a
    /// rules change gated behind the flag register.
    /// </summary>
    public static class LegacyCombat
    {
        /// <summary>
        /// focusFire: assign each dealer to ONE target, greedy lethal-first, no spillover.
        /// Chip damage only ever lands on the single toughest target via the leftover rule;
        /// zero-attack dealers vanish entirely. Ordered accumulation - never a Dictionary.
        /// </summary>
        public static List<KeyValuePair<BoardObject, int>> FocusFire(
            List<CreatureUnit> dealers, List<BoardObject> targets)
        {
            var dmg = new List<KeyValuePair<BoardObject, int>>();
            for (int i = 0; i < targets.Count; i++)
                dmg.Add(new KeyValuePair<BoardObject, int>(targets[i], 0));
            if (targets.Count == 0) return dmg;

            int IndexOf(BoardObject t)
            {
                for (int i = 0; i < dmg.Count; i++) if (ReferenceEquals(dmg[i].Key, t)) return i;
                return -1;
            }
            void Hit(BoardObject t, int d)
            {
                int i = IndexOf(t);
                dmg[i] = new KeyValuePair<BoardObject, int>(t, dmg[i].Value + d);
            }
            int Dealt(BoardObject t) { return dmg[IndexOf(t)].Value; }
            int HpOf(BoardObject o)
            {
                var c = o as CreatureUnit;
                if (c != null) return c.Hp;
                var b = o as StructureUnit;
                return b != null ? b.Hp : 0;
            }
            int EffA(CreatureUnit c) { return c.EffectiveAttack; }

            var avail = new List<CreatureUnit>();
            for (int i = 0; i < dealers.Count; i++)
                if (dealers[i] != null && EffA(dealers[i]) > 0) avail.Add(dealers[i]);
            Sorting.StableSort(avail, (a, b) => EffA(b).CompareTo(EffA(a)));   // strongest first

            var used = new List<CreatureUnit>();

            var order = new List<BoardObject>(targets);
            Sorting.StableSort(order, (a, b) => HpOf(a).CompareTo(HpOf(b)));   // cheapest kill first

            for (int t = 0; t < order.Count; t++)
            {
                var tgt = order[t];
                int need = HpOf(tgt) - Dealt(tgt);
                if (need <= 0) continue;

                var free = new List<CreatureUnit>();
                for (int i = 0; i < avail.Count; i++)
                    if (!used.Contains(avail[i])) free.Add(avail[i]);
                Sorting.StableSort(free, (a, b) => EffA(a).CompareTo(EffA(b))); // weakest first

                var tryUse = new List<CreatureUnit>();
                int n = need;
                for (int i = 0; i < free.Count && n > 0; i++)
                {
                    tryUse.Add(free[i]);
                    n -= EffA(free[i]);
                }
                if (n <= 0)                                   // lethal is reachable - commit
                    for (int i = 0; i < tryUse.Count; i++)
                    {
                        used.Add(tryUse[i]);
                        Hit(tgt, EffA(tryUse[i]));
                    }
                // else commit NOTHING to this target
            }

            var leftover = new List<CreatureUnit>();
            for (int i = 0; i < avail.Count; i++)
                if (!used.Contains(avail[i])) leftover.Add(avail[i]);
            if (leftover.Count > 0)
            {
                var byToughest = new List<BoardObject>(targets);
                Sorting.StableSort(byToughest, (a, b) => HpOf(b).CompareTo(HpOf(a)));
                for (int i = 0; i < leftover.Count; i++)
                    Hit(byToughest[0], EffA(leftover[i]));
            }
            return dmg;
        }

        public static void ApplyDamage(List<KeyValuePair<BoardObject, int>> map, EventSink ev)
        {
            for (int i = 0; i < map.Count; i++)
            {
                if (map[i].Value <= 0) continue;
                var c = map[i].Key as CreatureUnit;
                if (c != null) c.Hp -= map[i].Value;
                else
                {
                    var b = map[i].Key as StructureUnit;
                    if (b != null) b.Hp -= map[i].Value;
                }
                ev.Add(new DamageApplied(map[i].Key.Id, map[i].Value, 0, DamageTier.Normal));
            }
        }

        /// <summary>
        /// resolveCombat: Undertow first, then a simultaneous First-Strike exchange, then the
        /// main exchange, then the sweep. FS units strike once, in the pre-tier only; anything
        /// killed there never strikes back. Worker stacks soak but deal nothing (attack 0).
        /// </summary>
        public static void Resolve(GameState s, List<CreatureUnit> groupA,
                                   List<CreatureUnit> groupB, ICardCatalog cat, EventSink ev)
        {
            KeywordEngine.PreCombat(s, groupA, groupB, cat, ev);

            List<BoardObject> Live(List<CreatureUnit> g)
            {
                var outList = new List<BoardObject>();
                for (int i = 0; i < g.Count; i++)
                    if (g[i] != null && g[i].Hp > 0) outList.Add(g[i]);
                return outList;
            }
            List<CreatureUnit> Fs(List<CreatureUnit> g, bool fs, bool aliveOnly)
            {
                var outList = new List<CreatureUnit>();
                for (int i = 0; i < g.Count; i++)
                {
                    var c = g[i];
                    if (c == null || c.FirstStrike != fs) continue;
                    if (aliveOnly && c.Hp <= 0) continue;
                    outList.Add(c);
                }
                return outList;
            }

            var aFs = Fs(groupA, true, false);
            var bFs = Fs(groupB, true, false);
            if (aFs.Count > 0 || bFs.Count > 0)
            {
                var dA = FocusFire(aFs, Live(groupB));
                var dB = FocusFire(bFs, Live(groupA));
                ApplyDamage(dA, ev);                          // simultaneous FS exchange
                ApplyDamage(dB, ev);
            }

            var mainA = Fs(groupA, false, true);
            var mainB = Fs(groupB, false, true);
            var d2A = FocusFire(mainA, Live(groupB));
            var d2B = FocusFire(mainB, Live(groupA));
            ApplyDamage(d2A, ev);
            ApplyDamage(d2B, ev);

            DeathSweep.Cleanup(s, cat, ev);
        }
    }
}
