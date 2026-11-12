using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace HarmonyLib
{
	[AttributeUsage(AttributeTargets.Class)]
	public sealed class HarmonyPatch : Attribute
	{
		public HarmonyPatch(Type type, string method) { }
	}
}

namespace UnityEngine
{
	public static class Mathf
	{
		public static int Min(int a, int b) => Math.Min(a, b);
		public static int Max(int a, int b) => Math.Max(a, b);
		public static int Clamp(int value, int min, int max) => Math.Min(Math.Max(value, min), max);
	}
}

namespace Verse
{
	public readonly struct IntVec3 : IEquatable<IntVec3>
	{
		public readonly int x, y, z;
		public IntVec3(int x, int y, int z) { this.x = x; this.y = y; this.z = z; }
		public static IntVec3 Zero => new IntVec3(0, 0, 0);
		public static IntVec3 operator +(IntVec3 a, IntVec3 b) => new IntVec3(a.x + b.x, a.y + b.y, a.z + b.z);
		public static IntVec3 operator -(IntVec3 a, IntVec3 b) => new IntVec3(a.x - b.x, a.y - b.y, a.z - b.z);
		public static bool operator ==(IntVec3 a, IntVec3 b) => a.Equals(b);
		public static bool operator !=(IntVec3 a, IntVec3 b) => !a.Equals(b);
		public bool Equals(IntVec3 other) => x == other.x && y == other.y && z == other.z;
		public override bool Equals(object other) => other is IntVec3 value && Equals(value);
		public override int GetHashCode() => HashCode.Combine(x, y, z);
		public override string ToString() => $"({x},{y},{z})";
		public bool InBounds(Map map) => x >= 0 && z >= 0 && x < map.Size.x && z < map.Size.z;
		public List<Thing> GetThingList(Map map) => map.Cells[x, z];
		public Building GetEdifice(Map map) => GetThingList(map).OfType<Building>().FirstOrDefault();
	}

	public struct CellRect : IEnumerable<IntVec3>, IEquatable<CellRect>
	{
		public int minX, minZ, maxX, maxZ;
		public CellRect(int x, int z, int width, int height)
		{
			minX = x; minZ = z; maxX = x + width - 1; maxZ = z + height - 1;
		}
		public int Width => maxX - minX + 1;
		public int Height => maxZ - minZ + 1;
		public int Area => Width * Height;
		public IntVec3 Min => new IntVec3(minX, 0, minZ);
		public IntVec3 CenterCell => new IntVec3(minX + Width / 2, 0, minZ + Height / 2);
		public bool Contains(IntVec3 c) => c.x >= minX && c.x <= maxX && c.z >= minZ && c.z <= maxZ;
		public bool Contains(CellRect r) => r.minX >= minX && r.maxX <= maxX && r.minZ >= minZ && r.maxZ <= maxZ;
		public bool Overlaps(CellRect r) => minX <= r.maxX && maxX >= r.minX && minZ <= r.maxZ && maxZ >= r.minZ;
		public CellRect ExpandedBy(int amount) => new CellRect(minX - amount, minZ - amount, Width + 2 * amount, Height + 2 * amount);
		public CellRect ClipInsideMap(Map map)
		{
			int x = Math.Max(0, minX), z = Math.Max(0, minZ);
			return new CellRect(x, z, Math.Min(map.Size.x - 1, maxX) - x + 1, Math.Min(map.Size.z - 1, maxZ) - z + 1);
		}
		public IEnumerable<IntVec3> EdgeCells
		{
			get
			{
				foreach (IntVec3 c in this)
					if (c.x == minX || c.x == maxX || c.z == minZ || c.z == maxZ) yield return c;
			}
		}
		public IEnumerator<IntVec3> GetEnumerator()
		{
			for (int z = minZ; z <= maxZ; z++)
				for (int x = minX; x <= maxX; x++) yield return new IntVec3(x, 0, z);
		}
		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
		public bool Equals(CellRect r) => minX == r.minX && minZ == r.minZ && maxX == r.maxX && maxZ == r.maxZ;
		public override bool Equals(object other) => other is CellRect r && Equals(r);
		public override int GetHashCode() => HashCode.Combine(minX, minZ, maxX, maxZ);
		public override string ToString() => $"[{minX},{minZ},{Width},{Height}]";
	}

