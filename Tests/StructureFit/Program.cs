using System;
using System.Collections.Generic;
using System.Linq;
using AsAboveSoBelow;
using RimWorld;
using Verse;

internal static class Program
{
	private static readonly TerrainDef Soil = new TerrainDef { defName = "Soil" };
	private static readonly TerrainDef River = new TerrainDef { defName = "WaterMoving", IsWater = true, tags = new List<string> { "River" } };
	private static readonly RoofDef Roof = new RoofDef { defName = "RoofConstructed" };
	private static readonly ThingDef Wall = new ThingDef { defName = "Wall", MakeFog = true, building = new BuildingProperties() };

	private static int Main()
	{
		var tests = new (string Name, Action Run)[] {
			("dry clamped destination preserves the original placement", DryDestination),
			("a river cell at the footprint edge is preserved", RiverEdge),
			("a custom River tag is protected without IsWater", RiverTag),
			("ordinary lake water is protected without a River tag", WaterFlag),
			("water under a dry bridge is preserved", UnderWater),
			("a river spanning all Z placements permits a lateral rescue", LateralRescue),
			("overlapping relocation snapshots terrain and things", Overlap),
			("lateral relocation updates start spot, registry and both fog footprints", LateralBookkeeping),
			("touching modules move together and protect the bounding gap", GroupedModules),
			("foreign artificial buildings are not cleared", ForeignBuilding),
			("an occupied structure's empty courtyard is reserved", ReservedCourtyard),
			("a later rescue group's source courtyard is reserved", LaterGroupCourtyard),
			("small debris registry entries do not block rescue", DebrisRegistry),
			("no safe destination has no map or registry mutations", NoDestination),
			("already-contained water structures are left unchanged", ContainedStructure),
			("equal-distance fallback is deterministic without RNG", Deterministic)
		};
		int failures = 0;
		foreach (var test in tests)
		{
			try { test.Run(); Console.WriteLine("PASS " + test.Name); }
			catch (Exception ex) { failures++; Console.WriteLine("FAIL " + test.Name + ": " + ex.Message); }
		}
		Console.WriteLine($"{tests.Length - failures}/{tests.Length} regression checks passed.");
		return failures == 0 ? 0 : 1;
	}

	private static void DryDestination()
	{
		Fixture f = new Fixture();
		f.Rescue();
		Equal(new CellRect(8, 16, 8, 8), f.Used[0], "dry destination changed");
		f.AssertCarried(f.Source, f.Used[0]);
		Check(f.Map.fogGrid.IsFogged(f.Used[0].CenterCell), "sealed destination interior was revealed");
		Check(Log.Warnings.Count == 0 && Log.Errors.Count == 0, "unexpected diagnostics");
	}

	private static void RiverEdge()
	{
		Fixture f = new Fixture();
		IntVec3 water = new IntVec3(15, 0, 23);
		f.Map.terrainGrid.SetTerrain(water, River);
		f.Rescue();
		Check(!f.Used[0].Contains(water), "destination still includes the edge river cell");
		Same(River, f.Map.terrainGrid.TerrainAt(water), "river terrain overwritten");
		f.AssertCarried(f.Source, f.Used[0]);
	}

	private static void RiverTag()
	{
		TerrainDef custom = new TerrainDef { defName = "ModdedRiver", tags = new List<string> { "River" } };
		Fixture f = new Fixture();
		IntVec3 water = new IntVec3(15, 0, 23);
		f.Map.terrainGrid.SetTerrain(water, custom);
		f.Rescue();
		Check(!f.Used[0].Contains(water), "River tag was not honored");
		Same(custom, f.Map.terrainGrid.TerrainAt(water), "custom river overwritten");
	}

	private static void WaterFlag()
	{
		TerrainDef lake = new TerrainDef { defName = "Lake", IsWater = true, tags = null };
		Fixture f = new Fixture();
		IntVec3 water = new IntVec3(15, 0, 23);
		f.Map.terrainGrid.SetTerrain(water, lake);
		f.Rescue();
		Check(!f.Used[0].Contains(water), "IsWater was not honored");
		Same(lake, f.Map.terrainGrid.TerrainAt(water), "lake overwritten");
	}

