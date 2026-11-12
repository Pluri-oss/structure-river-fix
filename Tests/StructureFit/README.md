# Structure relocation regression checks

Run from the repository root with the .NET 10 SDK:

```sh
dotnet run --project Tests/StructureFit/StructureFit.csproj
```

The project links `Source/V2/ABStructureFit.cs` directly. It does not contain a
second implementation of the destination search or relocation logic. Each
scenario calls `RescueStraddlingStructures` against a small in-memory map.

The checks cover:

- Unchanged placement when the original clamped destination is safe.
- River tags, water flags, footprint-edge water, and water beneath a bridge.
- Lateral rescue when a river blocks every placement along the original X axis.
- Overlapping source/destination rectangles, multi-cell buildings, terrain,
  underlying terrain, roofs, pawns, and items.
- Player start coordinates, nested `UsedRects`, and fog repair across both axes.
- Grouped structures and the empty cells inside their bounding rectangle.
- Existing buildings, reserved courtyards, later rescue groups, and small debris
  entries that must not reserve an entire area.
- Failure without map mutation when no safe destination exists.
- Already-contained structures and deterministic equal-distance selection.

`Stubs.cs` models only the engine and mod APIs this source file uses. The fog
and terrain grids record changes, and the modeled random APIs throw if used.
These are regression checks of the production control flow, **not in-game
integration tests**. They do not validate Harmony installation, Unity rendering,
the full game's terrain side effects, or interactions with other mods. The main
mod still needs its normal build and a newly generated river map for integration
testing. The harness never writes to `Assemblies/`.

The project has no NuGet dependencies. Its local `NuGet.Config` clears package
feeds so an installed .NET 10 SDK can restore and run it offline.

To compare another revision, export its `ABStructureFit.cs` to a temporary file
and set the `SourceFile` build property. For example:

```sh
git show main:Source/V2/ABStructureFit.cs > /tmp/ABStructureFit-before.cs
dotnet build Tests/StructureFit/StructureFit.csproj \
    -p:SourceFile=/tmp/ABStructureFit-before.cs \
    -p:OutputPath=bin/baseline
dotnet Tests/StructureFit/bin/baseline/StructureFit.dll
```

Use a revision that predates the fix for that comparison. Rebuild without the
`SourceFile` override to return to the working-tree implementation.
