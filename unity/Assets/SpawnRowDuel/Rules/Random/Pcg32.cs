using System;
using System.Collections.Generic;

namespace SpawnRowDuel.Rules
{
    /// <summary>
    /// Every random draw in the core goes through this interface. There is no ambient RNG:
    /// determinism is a property of the engine, not a convention (design 01 s7).
    /// </summary>
    public interface IRandomSource
    {
        /// <summary>Uniform in [0, bound). Bound must be positive.</summary>
        int NextInt(int bound);

        ulong State { get; }
    }

    /// <summary>
    /// PCG-XSH-RR 32-bit. Chosen over System.Random because System.Random's algorithm is not
    /// contractually stable across .NET runtimes - a save or a replay must reproduce exactly,
    /// including under IL2CPP.
    /// </summary>
    public sealed class Pcg32 : IRandomSource
    {
        private const ulong Multiplier = 6364136223846793005UL;

        private ulong _state;
        private readonly ulong _increment;

        public Pcg32(ulong seed, ulong sequence = 1UL)
        {
            _increment = (sequence << 1) | 1UL;
            _state = 0UL;
            NextUInt();
            _state = unchecked(_state + seed);
            NextUInt();
        }

        private Pcg32(ulong state, ulong increment, bool raw)
        {
            _state = state;
            _increment = increment;
        }

        /// <summary>
        /// Rebuild a generator sitting at an exact stream position. Needed because RNG position is
        /// part of GameState: a clone, a save, or a replay that resumed from a re-seeded generator
        /// would silently diverge.
        /// </summary>
        public static Pcg32 FromRaw(ulong state, ulong increment)
        {
            return new Pcg32(state, increment | 1UL, true);
        }

        public ulong State { get { return _state; } }
        public ulong Increment { get { return _increment; } }

        public Pcg32 Clone() { return new Pcg32(_state, _increment, true); }

        public uint NextUInt()
        {
            ulong old = _state;
            _state = unchecked(old * Multiplier + _increment);
            uint xorshifted = (uint)(((old >> 18) ^ old) >> 27);
            int rot = (int)(old >> 59);
            return (xorshifted >> rot) | (xorshifted << ((-rot) & 31));
        }

        /// <summary>
        /// Fisher-Yates, descending (i = n-1 down to 1), the JS shuffles' exact loop shape
        /// (06_mana_workers.js:34,88). The ONLY shuffle in the core - a second shuffle with a
        /// different draw order would silently desync replays.
        /// </summary>
        public void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = NextInt(i + 1);
                T tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }

        /// <summary>
        /// Integer probability: Chance(60, 100) replaces `Math.random() &lt; 0.6`. No floats.
        /// </summary>
        public bool Chance(int numerator, int denominator)
        {
            return NextInt(denominator) < numerator;
        }

        /// <summary>
        /// Uniform in [0, bound), rejection-sampled so the distribution carries no modulo bias.
        /// </summary>
        public int NextInt(int bound)
        {
            if (bound <= 0) throw new ArgumentOutOfRangeException("bound", "bound must be positive");
            uint threshold = (uint)((0x100000000UL - (ulong)bound) % (ulong)bound);
            while (true)
            {
                uint r = NextUInt();
                if (r >= threshold) return (int)(r % (uint)bound);
            }
        }
    }
}
