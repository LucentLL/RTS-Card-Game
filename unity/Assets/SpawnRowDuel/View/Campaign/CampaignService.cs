using System.Collections.Generic;
using System.IO;
using SpawnRowDuel.Campaign;
using SpawnRowDuel.Rules;
using UnityEngine;

namespace SpawnRowDuel.View.Campaign
{
    /// <summary>
    /// The campaign's Unity-side owner: where the save lives, when it is written, and how a
    /// territory becomes a duel and a duel becomes a territory again.
    ///
    /// The rules core knows nothing about any of this. It takes a state and an outcome and returns
    /// events; this decides when to ask. That inversion is the one structural change the port
    /// makes to the JS design, where the battle's own win check reached into the campaign and
    /// every other mode had to remember to defensively clear the pending target first.
    /// </summary>
    public sealed class CampaignService
    {
        public CampaignState State { get; private set; }

        /// <summary>The territory a launched battle is fighting for, and what it was worth.</summary>
        public bool BattlePending { get { return State != null && State.TargetTerritory.HasValue; } }

        readonly CampaignBattleResolver _battles = new CampaignBattleResolver();
        readonly CampaignTurnResolver _turns = new CampaignTurnResolver();

        Pcg32 _rng;

        static string SavePath
        {
            get { return Path.Combine(Application.persistentDataPath, CampaignCodec.FileName); }
        }

        public void Load()
        {
            State = null;
            try
            {
                if (!File.Exists(SavePath)) return;
                State = CampaignCodec.Read(File.ReadAllText(SavePath));
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[campaign] save unreadable, starting fresh: " + e.Message);
                State = null;
            }
            if (State != null) _rng = new Pcg32(State.Seed ^ 0x9E3779B97F4A7C15UL, 7UL);
        }

        public void Save()
        {
            if (State == null) return;
            try { File.WriteAllText(SavePath, CampaignCodec.Write(State)); }
            catch (System.Exception e) { Debug.LogWarning("[campaign] could not save: " + e.Message); }
        }

        public void Delete()
        {
            State = null;
            try { if (File.Exists(SavePath)) File.Delete(SavePath); }
            catch (System.Exception e) { Debug.LogWarning("[campaign] could not delete save: " + e.Message); }
        }

        /// <summary>Draw a new world under a chosen banner.</summary>
        public void Begin(Element faction)
        {
            ulong seed = (ulong)System.DateTime.UtcNow.Ticks;
            _rng = new Pcg32(seed ^ 0x9E3779B97F4A7C15UL, 7UL);

            State = new CampaignState { Faction = faction, Turn = 1, Seed = seed };
            State.Map = new CampaignMapGenerator().Generate(faction, new Pcg32(seed));
            Save();
        }

        public bool HasRunnableCampaign
        {
            get { return State != null && State.IsValid && !State.Lost; }
        }

        // ── the battle handoff ──────────────────────────────────────────────────────────

        /// <summary>
        /// Mark a territory as the one being fought for and describe the duel to launch. The
        /// target is saved BEFORE the battle starts: a crash mid-duel should leave a campaign that
        /// knows a battle was pending, not one that quietly forgot.
        /// </summary>
        public BattleLaunchRequest Launch(int territoryId, CommanderId banner)
        {
            var t = State.Map.Of(territoryId);
            State.TargetTerritory = territoryId;
            Save();

            return new BattleLaunchRequest
            {
                PlayerCommander = banner,
                EnemyCommander = CampaignRules.Solo(t.Owner),
                DeckSeed = (ulong)Random.Range(1, int.MaxValue),
                TerritoryId = territoryId,
            };
        }

        public IReadOnlyList<CampaignEvent> Resolve(BattleOutcome outcome)
        {
            var log = _battles.Resolve(State, outcome);
            Save();
            return log;
        }

        public IReadOnlyList<CampaignEvent> EndTurn()
        {
            if (_rng == null) _rng = new Pcg32(State.Seed ^ 0x9E3779B97F4A7C15UL, 7UL);
            var log = _turns.EndTurn(State, _rng);
            Save();
            return log;
        }

        // ── things the map screen asks ──────────────────────────────────────────────────

        public bool IsAttackable(int territoryId)
        {
            return State != null && CampaignRules.IsAttackable(State.Map, State.Faction, territoryId);
        }

        public Element CapitalPrize(int territoryId)
        {
            return State == null ? Element.None : CampaignRules.CapitalPrize(State, territoryId);
        }

        public Element CapitalDesignation(int territoryId)
        {
            return State == null ? Element.None : CampaignRules.CapitalDesignation(State.Map, territoryId);
        }
    }
}
