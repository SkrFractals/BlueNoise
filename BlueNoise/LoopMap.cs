using System;
using System.Reflection;
using S2 = (short X, short T);
using S3 = (short X, short Y, short T);
using S4 = (short X, short Y, short Z, short T);

namespace BlueNoise;

internal abstract class LoopMap<V>/*(V size, uint sizeDivLength, ushort length, ushort minSide, byte dims, byte counts)*/ {

	internal LoopMap(V size, byte dims, byte spatialDims, byte counts) {
		static uint PowerOfThree(byte L) {
			uint e = 1;
			while (L-- > 0) e *= 3;
			return e;
		}
		if (spatialDims > dims)
			throw new("cannot have "+spatialDims+" within " + dims + " dimensions!");
		var powerSpatials = PowerOfThree(SpatialDims = spatialDims);
		var powerLinked = PowerOfThree(Linked = (byte)((Dims = dims) - spatialDims));
		Zero = GetZero();
		N = new (V, uint)[powerSpatials * (1 + 2 * powerLinked)];
		Values = new uint[Size = Vol(S = size)];
		Length = (short)(Size / (SizeDivLength = VolS(size)));
		Voidest = new short[1 << dims];
		oct = new(SizeDivLength, Lengths = GetLengths(S), MinSide = MinS(size), dims, Linked, counts);
		n = new uint[(D3 = (byte)(Dims * 3)) + (L3 = (byte)(Linked * 3))];
		nl = new byte[L3];
		link = link = new List<(uint start, uint end)>[Linked];
		//for (int i = 0; i < LoopMap3; nl[i++] = 1) { }
	}

	// TODO for initial testing, export white values as all tops (including torus wraps!) and blacks as bottoms. Non-linked pixels would be gray. Then test it all.

	internal uint[] Values; // serialized 1D value array
	internal float DesiredDifference = 1;
	internal readonly V Zero;
	internal readonly V S;
	internal readonly uint Size;

	protected readonly OctCells oct;
	protected readonly List<(uint start, uint end)>[] link; // Links adding more seamless time loops
	protected readonly byte Dims, SpatialDims, Linked, D3, L3; // Dimensions, Dimensions - Linked, Linked, Dimensions * 3, Linked * 3
	protected readonly (V direction, uint coord)[] N; // 3^n + 3^(n-1) = 4*3^(n-1)
	protected readonly short MinSide, Length; // Smallest Spatial Side, Loopable Time Length 
	protected readonly uint SizeDivLength; // Size of other non-time dimensions multiplied (like Width * Height in 3D)
	protected readonly short[] Voidest, Lengths;
	protected readonly uint[] n; // spatial neighbor step coords
	protected readonly byte[] nl;

	// TODO maybe replace with seedable hash?
	static readonly Random random = new();

