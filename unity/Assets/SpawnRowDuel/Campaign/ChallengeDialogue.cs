using System.Collections.Generic;
using SpawnRowDuel.Rules;

namespace SpawnRowDuel.Campaign
{
    public enum DialogueSide : byte { Attacker = 0, Defender = 1 }

    public enum BarkBucket : byte { Open = 0, Capital = 1, Taunt = 2, Retort = 3, Close = 4 }

    public struct DialogueLine
    {
        public Element Speaker;
        public string SpeakerName;
        public DialogueSide Side;
        public string Text;
    }

    /// <summary>
    /// The four-line challenge each element's champion trades before a campaign duel.
    ///
    /// Authored content, ported verbatim - eight elements times five buckets times two
    /// alternatives, plus eight rival exchanges for the four opposed pairs. There is no branching,
    /// no player choice and no state: the only two things that change the script are whether the
    /// target is the defender's OWN throne and whether the two elements are rivals.
    ///
    /// The capital flag is owner-relative on purpose. The capital barks are written in the first
    /// person about the speaker's own seat, so a throne a rival already seized must not trigger
    /// them - the defender standing on it does not think of it as home.
    /// </summary>
    public static class ChallengeDialogue
    {
        /// <summary>Each element speaks through its flagship, the cost-6 creature.</summary>
        public static string Champion(Element el)
        {
            switch (el)
            {
                case Element.Fire: return "Magmaw";
                case Element.Water: return "Leviath";
                case Element.Earth: return "Titanore";
                case Element.Wind: return "Tempest";
                case Element.Forest: return "Hive Cradle";
                case Element.Electric: return "Galvanwyrm";
                case Element.Light: return "Seraphine";
                case Element.Dark: return "Voidwyrm";
                default: return CampaignRules.Name(el);
            }
        }

        public static string[] Barks(Element el, BarkBucket bucket)
        {
            string[][] set;
            if (!Table.TryGetValue(el, out set)) return Empty;
            return set[(int)bucket];
        }

        static readonly string[] Empty = { "" };

        /// <summary>
        /// Build the exchange. The defender opens and speaks line three; the attacker always has
        /// the last word.
        /// </summary>
        public static DialogueLine[] Build(Element attacker, Element defender,
                                           bool defenderOwnCapital, IRandomSource rng)
        {
            string an = Champion(attacker), dn = Champion(defender);
            string[] rival = Rival(attacker, defender);

            var open = defenderOwnCapital ? Barks(defender, BarkBucket.Capital) : Barks(defender, BarkBucket.Open);
            if (open.Length == 0) open = Barks(defender, BarkBucket.Open);

            return new[]
            {
                Line(defender, dn, DialogueSide.Defender, Pick(open, rng)),
                Line(attacker, an, DialogueSide.Attacker,
                     rival != null ? rival[0] : Pick(Barks(attacker, BarkBucket.Taunt), rng)),
                Line(defender, dn, DialogueSide.Defender,
                     rival != null ? rival[1] : Pick(Barks(defender, BarkBucket.Retort), rng)),
                Line(attacker, an, DialogueSide.Attacker, Pick(Barks(attacker, BarkBucket.Close), rng)),
            };
        }

        static DialogueLine Line(Element speaker, string name, DialogueSide side, string text)
        {
            return new DialogueLine { Speaker = speaker, SpeakerName = name, Side = side, Text = text };
        }

        static string Pick(string[] options, IRandomSource rng)
        {
            if (options == null || options.Length == 0) return "";
            return options[rng == null ? 0 : rng.NextInt(options.Length)];
        }

        /// <summary>The four opposed pairs, both directions. Everything else uses the generic
        /// taunt and retort buckets.</summary>
        public static string[] Rival(Element attacker, Element defender)
        {
            foreach (var r in Rivals)
                if (r.A == attacker && r.D == defender) return new[] { r.Taunt, r.Retort };
            return null;
        }

        struct RivalPair
        {
            public Element A, D;
            public string Taunt, Retort;
            public RivalPair(Element a, Element d, string taunt, string retort)
            { A = a; D = d; Taunt = taunt; Retort = retort; }
        }

