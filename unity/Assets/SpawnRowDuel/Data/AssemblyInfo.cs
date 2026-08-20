using System.Runtime.CompilerServices;

// The importer is the ONLY writer of card data. Runtime code sees the read-only surface;
// the editor assembly gets the internal fields.
[assembly: InternalsVisibleTo("SpawnRowDuel.Editor")]