	internal class OctCells {
		internal readonly short Side;
		internal readonly byte Log2Side;
		internal OctTree[] Children = [];
		protected readonly short[] Voidest;
		/// <summary>
		/// Makes the cells of octtrees
		/// </summary>
		/// <param name="minSide">smallest spatial side, the other spatial sides are also powers of two, so it can tile perfectly into them</param>
		/// <param name="sizeSpatial">area of everything divided by the linked dimensions, dividing this by the minSide^(dims-1) gives us the numbers of cells</param>
		/// <param name="length">the linked side lengths, it will make an arbitrary cut at the bottom of the octtree</param>
		/// <param name="dims">number of all dimensions</param>
		internal OctCells(uint sizeSpatial, short[] length, short minSide, byte dims, byte linkedDims, byte counts) {
			var s = Side = minSide; Log2Side = 0;
			while (0 < --s)
				++Log2Side;
			var d = dims;
			// cells = minSide^(dims-1): 
			var cells = sizeSpatial;
			while (--d > 0)
				cells /= (ushort)Side;
			Voidest = new short[cells];
			// i split the non-linked hyper volume into cubes of the smallest side sizes, then I've calculated how many cells are there tiling that volume.
			// i don't really know the aspect ratio of such hyperrectangle, but i don't need to know that in this init yet.
			// each cell is going to initialize the same way
			// (i could also simply Init only one, and then copy it to the others, but in a way, that each has its own equally initialized children and their children...)
			var f = new short[linkedDims];
			uint c = 0, fp = 1;
			for (int j = 0; j < linkedDims; ++j)
				fp *= (uint)(f[j] = (short)(length[j] / minSide)) + 1;
			Children = new OctTree[cells * fp];
			void Recurse(short[] t, byte ld) {
				if (ld-- <= 0) {
					for (int j = 0; j < cells; ++j)
						Children[c++] = new(t, minSide, length, dims, linkedDims, counts);
					return;
				}
				short[] nt = new short[t.Length];
				for (int j = 0; j < t.Length; ++j)
					nt[j] = t[j];
				for (int l = 0; l++ < f[ld]; nt[ld] += minSide)
					Recurse(nt, ld);
			}
			Recurse(new short[linkedDims], linkedDims);
		}
		internal int Min(byte countIndex) => MinFilled(Children, Voidest, countIndex);
	}
	internal class OctTree {
		internal float[] InvRatio;
		internal uint[] Count;
		internal readonly OctTree[] Children;
		internal OctTree(short[] lengthOffset, short minSide, short[] length, byte dims, byte linkedDims, byte counts) {
			Count = new uint[counts];
			// set up the ratio (how large is its real area compared to the octree volume it's supposed to occupy, for count density normalization)
			short minSideH = (short)(minSide >> 1);
			InvRatio = new float[linkedDims];
			var bothSplits = new bool[linkedDims];
			var spatial = 1 << (dims - linkedDims);
			var splits = 0;
			for (int i = 0; i < linkedDims; ++i) {
				InvRatio[i] = 1.0f / Math.Min(1, (float)(length[i] - lengthOffset[i]) / minSide);
				splits += (bothSplits[i] = lengthOffset[i] + minSideH >= length[i]) ? 1 : 0;
			}
			// maybe find a better threshold than a fixed 48? Make it a Leafsize parameter?
			if (minSide < 48) { // finish recursion (the lower this number is, the faster the algo, as it will switch to energy deeper on smaller kernels)
				Children = [];
				return;
			}
			Children = new OctTree[spatial << splits];
			int c = 0, cells = 1 << (dims - linkedDims);
			void Recurse(short[] t, byte ld) {
				if (ld-- <= 0) {
					for (int j = 0; j < cells; ++j)
						Children[c++] = new(t, minSideH, length, dims, linkedDims, counts);
					return;
				}
				short[] nt = new short[t.Length];
				for (int j = 0; j < t.Length; ++j)
					nt[j] = t[j];
				Recurse(nt, ld); // top
				if (bothSplits[ld]) {
					nt[ld] += minSideH;
					Recurse(nt, ld); // bottom
				}
			}
			Recurse(lengthOffset, linkedDims);
		}
		internal int Min(short[] voidest, byte countIndex) => MinFilled(Children, voidest, countIndex);
	}

	// Finds the Voidest bottom of the octtree:
	internal (ushort size, V coord) GetVoidest(byte countIndex) {
		byte s = oct.Log2Side;
		int mini = oct.Min(countIndex);
		var coord = BitMul(ToCoord((uint)mini, BitDiv(S, s)), s);
		for (var cell = oct.Children[mini]; cell.Children.Length > 0; OctAddCoord(ref coord, (uint)mini, s))
			cell = cell.Children[mini = cell.Min(Voidest, countIndex)];
		return ((ushort)(1 << s), coord);
	}
	// Increments the counters in the octree to easily find the most voidest after
	internal void Place(V coord, byte countIndex) {
		byte s = oct.Log2Side;
		var c = BitDiv(coord, s);
		var cell = oct.Children[OctSerialize(coord)];
		void Recurse() {
			SubBitMul(ref coord, c, s);
			++cell.Count[countIndex];
		}
		Recurse();
		while (cell.Children.Length > 0) {
			cell = cell.Children[BitSerialize(c = BitDiv(coord, ++s), 1)];
			Recurse();
		}
	}

	// Add a looping link between time levels, so that a cut interval between them will still seamlessly loop:
	internal void AddLink((uint start, uint end) newLink, byte linkedDim = 0) {
		(uint s, uint e) n = (newLink.start * SizeDivLength, newLink.end * SizeDivLength);
		foreach (var (s, e) in link[linkedDim]) 
			if (s == n.s || e == n.e || s == n.s || e == n.e)
				throw new("every link start and end must never touch any other link's start or end!");
		link[linkedDim].Add(n);
	}
	/// <summary>
	/// Get further neighbors from "from" in direction "to", used for directional breath-first search for a kernel
	/// </summary>
	/// <param name="from">neighbors next to this coordinate</param>
	/// <param name="to">only allowing neighbors step in the same sign of directions as this</param>
	/// <returns>all the allowed neighbors as pairs of (array coordinate, added direction vector)</returns>
	internal abstract Span<(V dir, uint coord)> GetSteps(uint from, V to);