	public sealed class TerrainDef
	{
		public string defName;
		public bool IsWater;
		public bool Removable;
		public List<string> tags = new List<string>();
		public bool HasTag(string tag) => tags != null && tags.Contains(tag);
	}
	public sealed class RoofDef { public string defName; }
	public sealed class BuildingProperties { public bool isNaturalRock; }
	public sealed class ThingDef
	{
		public string defName;
		public bool MakeFog;
		public bool destroyable = true;
		public BuildingProperties building;
		public int Width = 1, Height = 1;
	}
	public readonly struct Rot4 { public static Rot4 South => default; }
	public enum DestroyMode { Vanish }
	public enum WipeMode { Vanish }

	public class Thing
	{
		public string Id;
		public ThingDef def;
		public bool Destroyed, Spawned;
		public IntVec3 Position;
		public Rot4 Rotation;
		internal Map Map;
		public int SpawnCount, DeSpawnCount;
		public CellRect OccupiedRect() => new CellRect(Position.x, Position.z, def.Width, def.Height);
		public void DeSpawn(DestroyMode mode)
		{
			if (!Spawned) return;
			foreach (IntVec3 c in OccupiedRect()) if (c.InBounds(Map)) Map.Cells[c.x, c.z].Remove(this);
			Map.Mutations++;
			Spawned = false;
			DeSpawnCount++;
		}
		public void Destroy(DestroyMode mode)
		{
			DeSpawn(mode);
			Destroyed = true;
		}
	}
	public class Building : Thing { }
	public class Pawn : Thing { }

	public sealed class Map
	{
		public readonly IntVec3 Size;
		public readonly TerrainGrid terrainGrid;
		public readonly RoofGrid roofGrid;
		public readonly FogGrid fogGrid;
		public readonly CellIndices cellIndices;
		public readonly List<Thing>[,] Cells;
		public readonly List<Thing> Things = new List<Thing>();
		public int Mutations;
		public IntVec3 Center => new IntVec3(Size.x / 2, 0, Size.z / 2);
		public Map(int width, int height, TerrainDef terrain)
		{
			Size = new IntVec3(width, 1, height);
			Cells = new List<Thing>[width, height];
			for (int z = 0; z < height; z++)
				for (int x = 0; x < width; x++) Cells[x, z] = new List<Thing>();
			terrainGrid = new TerrainGrid(this, terrain);
			roofGrid = new RoofGrid(this);
			fogGrid = new FogGrid(this);
			cellIndices = new CellIndices(this);
		}
	}