        static readonly RivalPair[] Rivals =
        {
            new RivalPair(Element.Fire, Element.Water,
                "Steam. That's all your ocean is to me — steam I haven't made yet.",
                "Oceans have swallowed a thousand fires like you. You won't even hiss."),
            new RivalPair(Element.Water, Element.Fire,
                "Every forge goes cold, ember. Yours goes cold today.",
                "Come and try, puddle. I've boiled seas for less."),
            new RivalPair(Element.Light, Element.Dark,
                "The dark is only the absence of my arrival. I have arrived.",
                "Little candle, the dark was here before you and will be here after. Come — be snuffed."),
            new RivalPair(Element.Dark, Element.Light,
                "Every dawn ends, Seraphine. I am what it ends INTO.",
                "The dark has knelt at every sunrise since the first. Kneel again."),
            new RivalPair(Element.Earth, Element.Wind,
                "Even the wind must land somewhere, breeze. And everywhere it lands is mine.",
                "Landing? Sweet old rock — why would I ever come down for you?"),
            new RivalPair(Element.Wind, Element.Earth,
                "Mountains erode, boulder. I am the thing that erodes them. Grain by grain.",
                "Blow, then. When you tire, the mountain will still be counting."),
            new RivalPair(Element.Forest, Element.Electric,
                "Wood does not conduct, storm-worm. But it burns SLOWLY, and grows back faster.",
                "Nature's rebuttal to a tree: lightning. Ask any tall one what it thinks of me."),
            new RivalPair(Element.Electric, Element.Forest,
                "Tallest thing on the field gets the bolt, cradle. Guess what you are.",
                "Strike, spark. The grove has drunk a million storms and grown from every one."),
        };