	// Calculate the looped coordinates of steps {-1,0,+1} thorugh a non-linkable axis
	protected void Steps(int offset, short f, short mod, uint mul) {
		n[offset] = (uint)((f - 1 + mod) % mod * mul);
		n[offset + 1] = (uint)f * mul;
		n[offset + 2] = (uint)((f + 1) % mod * mul);
	}
	// For start and end bounds of allowed steps in "to" direction
	protected static (short start, short end) Bounds(short to) => ((short)(to >= 0 ? 0 : -1), (short)(to <= 0 ? 0 : 1));
	// Time neighbors can be duplicated through the links, on top of the trivial torus loop
	protected (short s, short e) BoundsL(short from, short to, int offset, byte linkedIndex = 0) {
		n[offset] = n[offset + 1] = n[offset + 2] = uint.MaxValue; // maxvalue stands for no found link
		(short s, short e) b;
		if (to >= 0)
			b.s = 0; // can't go previous
		else {
			b.s = -1; // can go previous (and through link starts into ends)
			foreach (var (start, end) in link[linkedIndex])
				if (from == start) {
					n[offset] = end; // remember the destination of the link
					break;
				}
		}
		if (to <= 0)
			b.e = 0; // can't go next
		else {
			b.e = 1; // can go next (and through link ends into starts)
			foreach (var (start, end) in link[linkedIndex])
				if (from == end) {
					n[offset + 2] = start; // remember the destination of the link
					break;
				}
		}
		return b;
	}

	private static int MinFilled(OctTree[] children, short[] voidestBuffer, byte countIndex) {
		int voidest = 0;
		float min = float.MaxValue;
		for (short i = 0; i < children.Length; ++i) {
			var ch = children[i];
			float filled = ch.Count[countIndex];
			for(int f = 0; f < ch.InvRatio.Length; ++f)
				filled *= ch.InvRatio[f];
			if (filled > min)
				continue;
			if (filled < min) {
				min = filled;
				voidest = 0;
			}
			voidestBuffer[voidest++] = i;
		}
		return voidestBuffer[random.Next(voidest)];
	}

	

	//internal abstract V GetRandom((ushort size, V coord) from);


	#region Coords
	protected abstract uint Vol(V v);
	protected abstract uint VolS(V v);
	protected abstract short[] GetLengths(V v);
	protected abstract short Min(V v);
	protected abstract short MinS(V v);
	protected abstract V ToCoord(uint serialized, V size);
	protected abstract uint BitSerialize(V coord, byte log);
	protected abstract uint OctSerialize(V coord);
	protected abstract void OctAddCoord(ref V coord, uint serialized, byte mulLog);
	protected abstract V BitDiv(V v, byte log);
	protected abstract V BitMul(V v, byte log);
	protected abstract void SubBitMul(ref V coord, V c, byte s);
	protected abstract V GetZero();
	internal abstract V AddTo(V from, ref V to);
	internal abstract int SizeSqr(V v);
	#endregion
}
// 1D NoiseMap (only the loopable+linked T time):
internal class LoopMap1(short size, byte counts) : LoopMap<short>(size, 1, 0, counts) {
	internal override Span<(short, uint)> GetSteps(uint from, short to) {
		// Convert 1D coordinate to T:
		var T = (short)from;
		// Coordinates of steps {-1,0,+1} through linked T
		Steps(0, T, Length, 1);
		(short ts, short te) = BoundsL(T, to, D3);
		// Build the neighbor list:
		ushort found = 0;
		// For T directions -> For linked T steps: Add neighbor
		for (short t = ts; t <= te; ++t) {
			int index = 1 + t; // 1 = 3*spatial + 1
			uint nv = n[index + L3];
			if (nv != uint.MaxValue)
				N[found++] = (t, nv); // There is a linked warp, explore it
			N[found++] = (t, n[index]); // Explore the regular time direction

		}
		return N.AsSpan(0, found);
	}
	internal override short GetRandom((ushort size, short coord) from) {
	
	}
	#region Coords
	protected override uint Vol(short v) => (uint)v;
	protected override uint VolS(short v) => 1;
	protected override short[] GetLengths(short T) => [T];
	protected override short Min(short v) => v;
	protected override short MinS(short v) => 1;
	protected override short ToCoord(uint serialized, short _) => (short)serialized;
	protected override uint BitSerialize(short coord, byte log) => (uint)coord;
	protected override uint OctSerialize(short coord) => (uint)coord;
	protected override void OctAddCoord(ref short coord, uint serialized, byte mulLog) => coord += (short)((serialized >> 1) << mulLog);
	protected override short BitDiv(short v, byte log) => (short)(v >> log);
	protected override short BitMul(short v, byte log) => (short)(v << log);
	protected override void SubBitMul(ref short coord, short c, byte s) => coord -= (short)(c << s);
	protected override short GetZero() => 0;
	internal override short AddTo(short from, ref short to) => to += from;
	internal override int SizeSqr(short v) => v * v;
	#endregion
}
// 2D NoiseMap (X = loopable, Y = loopable+linked T time):

