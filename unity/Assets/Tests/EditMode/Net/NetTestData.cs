using SpawnRowDuel.Rules;

namespace SpawnRowDuel.Net.Tests
{
    /// <summary>
    /// The real registry, and the fuzz policy, both borrowed from the Rules test assembly rather
    /// than copied. There must be exactly one loader and exactly one fuzzer in this project: a
    /// second copy of either would drift, and a netcode test passing against a slightly different
    /// catalog than the rules tests use would be worse than no test.
    /// </summary>
    public static class NetTestData
    {
        public static ICardCatalog Catalog() { return SpawnRowDuel.Rules.Tests.TestData.Catalog; }
    }
}
