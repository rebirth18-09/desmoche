using Desmoche.Presentation;

namespace Desmoche.Diagnostics;

// Solo los fixtures usan este arranque sincronico. Main real conserva la carga.
public partial class DiagnosticMain : Main
{
    protected override bool ImmediateInitialization => true;
}