internal class LoopMap2(S2 size, byte counts) : LoopMap<S2>(size, 2, 1, counts) {
	internal override Span<(S2, uint)> GetSteps(uint from, S2 to) {
		// Convert 1D coordinate to XT:
		var (X, T) = ToCoord(from, S);
		// Coordinates of steps {-1,0,+1} through looped X + linked T
		Steps(0, X, S.X, 1);
		Steps(3, T, Length, SizeDivLength);
		// For bounds of all allowed X steps
		short xs, xe, ts, te;
		(xs, xe) = Bounds(to.X);
		(ts, te) = BoundsL(T, to.T, D3);
		// Build the neighbor list:
		ushort found = 0;
		// For T directions -> For linked T steps -> For X steps: Add neighbor
		for (short t = ts; t <= te; ++t) {
			void ForSpatial(uint cT) { // For X steps:
				for (short x = xs; x <= xe; N[found++] = (new(x, t), n[++x] + cT)) { }
			}
			int index = 4 + t; // 4 = 3*spatial + 1
			uint nv = n[index + L3];
			if (nv != uint.MaxValue)
				ForSpatial(nv); // There is a linked warp, explore it
			ForSpatial(n[index]); // Explore the regular time direction
		}
		return N.AsSpan(0, found);
	}
	#region Coords
	protected override uint Vol(S2 v) => (uint)v.X * (uint)v.T;
	protected override uint VolS(S2 v) => (uint)v.X;
	protected override short[] GetLengths((short X, short T) v) => [v.T];
	protected override short Min(S2 v) => Math.Min(v.X, v.T);
	protected override short MinS(S2 v) => v.X;
	protected override S2 ToCoord(uint serialized, S2 size)
		=> ((short)(serialized % size.X), (short)(serialized / (ushort)size.X));
	protected override uint BitSerialize(S2 coord, byte log) => (uint)(coord.X + (coord.T << 1));
	protected override uint OctSerialize(S2 coord) => (uint)(coord.X + coord.T * (S.X >> oct.Log2Side));
	protected override void OctAddCoord(ref S2 coord, uint serialized, byte mulLog) {
		coord.X += (short)((serialized & 1) << mulLog);
		coord.T += (short)((serialized >> 1) << mulLog);
	}
	protected override S2 BitDiv(S2 v, byte log) => ((short)(v.X >> log), (short)(v.T >> log));
	protected override S2 BitMul(S2 v, byte log) => ((short)(v.X << log), (short)(v.T << log));
	protected override void SubBitMul(ref S2 coord, S2 c, byte s) {
		coord.X -= (short)(c.X << s);
		coord.T -= (short)(c.T << s);
	}
	protected override S2 GetZero() => (0, 0);
	internal override S2 AddTo(S2 from, ref S2 to) => to = ((short)(from.X + to.X), (short)(from.T + to.T));
	internal override int SizeSqr(S2 v) => v.X * v.X + v.T * v.T;
	#endregion
}
// 3D NoiseMap (XY = loopable, Z = loopable+linked T time):
internal class LoopMap3(S3 size, byte counts) : LoopMap<S3>(size, 3, 2, counts) {
	internal override Span<(S3, uint)> GetSteps(uint from, S3 to) {
		// Convert 1D coordinate to XYT:
		var (X, Y, T) = ToCoord(from, S);
		// Coordinates of steps {-1,0,+1} through looped XY + linked T
		Steps(0, X, S.X, 1);
		Steps(3, Y, S.Y, (uint)S.X);
		Steps(6, T, Length, SizeDivLength);
		// For bounds of all allowed XY steps
		short xs, xe, ys, ye, ts, te;
		(xs, xe) = Bounds(to.X);
		(ys, ye) = Bounds(to.Y);
		(ts, te) = BoundsL(T, to.T, D3);
		// Build the neighbor list:
		ushort found = 0;
		// For T directions -> For linked T steps:
		for (short t = ts; t <= te; ++t) {
			void ForSpatial(uint cT) {
				// Build the neighbor direction vt; For Y steps:
				for (short y = ys; y <= ye; ++y) {
					// +Y to the coord/vector; For X steps: Add neighbor
					var cYT = n[y + 4] + cT;
					for (short x = xs; x <= xe; N[found++] = (new(x, y, z, t), n[++x] + cYT)) { }
				}
			}
			int index = 7 + t; // 7 = 3*spatial + 1
			uint nv = n[index + L3];
			if (nv != uint.MaxValue)
				ForSpatial(nv); // There is a linked warp, explore it
			ForSpatial(n[index]); // Explore the regular time direction
		}
		return N.AsSpan(0, found);
	}
	#region Coords
	protected override uint Vol(S3 v) => (uint)v.X * (uint)v.Y * (uint)v.T;
	protected override uint VolS(S3 v) => (uint)v.X * (uint)v.Y;
	protected override short[] GetLengths((short X, short Y, short T) v) => [v.T];
	protected override short Min(S3 v) => Math.Min(Math.Min(v.X, v.Y), v.T);
	protected override short MinS(S3 v) => Math.Min(v.X, v.Y);
	protected override S3 ToCoord(uint serialized, S3 size)
		=> ((short)(serialized % size.X), (short)((serialized /= (ushort)size.X) % size.Y), (short)(serialized / (ushort)size.Y));
	protected override uint BitSerialize(S3 coord, byte log) => (uint)(coord.X + ((coord.Y + (coord.T << 1)) << 1));
	protected override uint OctSerialize(S3 coord) => (uint)(coord.X + (coord.Y + coord.T * (S.Y >> oct.Log2Side)) * (S.X >> oct.Log2Side));
	protected override void OctAddCoord(ref S3 coord, uint serialized, byte mulLog) {
		coord.X += (short)((serialized & 1) << mulLog);
		coord.Y += (short)(((serialized >> 1) & 1) << mulLog);
		coord.T += (short)((serialized >> 2) << mulLog);
	}
	protected override S3 BitDiv(S3 v, byte log) => ((short)(v.X >> log), (short)(v.Y >> log), (short)(v.T >> log));
	protected override S3 BitMul(S3 v, byte log) => ((short)(v.X << log), (short)(v.Y << log), (short)(v.T << log));
	protected override void SubBitMul(ref S3 coord, S3 c, byte s) {
		coord.X -= (short)(c.X << s);
		coord.Y -= (short)(c.Y << s);
		coord.T -= (short)(c.T << s);
	}
	protected override S3 GetZero() => (0, 0, 0);
	internal override S3 AddTo(S3 from, ref S3 to) => to = ((short)(from.X + to.X), (short)(from.Y + to.Y), (short)(from.T + to.T));
	internal override int SizeSqr(S3 v) => v.X * v.X + v.Y * v.Y + v.T * v.T;
	#endregion
}
// multidimensional NoiseMap ([0..L-2] = loopable, [L-1] = loopable+linked T time):
internal class LoopMap4(S4 size, byte counts) : LoopMap<S4>(size, 4, 3, counts) {

