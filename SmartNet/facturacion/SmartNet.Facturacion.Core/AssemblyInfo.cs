using System.Runtime.CompilerServices;

// tasks.md 1.1/1.2 — PayloadOutbox.ConstruirAsync/Serializar are internal by design (design.md
// Interfaces/Contracts snippet): only the golden-fixture tests need to reach them directly.
[assembly: InternalsVisibleTo("SmartNet.Facturacion.Core.Tests")]