	private static void UnderWater()
	{
		Fixture f = new Fixture();
		IntVec3 water = new IntVec3(15, 0, 23);
		TerrainDef bridge = new TerrainDef { defName = "Bridge", Removable = true };
		f.Map.terrainGrid.SetTerrain(water, bridge);
		f.Map.terrainGrid.SetUnderTerrain(water, River);
		f.Rescue();
		Check(!f.Used[0].Contains(water), "underlying river was not honored");
		Same(bridge, f.Map.terrainGrid.TerrainAt(water), "bridge overwritten");
		Same(River, f.Map.terrainGrid.UnderTerrainAt(water), "underlying river overwritten");
	}

	private static void LateralRescue()
	{
		Fixture f = new Fixture(new CellRect(8, 5, 8, 8));
		for (int z = f.Surface.minZ; z <= f.Surface.maxZ; z++)
			f.Map.terrainGrid.SetTerrain(new IntVec3(11, 0, z), River);
		f.Rescue();
		Equal(new CellRect(12, 16, 8, 8), f.Used[0], "expected the nearest dry location east of the river");
		for (int z = f.Surface.minZ; z <= f.Surface.maxZ; z++)
			Same(River, f.Map.terrainGrid.TerrainAt(new IntVec3(11, 0, z)), "river was modified");
		f.AssertCarried(f.Source, f.Used[0]);
	}

	private static void Overlap()
	{
		Fixture f = new Fixture();
		Thing wide = new Building { Id = "two-cell fixture", def = new ThingDef { defName = "WideBuilding", Width = 2, building = new BuildingProperties() } };
		f.Place(wide, new IntVec3(10, 0, 16));
		f.Rescue();
		Check(f.Source.Overlaps(f.Used[0]), "fixture should overlap");
		f.AssertCarried(f.Source, f.Used[0]);
		Check(wide.DeSpawnCount == 1 && wide.SpawnCount == 2, "multi-cell thing was carried more than once");
	}

	private static void LateralBookkeeping()
	{
		Fixture f = new Fixture();
		// Water starts outside the original footprint, leaving the overlap itself dry.
		for (int z = 20; z <= f.Surface.maxZ; z++)
			f.Map.terrainGrid.SetTerrain(new IntVec3(15, 0, z), River);
		IntVec3 start = new IntVec3(11, 0, 16);
		MapGenerator.PlayerStartSpotValid = true;
		MapGenerator.PlayerStartSpot = start;
		CellRect inner = new CellRect(10, 14, 2, 2);
		f.Used.Add(inner);
		f.Rescue();
		Equal(new CellRect(7, 16, 8, 8), f.Used[0], "expected a one-cell westward move");
		IntVec3 delta = f.Used[0].Min - f.Source.Min;
		Equal(start + delta, MapGenerator.PlayerStartSpot, "player start spot did not follow both axes");
		Equal(new CellRect(inner.minX + delta.x, inner.minZ + delta.z, inner.Width, inner.Height), f.Used[1], "nested registry rectangle was not retargeted");
		Equal(new CellRect(7, 16, 9, 8), f.Map.fogGrid.RefogCalls.Single(), "fog repair omitted the vacated X strip");
		Check(!f.Map.fogGrid.IsFogged(new IntVec3(15, 0, 17)), "vacated X strip remained fogged");
		Check(!f.Map.fogGrid.IsFogged(MapGenerator.PlayerStartSpot), "moved player start was not revealed");
		foreach (CellRect repair in f.Map.fogGrid.RefogCalls)
			Check(f.Surface.Contains(repair), "fog repair escaped the surface level");
		f.AssertCarried(f.Source, f.Used[0]);
	}

	private static void GroupedModules()
	{
		CellRect first = new CellRect(1, 5, 8, 8);
		Fixture f = new Fixture(first);
		CellRect second = new CellRect(9, 6, 8, 8);
		f.AddStructure(second, "second");
		// This corner is in the union's bounding box but in neither module.
		IntVec3 gap = new IntVec3(16, 0, 16);
		f.Map.terrainGrid.SetTerrain(gap, River);
		f.Rescue();
		IntVec3 deltaA = f.Used[0].Min - first.Min;
		IntVec3 deltaB = f.Used[1].Min - second.Min;
		Equal(deltaA, deltaB, "group modules moved by different offsets");
		CellRect union = new CellRect(f.Used[0].minX, f.Used[0].minZ, 16, 9);
		Check(!union.Contains(gap), "water in the bounding gap was not protected");
		Same(River, f.Map.terrainGrid.TerrainAt(gap), "bounding-gap river overwritten");
		f.AssertThingsCarried(deltaA);
	}

