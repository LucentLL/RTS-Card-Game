using System.Collections.Generic;
using SpawnRowDuel.Rules;

namespace SpawnRowDuel.Ai
{
    /// <summary>
    /// aiChooseInterceptors (15_combat.js:70-84), the defending heuristic, ported verbatim as a
    /// POLICY - the engine never calls it. Tests and the stand-in opponent answer BlockerRequests
    /// with it; M11's scripted AI builds on it. On the x500 scale, "P &gt;= 4" is true for any
    /// real attack, so the AI effectively always defends its wall.
    /// </summary>
    public static class AiPolicy
    {
        public static UnitRef[] ChooseInterceptors(GameState s, BlockerRequest req)
        {
            var elig = req.Eligible;
            if (elig.Length == 0) return new UnitRef[0];

            var attacker = s.FindById(req.AttackerId, out _, out _) as CreatureUnit;
            int power = attacker != null ? attacker.EffectiveAttack : 0;

            var d = s.Combat.Declarations[req.DeclarationIndex];
            bool wall = d.Kind == DeclarationKind.Wall;
            bool charge = d.Kind == DeclarationKind.Unit
                && s.FindById(d.TargetUnitId, out _, out _) is ChargeUnit;

            var units = new List<KeyValuePair<UnitRef, CreatureUnit>>();
            for (int i = 0; i < elig.Length; i++)
            {
                var u = s.FindById(elig[i].UnitId, out _, out _) as CreatureUnit;
                if (u != null) units.Add(new KeyValuePair<UnitRef, CreatureUnit>(elig[i], u));
            }

            if (wall)
            {
                int life = s.P(req.Responder).Life;
                if (!(power >= life || power >= 4)) return new UnitRef[0];

                var survivors = units.FindAll(kv => kv.Value.Hp > power);
                Sorting.StableSort(survivors, (x, y) => x.Value.Hp.CompareTo(y.Value.Hp));
                if (survivors.Count > 0) return new[] { survivors[0].Key };

                var byHp = new List<KeyValuePair<UnitRef, CreatureUnit>>(units);
                Sorting.StableSort(byHp, (x, y) => x.Value.Hp.CompareTo(y.Value.Hp));
                int n = byHp.Count < 2 ? byHp.Count : 2;       // chump with the two weakest
                var outRefs = new UnitRef[n];
                for (int i = 0; i < n; i++) outRefs[i] = byHp[i].Key;
                return outRefs;
            }

            if (charge)
            {
                var survivors = units.FindAll(kv => kv.Value.Hp > power);
                Sorting.StableSort(survivors, (x, y) => x.Value.Hp.CompareTo(y.Value.Hp));
                if (survivors.Count > 0) return new[] { survivors[0].Key };
            }

            return new UnitRef[0];       // never trade a body to save a single creature
        }
    }
}