	private readonly uint WH = (uint)size.X * (uint)size.Y; // Width * Height
	internal override Span<(S4, uint)> GetSteps(uint from, S4 to) {
		// Convert 1D coordinate to XYZT:
		var (X, Y, Z, T) = ToCoord(from, S);
		// Coordinates of steps {-1,0,+1} through looped XYZ + linked T
		Steps(0, X, S.X, 1);
		Steps(3, Y, S.Y, (uint)S.X);
		Steps(6, Z, S.Z, WH);
		Steps(9, T, Length, SizeDivLength);
		// For bounds of all allowed XY steps
		short xs, xe, ys, ye, zs, ze, ts, te;
		(xs, xe) = Bounds(to.X);
		(ys, ye) = Bounds(to.Y);
		(zs, ze) = Bounds(to.Z);
		(ts, te) = BoundsL(T, to.T, D3);
		// Build the neighbor list:
		ushort found = 0;
		// For T directions -> For linked T steps:
		for (short t = ts; t <= te; ++t) {
			void ForSpatial(uint cT) {
				// Build the neighbor direction vt; For Z steps:
				for (short z = zs; z <= ze; ++z) {
					// +Z to the coord/vector; For Y steps:
					var cZT = n[z + 7] + cT;
					for (short y = ys; y <= ye; ++y) {
						// +Y to the coord/vector; For X steps: Add neighbor
						var cYZT = n[y + 4] + cZT;
						for (short x = xs; x <= xe; N[found++] = (new(x, y, z, t), n[++x] + cYZT)) { }
					}
				}
			}
			int index = 10 + t; // 10 = 3*spatial + 1
			uint nv = n[index + L3];
			if (nv != uint.MaxValue)
				ForSpatial(nv); // There is a linked warp, explore it
			ForSpatial(n[index]); // Explore the regular time direction
		}
		return N.AsSpan(0, found);
	}
	#region Coords
	protected override uint Vol(S4 v) => (uint)v.X * (uint)v.Y * (uint)v.Z * (uint)v.T;
	protected override uint VolS(S4 v) => (uint)v.X * (uint)v.Y * (uint)v.Z;
	protected override short[] GetLengths((short X, short Y, short Z, short T) v) => [v.T];
	protected override short Min(S4 v) => Math.Min(Math.Min(v.X,v.Y),Math.Min(v.Z,v.T));
	protected override short MinS(S4 v) => Math.Min(Math.Min(v.X, v.Y), v.Z);
	protected override S4 ToCoord(uint serialized, S4 size) 
		=> ((short)(serialized % size.X), (short)((serialized /= (ushort)size.X) % size.Y), (short)((serialized /= (ushort)size.Y) % size.Z), (short)(serialized / (ushort)size.Z));
	protected override uint BitSerialize(S4 coord, byte log) => (uint)(coord.X + ((coord.Y + ((coord.Z + (coord.T << 1)) << 1)) << 1));
	protected override uint OctSerialize(S4 coord) => (uint)(coord.X + (coord.Y + (coord.Z + coord.T * (S.Z >> oct.Log2Side)) * (S.Y >> oct.Log2Side)) * (S.X >> oct.Log2Side));
	protected override void OctAddCoord(ref S4 coord, uint serialized, byte mulLog) {
		coord.X += (short)((serialized & 1) << mulLog);
		coord.Y += (short)(((serialized >> 1) & 1) << mulLog);
		coord.Z += (short)(((serialized >> 2) & 1) << mulLog);
		coord.T += (short)((serialized >> 3) << mulLog);
	}
	protected override S4 BitDiv(S4 v, byte log) => ((short)(v.X >> log), (short)(v.Y >> log), (short)(v.Z >> log), (short)(v.T >> log));
	protected override S4 BitMul(S4 v, byte log) => ((short)(v.X << log), (short)(v.Y << log), (short)(v.Z << log), (short)(v.T << log));
	protected override void SubBitMul(ref S4 coord, S4 c, byte s) {
		coord.X -= (short)(c.X << s);
		coord.Y -= (short)(c.Y << s);
		coord.Z -= (short)(c.Z << s);
		coord.T -= (short)(c.T << s);
	}
	protected override S4 GetZero() => (0, 0, 0, 0);
	internal override S4 AddTo(S4 from, ref S4 to) => to = ((short)(from.X + to.X), (short)(from.Y + to.Y), (short)(from.Z + to.Z), (short)(from.T + to.T));
	internal override int SizeSqr(S4 v) => v.X * v.X + v.Y * v.Y + v.Z * v.Z + v.T * v.T;
	#endregion
}
internal class LoopMapX : LoopMap<short[]> {
	public LoopMapX(short[] size, byte spatialDims, byte counts) : base(size, (byte)size.Length, spatialDims, counts) {
		p = new (short, short)[Dims];
		v = new short[Dims];
	}
	private readonly (short s, short e)[] p;
	private readonly short[] v;
	internal override Span<(short[], uint)> GetSteps(uint from, short[] to) {
		// Convert 1D coordinate to XYZT:
		short[] f = ToCoord(from, S);
		// Coordinates of steps {-1,0,+1} through looped XYZ + linked T
		uint TS = 1;
		for (int i = 0; i < Dims; TS *= (uint)S[i++])
			Steps(3 * i, f[i], S[i], TS);
		// For bounds of all allowed XY steps
		for (int i = SpatialDims; 0 <= --i; p[i] = Bounds(to[i])) { }
		for (int i = SpatialDims, d = D3; i < Dims; ++i, d += 3)
			p[i] = BoundsL(f[i], to[i], d, (byte)(i - SpatialDims));
		ushort found = 0;
		void ForSpatial(byte d, uint c) {
			if (d <= 0)
				N[found++] = ((short[])v.Clone(), c);
			else
				for (short di = (short)(d * 3 + 1), i = p[d].s, e = p[d].e; i <= e; ForSpatial(d, c + n[di + i++]))
					v[d] = i;
		}
		void ForLinked(byte d, uint c) {
			if (d <= SpatialDims)
				ForSpatial(d, c); // No more linked dimension, continue the spatial ones
			else // For Linked directions:
				for (short di = (short)(d * 3 + 1), i = p[d].s, e = p[d].e; i <= e; ++i) {
					v[d] = i; // record the step direction vector coordinate
					int index = di + i;
					uint nv = n[index + L3];
					if (nv != uint.MaxValue)
						ForLinked(d, c + nv);
					ForLinked(d, c + n[index]);
				}
		}
		ForLinked(Dims, 0);
		return N.AsSpan(0, found);
	}
	#region Coords
	protected override uint Vol(short[] v) => VolT(v, Dims);
	protected override uint VolS(short[] v) => VolT(v, SpatialDims);
	protected override short[] GetLengths(short[] v) {
		short[] f = new short[Linked];
		for (int i = 0; i < Linked; ++i)
			f[i] = v[i + SpatialDims];
		return f;
	}
	private static uint VolT(short[] v, byte terminator) {
		uint vol = 1;
		for (int i = 0; i < terminator; ++i)
			vol *= (uint)v[i];
		return vol;
	}
	protected override short Min(short[] v) => MinT(v, Dims);
	protected override short MinS(short[] v) => MinT(v, SpatialDims);
	protected static short MinT(short[] v, byte terminator) {
		short m = short.MaxValue;
		for (int i = 0; i < terminator; ++i)
			m = Math.Min(m, v[i]);
		return m;
	}
	protected override short[] ToCoord(uint serialized, short[] size) {
		short[] f = new short[Dims];
		for (int i = 0; i < Dims; serialized /= (uint)size[i++])
			f[i] = (short)(serialized % size[i]);
		return f;
	}
	protected override uint BitSerialize(short[] coord, byte log) {
		uint ser = 0;
		for (int i = Dims; 0 <= --i; ser += (uint)coord[i])
			ser <<= log;
		return ser;
	}
	protected override uint OctSerialize(short[] coord) {
		uint ser = 0;
		for (int i = Dims; 0 <= --i; ser += (uint)coord[i])
			ser *= (uint)(S[i] >> oct.Log2Side);
		return ser;
	}
	protected override void OctAddCoord(ref short[] coord, uint serialized, byte mulLog) {
		for (int i = 0; i < Dims; ++i)
			coord[i] += (short)(((serialized >> i) & 1) << mulLog);
	}
	protected override short[] BitDiv(short[] v, byte log) => BitDivT(v, log, Dims);
	private static short[] BitDivT(short[] v, byte log, byte terminator) {
		var r = new short[terminator];
		for (int i = 0; i < terminator; ++i)
			r[i] = (short)(v[i] >> log);
		return r;
	}
	protected override short[] BitMul(short[] v, byte log) {
		var r = new short[Dims];
		for (int i = 0; i < Dims; ++i)
			r[i] = (short)(v[i] << log);
		return r;
	}
	protected override void SubBitMul(ref short[] coord, short[] c, byte s) {
		for (int i = Dims; 0 <= --i; coord[i] -= (short)(c[i] << s)) { }
	}
	protected override short[] GetZero() => new short[Dims];
	internal override short[] AddTo(short[] from, ref short[] to) {
		for (int i = 0; i < Dims; to[i] += from[i++]) { }
		return to;
	}
	internal override int SizeSqr(short[] v) {
		int s = 0;
		for (int i = 0; i < Dims; s += v[i] * v[i++]) { }
		return s;
	}
	#endregion
}