	private static void ForeignBuilding()
	{
		Fixture f = new Fixture();
		IntVec3 obstacle = new IntVec3(15, 0, 23);
		Building foreign = new Building { Id = "foreign structure", def = Wall };
		GenSpawn.Spawn(foreign, obstacle, f.Map, Rot4.South, WipeMode.Vanish);
		f.Rescue();
		Check(!f.Used[0].Contains(obstacle), "destination includes a foreign structure");
		Check(foreign.Spawned && !foreign.Destroyed && foreign.Position == obstacle, "foreign structure was moved or destroyed");
		f.AssertCarried(f.Source, f.Used[0]);
	}

	private static void NoDestination()
	{
		Fixture f = new Fixture(new CellRect(8, 5, 8, 8));
		foreach (IntVec3 c in f.Surface) f.Map.terrainGrid.SetTerrain(c, River);
		MapGenerator.PlayerStartSpotValid = true;
		MapGenerator.PlayerStartSpot = f.Source.CenterCell;
		int mutations = f.Map.Mutations;
		CellRect beforeRect = f.Used[0];
		IntVec3 beforeStart = MapGenerator.PlayerStartSpot;
		f.Rescue();
		Equal(mutations, f.Map.Mutations, "failed rescue mutated map state");
		Equal(beforeRect, f.Used[0], "failed rescue moved registry");
		Equal(beforeStart, MapGenerator.PlayerStartSpot, "failed rescue moved player start");
		Check(f.Map.fogGrid.RefogCalls.Count == 0, "failed rescue touched fog");
		Check(Log.Warnings.Count > 0, "failed rescue should report why the structure was left behind");
		Check(!ABSkySync.Suspended, "sky synchronization remained suspended");
		f.AssertThingsCarried(IntVec3.Zero);
	}

	private static void ReservedCourtyard()
	{
		Fixture f = new Fixture(new CellRect(8, 5, 8, 8));
		f.AddStructure(new CellRect(4, 16, 16, 16), "existing courtyard");
		int mutations = f.Map.Mutations;
		f.Rescue();
		Equal(f.Source, f.Used[0], "rescue landed inside a reserved courtyard");
		Equal(mutations, f.Map.Mutations, "reserved courtyard was altered");
		f.AssertThingsCarried(IntVec3.Zero);
	}

	private static void LaterGroupCourtyard()
	{
		Fixture f = new Fixture(new CellRect(8, 5, 8, 8));
		var firstThings = f.Map.Things.ToDictionary(t => t, t => t.Position);
		CellRect second = new CellRect(4, 17, 16, 16);
		f.AddStructure(second, "later group");
		f.Rescue();
		Equal(f.Source, f.Used[0], "first group entered the later group's courtyard");
		Equal(new CellRect(4, 16, 16, 16), f.Used[1], "later group was not rescued intact");
		foreach (var pair in firstThings)
		{
			Equal(pair.Value, pair.Key.Position, "first group was unexpectedly moved");
			Check(pair.Key.Spawned && !pair.Key.Destroyed, "first group was lost");
		}
		foreach (Thing thing in f.Map.Things.Where(t => t.Id.StartsWith("later group", StringComparison.Ordinal)))
			Check(thing.Spawned && !thing.Destroyed, "later group was damaged before its rescue");
	}

	private static void DebrisRegistry()
	{
		Fixture f = new Fixture();
		Thing debris = new Thing { Id = "chunk", def = new ThingDef { defName = "Chunk" } };
		GenSpawn.Spawn(debris, new IntVec3(15, 0, 23), f.Map, Rot4.South, WipeMode.Vanish);
		f.Used.Add(new CellRect(15, 23, 2, 1));
		f.Rescue();
		Equal(new CellRect(8, 16, 8, 8), f.Used[0], "small debris claim blocked an otherwise valid rescue");
		f.AssertCarried(f.Source, f.Used[0]);
	}

	private static void ContainedStructure()
	{
		Fixture f = new Fixture(new CellRect(8, 18, 8, 8));
		IntVec3 c = f.Source.CenterCell;
		f.Map.terrainGrid.SetUnderTerrain(c, River);
		int mutations = f.Map.Mutations;
		f.Rescue();
		Equal(mutations, f.Map.Mutations, "already-contained structure was touched");
		Equal(f.Source, f.Used[0], "already-contained structure moved");
		Check(Log.Warnings.Count == 0, "already-contained structure emitted warnings");
	}