	public sealed class TerrainGrid
	{
		private readonly Map map;
		private readonly TerrainDef[,] top, under;
		public TerrainGrid(Map map, TerrainDef initial)
		{
			this.map = map;
			top = new TerrainDef[map.Size.x, map.Size.z]; under = new TerrainDef[map.Size.x, map.Size.z];
			for (int z = 0; z < map.Size.z; z++)
				for (int x = 0; x < map.Size.x; x++) top[x, z] = initial;
		}
		public TerrainDef TerrainAt(IntVec3 c) => top[c.x, c.z];
		public TerrainDef UnderTerrainAt(IntVec3 c) => under[c.x, c.z];
		public void SetTerrain(IntVec3 c, TerrainDef value) { top[c.x, c.z] = value; map.Mutations++; }
		public void SetUnderTerrain(IntVec3 c, TerrainDef value) { under[c.x, c.z] = value; map.Mutations++; }
		public bool CanRemoveTopLayerAt(IntVec3 c) => top[c.x, c.z]?.Removable == true && under[c.x, c.z] != null;
		public void RemoveTopLayer(IntVec3 c, bool doLeavings)
		{
			top[c.x, c.z] = under[c.x, c.z]; under[c.x, c.z] = null; map.Mutations++;
		}
	}
	public sealed class RoofGrid
	{
		private readonly Map map;
		private readonly RoofDef[,] values;
		public RoofGrid(Map map) { this.map = map; values = new RoofDef[map.Size.x, map.Size.z]; }
		public RoofDef RoofAt(IntVec3 c) => values[c.x, c.z];
		public void SetRoof(IntVec3 c, RoofDef roof) { values[c.x, c.z] = roof; map.Mutations++; }
	}
	public sealed class FogGrid
	{
		private readonly Map map;
		private readonly bool[,] values;
		public readonly List<CellRect> RefogCalls = new List<CellRect>();
		public FogGrid(Map map) { this.map = map; values = new bool[map.Size.x, map.Size.z]; }
		public bool IsFogged(IntVec3 c) => values[c.x, c.z];
		public void Refog(CellRect rect)
		{
			RefogCalls.Add(rect);
			foreach (IntVec3 c in rect) values[c.x, c.z] = true;
			map.Mutations++;
		}
		public void Unfog(IntVec3 c) { values[c.x, c.z] = false; map.Mutations++; }
	}
	public sealed class CellIndices
	{
		private readonly Map map;
		public CellIndices(Map map) { this.map = map; }
		public int CellToIndex(IntVec3 c) => c.z * map.Size.x + c.x;
	}
	public static class GenSpawn
	{
		public static Thing Spawn(Thing thing, IntVec3 loc, Map map, Rot4 rot, WipeMode mode)
		{
			thing.Map = map; thing.Position = loc; thing.Rotation = rot; thing.Spawned = true; thing.SpawnCount++;
			foreach (IntVec3 c in thing.OccupiedRect()) if (c.InBounds(map)) map.Cells[c.x, c.z].Add(thing);
			if (!map.Things.Contains(thing)) map.Things.Add(thing);
			map.Mutations++;
			return thing;
		}
	}
	public static class GenAdj
	{
		public static readonly IntVec3[] CardinalDirections = {
			new IntVec3(1, 0, 0), new IntVec3(-1, 0, 0), new IntVec3(0, 0, 1), new IntVec3(0, 0, -1)
		};
		public static readonly IntVec3[] AdjacentCells = CardinalDirections.Concat(new[] {
			new IntVec3(1, 0, 1), new IntVec3(-1, 0, 1), new IntVec3(1, 0, -1), new IntVec3(-1, 0, -1)
		}).ToArray();
	}
	public static class Log
	{
		public static readonly List<string> Warnings = new List<string>();
		public static readonly List<string> Errors = new List<string>();
		public static void Warning(string value) => Warnings.Add(value);
		public static void Error(string value) => Errors.Add(value);
	}
	public static class Rand
	{
		public static int Calls;
		public static float Value { get { Calls++; throw new InvalidOperationException("Rescue consumed RNG"); } }
		public static int Range(int min, int max) { Calls++; throw new InvalidOperationException("Rescue consumed RNG"); }
		public static int RangeInclusive(int min, int max) { Calls++; throw new InvalidOperationException("Rescue consumed RNG"); }
	}
}

namespace RimWorld
{
	using Verse;
	public static class MapGenerator
	{
		public static Map mapBeingGenerated;
		public static bool PlayerStartSpotValid;
		public static IntVec3 PlayerStartSpot;
		public static List<IntVec3> rootsToUnfog = new List<IntVec3>();
		public static List<CellRect> UsedRects = new List<CellRect>();
		public static bool TryGetVar<T>(string name, out T value)
		{
			value = (T)(object)UsedRects;
			return name == "UsedRects";
		}
		public static void Reset(Map map)
		{
			mapBeingGenerated = map; PlayerStartSpotValid = false; PlayerStartSpot = default;
			rootsToUnfog = new List<IntVec3>(); UsedRects = new List<CellRect>();
		}
	}
}

namespace AsAboveSoBelow
{
	using Verse;
	public sealed class ABBandMap
	{
		public bool Banded = true;
		public int surfaceBand = 1;
		public CellRect Surface;
		public CellRect RectOfBand(int index) => Surface;
	}
	public static class ABSkySync { public static bool Suspended; }
	public static class ABLog
	{
		public const string Tag = "[AASB test]";
		public static void Dev(string value) { }
	}
	public static class ABBandedGeneration
	{
		public static bool TryPendingSurfaceRect(Map map, out CellRect surface, out int index)
		{
			surface = default; index = 0; return false;
		}
		public static void ClearCellHard(Map map, IntVec3 c)
		{
			foreach (Thing thing in c.GetThingList(map).ToArray())
				if (!thing.Destroyed) thing.Destroy(DestroyMode.Vanish);
		}
	}
	public static class ABBandSafety
	{
		public static bool TryFindSurfaceCell(Map map, ABBandMap bands, IntVec3 from, bool standable, out IntVec3 result)
		{
			foreach (IntVec3 c in bands.Surface)
				if (c.GetEdifice(map) == null && !map.terrainGrid.TerrainAt(c).IsWater) { result = c; return true; }
			result = default; return false;
		}
	}
}
