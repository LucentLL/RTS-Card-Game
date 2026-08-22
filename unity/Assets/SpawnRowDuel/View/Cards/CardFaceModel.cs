using SpawnRowDuel.Data;
using SpawnRowDuel.Rules;
using UnityEngine;

namespace SpawnRowDuel.View.Cards
{
    public enum CardKindFace { Creature = 0, Structure = 1, Spell = 2, Trap = 3, FaceDown = 4 }

    /// <summary>
    /// Everything the frame draws, flattened - so the frame never reaches into the catalog, the
    /// board or the engine, and one binder feeds it from a hand card, a board unit or a build-menu
    /// row without the frame knowing which.
    ///
    /// Live numbers (Hp, Attack) are separate from printed ones (MaxHp) because a card on the
    /// board shows what it has left while a card in hand shows what it will arrive as.
    /// </summary>
    public struct CardFaceModel
    {
        public string Name;
        public string TypeLine;        // "Human Wizard" / "Structure" / ""
        public string Ribbon;          // CREATURE / STRUCTURE / ✦ SPELL / ⚠ TRAP
        public string Rules;           // generated, rich text
        public int Cost;
        public Element Element;
        public CardKindFace Kind;

        public bool ShowStats;
        public int Attack, Hp, MaxHp;

        public int WorkerChip;         // creature upkeep as negative, structure support as positive
        public bool HasWorkerChip;

        public Sprite Art;

        // live board state - drawn as small chips, never as separate frames
        public bool Sick, Tapped, Moved, Foe;
        public int Bank;

        public static CardFaceModel OfCreature(CreatureCard c, CardTextService text, CardArtIndex art)
        {
            var m = new CardFaceModel
            {
                Name = c.Name,
                TypeLine = text.TypeLine(c),
                Ribbon = "CREATURE",
                Rules = text.CreatureBrief(c),
                Cost = c.Cost,
                Element = c.Element,
                Kind = CardKindFace.Creature,
                ShowStats = true,
                Attack = c.Attack,
                Hp = c.Health,
                MaxHp = c.Health,
                WorkerChip = -c.Upkeep,
                HasWorkerChip = c.Upkeep != 0,
                Art = art.CardArt(c.Name),
            };
            return m;
        }

        public static CardFaceModel OfSpell(SpellCard s, CardTextService text, CardArtIndex art)
        {
            return new CardFaceModel
            {
                Name = s.Name,
                TypeLine = s.IsTrap ? "Trap" : "Spell",
                Ribbon = s.IsTrap ? "⚠ TRAP" : "✦ SPELL",
                Rules = text.SpellText(s),
                Cost = s.Cost,
                Element = Element.None,
                Kind = s.IsTrap ? CardKindFace.Trap : CardKindFace.Spell,
                ShowStats = false,
                Art = art.CardArt(s.Name),
            };
        }

        public static CardFaceModel OfStructure(StructureDef d, CardTextService text, CardArtIndex art)
        {
            return new CardFaceModel
            {
                Name = d.Name,
                TypeLine = "Structure",
                Ribbon = "STRUCTURE",
                Rules = text.StructureBrief(d),
                Cost = d.Cost,
                Element = d.Element,
                Kind = CardKindFace.Structure,
                ShowStats = true,
                Attack = 0,
                Hp = d.MaxHp,
                MaxHp = d.MaxHp,
                WorkerChip = d.Support,
                HasWorkerChip = d.Support != 0,
                Art = art.CardArt(d.Name),
            };
        }

        /// <summary>A hand card, resolving through whichever registry knows the id.</summary>
        public static bool TryOfCard(CardId id, ICardCatalog catalog, CardTextService text,
                                     CardArtIndex art, out CardFaceModel model)
        {
            var creature = catalog.Creature(id);
            if (creature != null) { model = OfCreature(creature, text, art); return true; }

            var spell = catalog.Spell(id);
            if (spell != null) { model = OfSpell(spell, text, art); return true; }

            model = default(CardFaceModel);
            return false;
        }
    }
}