        // element -> [open, capital, taunt, retort, close], two alternatives each
        static readonly Dictionary<Element, string[][]> Table = new Dictionary<Element, string[][]>
        {
            {
                Element.Fire, new[]
                {
                    new[]{ "Who dares scorch their boots on my doorstep? Speak fast — the ground here eats the slow.",
                           "You smell that? Slag and ash. That's what becomes of banners that march on Fire." },
                    new[]{ "This is the Furnace-Keep itself. Every army that reached these walls is part of the walls now.",
                           "You bring an army to the heart of the forge? Good. We were running low on fuel." },
                    new[]{ "Burn it all down. What's left standing, we keep.",
                           "I'll give your line one chance to run. One. It's more than the last ones got." },
                    new[]{ "Then come closer. Everything you love is kindling.",
                           "Ha! Stoke the coals. This one thinks it can outlast a furnace." },
                    new[]{ "Enough talk. Light the field.",
                           "Then it's settled — by fire, as all things are." },
                }
            },
            {
                Element.Water, new[]
                {
                    new[]{ "The tide brought you to us. The tide will carry what's left of you away.",
                           "Still waters, stranger. Turn back before they remember how to drown." },
                    new[]{ "You stand before the Drowned Tower. Deeper powers than you have broken on this current.",
                           "The throne of the deep does not fall. It closes over, and is calm again." },
                    new[]{ "Every wall erodes. Yours simply erodes today.",
                           "We are patient as rain and sudden as the flood. Choose which one meets you." },
                    new[]{ "Come then. The undertow is patient, and you look tired already.",
                           "Waves do not argue with stone. They simply return, and return, and return." },
                    new[]{ "The current has decided. Let it pull.",
                           "Enough. Let the water speak." },
                }
            },
            {
                Element.Earth, new[]
                {
                    new[]{ "You are standing on me, little thing. That is as far as you will ever get.",
                           "Turn around. The mountain has outlasted better invasions than yours." },
                    new[]{ "This is the Hollow Mountain. Its walls have never fallen. You will not be the first to see them fall.",
                           "You march on bedrock. Bedrock does not surrender." },
                    new[]{ "I do not need to be fast. You will tire, and I will still be here.",
                           "Stone remembers every siege. Yours will be a short memory." },
                    new[]{ "Dig in, then. We will see whose roots go deeper.",
                           "Strike. The mountain will count your blows and forget them." },
                    new[]{ "The earth has spoken. It says: stay down.",
                           "Come. Break yourself against me." },
                }
            },
            {
                Element.Wind, new[]
                {
                    new[]{ "You're slow. Everything about you is slow. This will be over before your banners unfurl.",
                           "The updrafts carried word of your little march. We laughed, mostly." },
                    new[]{ "This crag belongs to the sky. You'd need wings to take it, and I don't see any on you.",
                           "The Screaming Crag stands because nothing can catch it. Certainly not you." },
                    new[]{ "Try to hit me. Go on. I'll wait — no, actually, I won't.",
                           "We'll scour your back line before your front line knows we've passed." },
                    new[]{ "Catch the wind, then. Others have tried. Their bones make lovely whistles.",
                           "You brought walls to a sky fight. Adorable." },
                    new[]{ "Skies darken. Time to fly.",
                           "Enough hovering. Strike like a gale." },
                }
            },
            {
                Element.Forest, new[]
                {
                    new[]{ "The grove counted your soldiers as they crossed the treeline. The grove is patient. We are patient.",
                           "Root and bough remember every axe. Yours will join the mulch." },
                    new[]{ "This is the First Grove. Everything you see grew from it. Everything you see will defend it.",
                           "The Cradle wakes. The brood stirs. You should not have come here." },
                    new[]{ "We grow through everything, given time. Your walls are no different.",
                           "The canopy closes over all things. Today it closes over you." },
                    new[]{ "Then the vines will take you slowly, as they take all impatient things.",
                           "Hatch, my broodlings. Show them what patience becomes." },
                    new[]{ "The forest marches. Root by root.",
                           "Grow. Strangle. Bloom." },
                }
            },
            {
                Element.Electric, new[]
                {
                    new[]{ "Signal detected. Response time: instant. That's the difference between us, friend.",
                           "You walked here? We ARRIVED. Before you finished deciding to come." },
                    new[]{ "This is the Pylon-Hold. Ten thousand volts of no-you-don't. Touch the fence and find out.",
                           "The storm's heart doesn't get conquered. It gets survived. Briefly." },
                    new[]{ "I've already won this fight nine times in my head. Care to see the live version?",
                           "First strike, last laugh. That's the whole doctrine." },
                    new[]{ "Cute speech. I overcharged during it. Your move.",
                           "Thunder answers lightning. Try to keep up." },
                    new[]{ "Storm's rolling in. Let's ride it.",
                           "Charge to full. DISCHARGE." },
                }
            },
            {
                Element.Light, new[]
                {
                    new[]{ "Dawn finds all who trespass here. Lay down your banner and be forgiven — this once.",
                           "The cloister's light does not flicker for armies. Approach, and be seen for what you are." },
                    new[]{ "You stand before the Gold Vault of Dawn. Its light has never failed, and never will.",
                           "The dawnlight judges all who reach these gates. Few are found worthy. None by force." },
                    new[]{ "We come not in anger, but in certainty. The light goes where it will.",
                           "Grace has an edge, stranger. You are about to see it drawn." },
                    new[]{ "Then the ward is raised, and the judgement is begun.",
                           "Radiance does not yield. It reveals. Stand in it, if you dare." },
                    new[]{ "By dawn's mandate — advance.",
                           "Let the light fall where it may." },
                }
            },
            {
                Element.Dark, new[]
                {
                    new[]{ "Ah. Fresh souls, walking themselves to the crypt. How considerate.",
                           "The dark whispered your coming days ago. It also whispered how you end." },
                    new[]{ "This is the Sunken Crypt. Everything that enters feeds it. You will feed it magnificently.",
                           "The void keeps its throne the old way: it simply never gives anything back." },
                    new[]{ "Everything you field, I harvest. Your army is just my army, waiting.",
                           "The void is patient and I am not. Lucky for you, only one of us is merciful. Unlucky: it's neither." },
                    new[]{ "Yes... struggle. The reaping is sweeter when the crop resists.",
                           "Every soldier you lose joins my line. Do the arithmetic, then despair." },
                    new[]{ "The dark is done whispering.",
                           "Reap them all." },
                }
            },
        };
    }
}
