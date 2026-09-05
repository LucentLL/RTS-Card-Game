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

        /// <summary>Retired: the second line under the name. It repeated the lozenge and took the
        /// height the name needed to do it. Kept as a field so nothing that sets it breaks; the
        /// frame no longer draws it.</summary>
        public string TypeLine;
        public string Ribbon;          // HUMAN WIZARD / STRUCTURE / ✦ SPELL / ⚠ TRAP
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
                // THE LOZENGE CARRIES THE RACE, and the banner carries only the name.
                //
                // There used to be a second line under the name saying the same thing the lozenge
                // said two centimetres lower - "TRAP" over a card whose lozenge reads ⚠ TRAP - and
                // it cost the name the height it needed, so long names shrank or clipped to make
                // room for a word already on the card. A creature is the one kind whose type line
                // was not redundant, because it named the RACE rather than the kind; that moves
                // here, where the kind was being repeated for free. Nothing else on this board has
                // an attack and a health meter, so "CREATURE" was never the thing telling anyone.
                TypeLine = "",
                Ribbon = Up(text.TypeLine(c), "CREATURE"),
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
                TypeLine = "",
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
                TypeLine = "",
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

        /// <summary>The lozenge's word: the race when a creature has one, the kind when it does
        /// not. A creature with neither tribe nor subtype still has to say something.</summary>
        static string Up(string race, string fallback)
        {
            return string.IsNullOrEmpty(race) ? fallback : race.ToUpperInvariant();
        }

        /// <summary>A hand card, resolving through whichever registry knows the id.</summary>
        public static bool TryOfCard(CardId id, ICardCatalog catalog, CardTextService text,
                                     CardArtIndex art, out CardFaceModel model)
        {
            // Try*, not the direct accessors: those THROW for an id of the wrong kind, and every
            // hand card asks both questions.
            CreatureCard creature;
            if (catalog.TryCreature(id, out creature)) { model = OfCreature(creature, text, art); return true; }

            SpellCard spell;
            if (catalog.TrySpell(id, out spell)) { model = OfSpell(spell, text, art); return true; }

            model = default(CardFaceModel);
            return false;
        }
    }
}