	private static void Deterministic()
	{
		CellRect Run()
		{
			Fixture f = new Fixture(new CellRect(8, 5, 8, 8));
			// The original 8-wide footprint straddles this two-column river. Both banks
			// are equally near, so the stable X ordering must consistently select west.
			for (int z = f.Surface.minZ; z <= f.Surface.maxZ; z++)
				for (int x = 11; x <= 12; x++) f.Map.terrainGrid.SetTerrain(new IntVec3(x, 0, z), River);
			f.Rescue();
			Equal(0, Rand.Calls, "random state was consumed");
			Equal(new CellRect(3, 16, 8, 8), f.Used[0], "equal-distance tie did not use ascending X");
			return f.Used[0];
		}
		Equal(Run(), Run(), "identical maps chose different destinations");
	}

	private sealed class Fixture
	{
		public readonly CellRect Source;
		public readonly CellRect Surface = new CellRect(0, 16, 24, 16);
		public readonly Map Map = new Map(24, 48, Soil);
		public readonly ABBandMap Bands;
		public readonly List<CellRect> Used;
		private readonly Dictionary<IntVec3, (TerrainDef Top, TerrainDef Under, RoofDef Roof)> originalCells = new Dictionary<IntVec3, (TerrainDef, TerrainDef, RoofDef)>();
		private readonly Dictionary<Thing, IntVec3> originalThings = new Dictionary<Thing, IntVec3>();
		public Fixture(CellRect? source = null)
		{
			Source = source ?? new CellRect(8, 12, 8, 8);
			Bands = new ABBandMap { Surface = Surface };
			MapGenerator.Reset(Map);
			Used = MapGenerator.UsedRects;
			Log.Warnings.Clear(); Log.Errors.Clear(); Rand.Calls = 0; ABSkySync.Suspended = false;
			AddStructure(Source, "first");
		}
		public void AddStructure(CellRect rect, string name)
		{
			Used.Add(rect);
			foreach (IntVec3 c in rect)
			{
				TerrainDef floor = new TerrainDef { defName = name + "-floor-" + c, Removable = true };
				Map.terrainGrid.SetTerrain(c, floor);
				Map.terrainGrid.SetUnderTerrain(c, Soil);
				Map.roofGrid.SetRoof(c, Roof);
				originalCells[c] = (floor, Soil, Roof);
			}
			foreach (IntVec3 c in rect.EdgeCells)
				Place(new Building { Id = name + "-wall-" + c, def = Wall }, c);
			Place(new Pawn { Id = name + "-pawn", def = new ThingDef { defName = "Colonist" } }, rect.CenterCell);
			Place(new Thing { Id = name + "-loot", def = new ThingDef { defName = "Steel" } }, rect.CenterCell + new IntVec3(1, 0, 0));
		}
		public void Place(Thing thing, IntVec3 c)
		{
			GenSpawn.Spawn(thing, c, Map, Rot4.South, WipeMode.Vanish);
			originalThings[thing] = c;
		}
		public void Rescue() => ABStructureFit.RescueStraddlingStructures(Map, Bands);
		public void AssertThingsCarried(IntVec3 delta)
		{
			foreach (var pair in originalThings)
			{
				Check(pair.Key.Spawned && !pair.Key.Destroyed, "lost " + pair.Key.Id);
				Equal(pair.Value + delta, pair.Key.Position, "incorrect position for " + pair.Key.Id);
			}
		}
		public void AssertCarried(CellRect source, CellRect destination)
		{
			Check(Surface.Contains(destination), "destination escaped surface level");
			IntVec3 delta = destination.Min - source.Min;
			AssertThingsCarried(delta);
			foreach (IntVec3 c in source)
			{
				var state = originalCells[c];
				Same(state.Top, Map.terrainGrid.TerrainAt(c + delta), "source terrain smeared at " + c);
				Same(state.Under, Map.terrainGrid.UnderTerrainAt(c + delta), "under-terrain not carried at " + c);
				Same(state.Roof, Map.roofGrid.RoofAt(c + delta), "roof not carried at " + c);
			}
		}
	}

	private static void Check(bool condition, string message)
	{
		if (!condition) throw new InvalidOperationException(message);
	}
	private static void Equal<T>(T expected, T actual, string message)
	{
		Check(EqualityComparer<T>.Default.Equals(expected, actual), message + $" (expected {expected}, got {actual})");
	}
	private static void Same(object expected, object actual, string message) => Check(ReferenceEquals(expected, actual), message);
}
