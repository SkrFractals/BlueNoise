namespace BlueNoise;
internal static class BlueNoiseOptimizer {
	// Gets the ideal choice of chunks to throw (the biggest possible set, and if there are multiple, choose the ones with bigger chunks), and what interlacing shift sequence lands at them.
	internal static (List<int> chunks, List<int> shifts) Best(int maxL, int jumpSize) {
		if (jumpSize <= 1)
			return ([], []); // jumpSize 0 would crash, 1 doesn't have space for interlacing, but let's return empty early as well.
		int mod = jumpSize << 1;
		int M = maxL % mod;
		int vals = mod - 3;
		List<int> availChunk = [];
		//implementing the modular maxT % (2*jumpSize) pattern of which chunk's interval set does every possible interlacing shift land me at.
		for (int i = 0; i < vals; ++i) {
			availChunk.Add(M++);
			M %= mod;
		}
		int bestCount = 0;
		(List<int>, List<int>) best = ([], []);
		// gets the most chunks i can get starting from this interlacing shift index
		(List<int> chunks, List<int>) AddFrom(int fromShift) {
			(int chunk, int shift) Next(int f) {
				for (int i = f; i < mod; ++i)
					if (availChunk.Contains(i)) return (i, availChunk.IndexOf(i));
				return (-1, -1);
			}
			(List<int> chunks, List<int> shifts) l = ([], []);
			var next = Next(fromShift);
			if (next.chunk < 0)
				return l;
			for (int a = 0; a < fromShift; ++a) {
				l.chunks.Add(next.chunk);
				l.shifts.Add(next.shift);
				next = Next(next.chunk + 1);
				if (next.chunk < 0)
					return l;
			}
			return l;
		}
		// try every starting shift index, and remember the best one.
		// Since I'm iterating upwards, the sets of smaller indexed (bigger) chunks will be preferred if the count is equal
		for (int i = 0; i < mod; ++i) {
			var list = AddFrom(i);
			if (list.chunks.Count > bestCount) {
				bestCount = list.chunks.Count;
				best = list;
			} else continue;
		}
		return best;
	}
	internal static (int topChunks, int bottomStart, int bottomFrames) GetInterlace(int shift, ushort JumpSize, ushort tempSize, ushort maxSize, Action<int, int> act) {
		int a = JumpSize, b = JumpSize - 1, interSize, topFrames = -1, bottomStart = -1, j2 = JumpSize << 1; bool f = false;
		while (--shift >= 0) if (f = !f) --a; else --b;
		for (int l = 0; tempSize - j2 * l - 1 - a - b > 0; ++l)
			if ((interSize = tempSize - (tempSize - j2 * l - a - b) + 2) >= Math.Max((ushort)2, JumpSize) && interSize <= maxSize) 
				act(topFrames = JumpSize * l + b + 1, bottomStart = tempSize - JumpSize * l - 1 - a);
		return (topFrames, bottomStart, tempSize - bottomStart);
	}
}
// How to use:
// Load once: sampler = new(filePath);
// Sample noise pixels at locations "x,y" and the animation frame "frame": sampler.Sample(frame, x, y);
public abstract class BlueNoiseSampler<S> {

	// PUBLIC:

	/// <summary>
	/// Initialize the Sampler.
	/// </summary>
	/// <param name="path">path to the texture file. It must exist, and support your needed time intervals</param>
	/// <param name="expected">number of dimensions, the user won't be calling this argument</param>
	/// <param name="frames">If 0, it will load in universal mode, and you will need to call PrecalculateLoop(frames) later intstead. Otherwise, it will only load that fixed length</param>
	public BlueNoiseSampler(string path, byte expected, ushort frames) {
		FrameSize = ParsePath(TryPath(path, expected, out JumpSize, out MaxT), out Bounds);
		JumpSize2 = (ushort)(JumpSize <= 0 ? 1 : JumpSize << 1);
		MinT = Math.Max((ushort)2, JumpSize);
		Load(path, out Blue, out BlueWhite, out BlueOffsets, frames);
		unsafe { sampleFunc = (IsFixed = frames > 0 || JumpSize == 0) ? &SampleSerialized_Fixed : &SampleSerialized_Universal; }
	}
	// Dimension bounds of the texture, the last value is the largest possible divisor of the time length interval
	public readonly S Bounds;

	#region Sample
	// precalculate blue noise texture interval and array offset, call this when user changes the animation length
	// must be called any time you want to change the time interval you are going to be using, only call this in universal mode
	public void PrecalculateLoop(uint frames) {
		if (IsFixed)
			return; // or throw if you want to avoid calling this when you shouldn't
		var newMod = FindInterval(frames, MaxT);
		if (FrameMod == newMod && UsingBlue.Length > 0)
			return;
		// Where the "texIndex=BlueFrameMod%BlueJumpSize2" starts + Add where our interval is starting to the offset (I hope I calculated this line right):
		BlueOffset = BlueOffsets[FrameMod = newMod];
		UsingBlue = Blue[newMod % JumpSize2];
	}
	// the safest way to sample, loops every coordinate, and used whatever mode was initialized. As long as you have properly initialized a fixed or universal mode, it should work:
	public abstract float SampleSafe(S coord);
	// only use this if your spatial coordinates aren't out of Bounds bounds (Coord[i] = Coord[i] % Bounds[i]):
	public abstract float Sample(S coord);
	// only use this if you are sure you have correctly calculated the serialized index of your noise:
	// example serialized index for BlueNoiseSampler4(coords=(X,Y,Z,T)): (X % Bounds.X) + ((Y % Bounds.Y) + ((Z % Bounds.Z) + T % FrameMod * Bounds.Z) * Bounds.Y) * Bounds.X;
	public unsafe float SampleUniversal(uint serialized) => sampleFunc(this, serialized);
	// only use this if you are sure you have initialized an universal mode: "new(path).PrecalculateLoop(frames);", and you have correctly calculated the serialized index of your noise:
	public float DirectSampleSerialized_Universal(uint serialized) => UsingBlue[BlueOffset + serialized] + BlueWhite[serialized];
	// only use this if you are sure you have initialized a fixed mode: "new(path, frames);", and you have correctly calculated the serialized index of your noise:
	public float DirectSampleSerialized_Fixed(uint serialized) => UsingBlue[serialized] + BlueWhite[serialized];

	private static float SampleSerialized_Universal(BlueNoiseSampler<S> sampler, uint serialized) => sampler.DirectSampleSerialized_Universal(serialized);
	private static float SampleSerialized_Fixed(BlueNoiseSampler<S> sampler, uint serialized) => sampler.DirectSampleSerialized_Fixed(serialized);
	#endregion

	// PRIVATE:

	#region Private Variables
	unsafe protected delegate*<BlueNoiseSampler<S>, uint, float> sampleFunc = &SampleSerialized_Universal;
	protected ushort FrameMod, JumpSize, JumpSize2, MinT, MaxT;
	protected uint BlueOffset;
	protected uint[] BlueOffsets;
	private byte[] UsingBlue = [];
	private readonly byte[][] Blue = []; // loaded blue texture as a 1D array
	private readonly float[] BlueWhite = [];
	private readonly bool IsFixed;
	private readonly uint FrameSize;
	#endregion

	
	#region Command Parse
	protected static string PF(string name, string parse)
		=> "Failed to parse " + name + ", string = " + parse + ". Must be an integer!";
	protected static string[] TryPath(string path, int expected, out ushort jumpSize, out ushort tempSize) {
		var fileExt = Path.GetFileName(path).Split('.');
		var x = fileExt.Length != 2 || fileExt[1] != "blue" ? throw new(fileExt + " is not a *.blue file!") : fileExt[0].Split('x');
		if (x.Length != expected + 1 || x[0] != "B")
			throw new("Invalid file name. Expected Bx<" + expected + "xBounds" + ">.blue");
		string parse = x[expected - 2];
		return ushort.TryParse(parse, out jumpSize)
			? ushort.TryParse(parse = x[expected - 1], out tempSize)
			? x
			: throw new(PF("tempSize", parse))
			: throw new(PF("jumpSize", parse));
	}
	protected static void Parse(string parse, out ushort value, string name) { if (!ushort.TryParse(parse, out value)) throw new(PF(name, parse)); }
	protected abstract uint ParsePath(string[] dims, out S size);
	#endregion

	#region Init
	private void Load(string path, out byte[][] blue, out float[] white, out uint[] offsets, ushort fixedLength = 0) {
		uint maxSize;
		using var fs = File.OpenRead(path);
		if (JumpSize <= 0) {
			// no extra intervals, only a single torus, so simply load the whole file into the single chunk it is
			if (fixedLength > 0 && fixedLength % MaxT != 0)
				throw new("cannot load an interval that dones't have the temporal size as a divisor. This texture only supports a single looping interval of its own temporal size.");
			UsingBlue = new byte[maxSize = FrameSize * (FrameMod = MaxT)];
			fs.ReadExactly(UsingBlue, 0, (int)maxSize);
			blue = [];
			offsets = [];
		} else {
			ushort tex0 = (ushort)(MaxT % JumpSize2);
			int chunkIndex = 0;
			int InterChunk() => (tex0 + JumpSize2 - chunkIndex) % JumpSize2;
			var (chunks, shifts) = BlueNoiseOptimizer.Best(MaxT, JumpSize);
			void ProcessChunks(ref byte[][] blue, Action<int, ushort, byte[][]> act) {
				for (ushort i = 0; i < JumpSize2; ++i) {
					chunkIndex = chunks.IndexOf(i);
					if (chunkIndex < 0)
						act(i, (ushort)(MaxT - i), blue);// not alraedy interlaced inside previous chunk
				}
			}
			if (fixedLength == 0) {
				// Load the who texture and save all the chunks, let PrecalculateLoop(frames) to switch between intervals later:
				(maxSize, blue) = (FrameSize * MaxT, new byte[JumpSize2][]);
				ProcessChunks(ref blue, (i, tempSize, blue) => {
					var chunkSize = tempSize * FrameSize;
					// we will always load the chunk when universal
					byte[] chunkBlue = blue[(tex0 + JumpSize2 - i) % JumpSize2] = new byte[chunkSize];
					fs.ReadExactly(chunkBlue, 0, (int)chunkSize);
					if (i < chunks.Count) {
						// there is another chunk interlaced in this one, so copy the correct intervals:
						(int topFrames, int bottomStart, int bottomFrames) = BlueNoiseOptimizer.GetInterlace(shifts[chunkIndex], JumpSize, tempSize, tempSize, (_, _) => { });
						int targetFrames = MaxT - chunks[chunkIndex], interTempSize = topFrames + bottomFrames;
						if (targetFrames != interTempSize)
							throw new("interlaced chunk does not match the supposed number of frames! TargetSum=" + targetFrames + ", Top=" + topFrames + ", Bottom:" + bottomFrames);
						byte[] interBlue = blue[InterChunk()] = new byte[interTempSize * FrameSize];
						Buffer.BlockCopy(chunkBlue, bottomStart * (int)FrameSize, interBlue, 0, bottomFrames * (int)FrameSize); // copy the bottom half of the source to the top half of the destination
						Buffer.BlockCopy(chunkBlue, 0, interBlue, bottomFrames * (int)FrameSize, topFrames * (int)FrameSize); // copy the top half of the source to the bottom half of the destination
					}
				});
				// precalculate offsets where each iterval starts for every chunk:
				offsets = new uint[MaxT];
				for (int b = JumpSize; b < MaxT; ++b)
					offsets[b] = (uint)(MaxT - b) / JumpSize2 * JumpSize;
			} else {
				// only find and load a single interval in the file:
				blue = [];
				UsingBlue = new byte[maxSize = FrameSize * fixedLength];
				// Where the "textIndex=BlueFrameMod%BlueJumpSize2" starts + Add where our interval is starting to the offset (I hope I calculated this line right):
				FrameMod = FindInterval(fixedLength, MaxT);
				ProcessChunks(ref blue, (i, tempSize, blue) => {
					uint chunkSize = tempSize * FrameSize;
					byte[] chunkBlue;
					if (i < chunks.Count) {
						// there is another chunk interlaced in this one, so copy the correct intervals:
						(int topFrames, int bottomStart, int bottomFrames) = BlueNoiseOptimizer.GetInterlace(shifts[chunkIndex], JumpSize, tempSize, FrameMod, (_, _) => { });
						if (InterChunk() == FrameMod % JumpSize2) {
							// this chunk contans the interlaced interval of our desired size FrameMod:
							// since we are now sure the frames are in this chunk - load it
							fs.ReadExactly(chunkBlue = new byte[chunkSize], 0, (int)chunkSize);
							// read both halves of our interval into UsingBlue
							Buffer.BlockCopy(chunkBlue, bottomStart * (int)FrameSize, UsingBlue, 0, bottomFrames * (int)FrameSize); // copy the bottom half of the source to the top half of the destination
							Buffer.BlockCopy(chunkBlue, 0, UsingBlue, bottomFrames * (int)FrameSize, topFrames * (int)FrameSize); // copy the top half of the source to the bottom half of the destination
							return; // we have found and loaded the chunk with our desired interval, no need to continue searching
						}
					}
					if ((tex0 + JumpSize2 - i) % JumpSize2 == FrameMod % JumpSize2) {
						// this chunk contains the middle (or whole chunk "l == 0") interval of our desired size FrameMod:
						// since we are now sure the frames are in this chunk - load it
						fs.ReadExactly(chunkBlue = new byte[chunkSize], 0, (int)chunkSize);
						for (int l = (tempSize - JumpSize) / JumpSize2; 0 <= l; --l) {
							int interval = tempSize - JumpSize2 * l;
							if (FrameMod == interval) {
								// found that interval: read it into UsingBlue
								Buffer.BlockCopy(chunkBlue, JumpSize * l * (int)FrameSize, UsingBlue, 0, interval * (int)FrameSize);
								return; // we have found and loaded the chunk with our desired interval, no need to continue searching
							}
						}
					}
					// our desired chunk is not this one, so skip to the next one without reading it:
					fs.Seek(chunkSize, SeekOrigin.Current);
				});
				// no offsets in the fixed mode, there is only one interval and it is BlockCopied without an offset
				offsets = [];
			}
		}
		// generate white noise to add random frac precision intervals:
		white = new float[maxSize];
		for (ushort i = 0; i < maxSize; ++i) {
			ulong z = i;
			z += 0x9E3779B97F4A7C15UL;
			z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
			z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
			z ^= z >> 31;
			white[i] = BitConverter.UInt32BitsToSingle((uint)(z >> 41) | 0x3F800000) - 1.0f;
		}
	}
	private ushort FindInterval(uint frames, ushort MaxT) { // temporal loop interval
		for (ushort i = MaxT; i >= MinT; --i)
			if (frames % i == 0)
				return i;
		throw new("No compatible temporal blue-noise period found. The animation length must have a divisor between 4 and " + MaxT + ". Consider changing the animation duration slightly");
	}
	#endregion
}

#region Dimensional Implementations
public class BlueNoiseSampler1(string path, ushort frames = 0) : BlueNoiseSampler<ushort>(path, 2, frames) {
	protected override uint ParsePath(string[] dims, out ushort size) => size = 1;
	// Loops: frame % FrameMod
	public unsafe override float SampleSafe(ushort coord) => sampleFunc(this, (uint)(coord % FrameMod));
	public unsafe override float Sample(ushort coord) => sampleFunc(this, (uint)(coord % FrameMod));
}
public class BlueNoiseSampler2(string path, ushort frames = 0) : BlueNoiseSampler<(ushort X, ushort T)>(path, 3, frames) {
	protected override uint ParsePath(string[] dims, out (ushort X, ushort T) size) {
		Parse(dims[size.T = 0], out size.X, "width");
		return size.X;
	}
	// Loops: x % Size, frame % FrameMod
	public unsafe override float SampleSafe((ushort X, ushort T) coords) => sampleFunc(this, (uint)(coords.X % Bounds.X + coords.T % FrameMod * Bounds.X));
	public unsafe override float Sample((ushort X, ushort T) coords) => sampleFunc(this, (uint)(coords.X + coords.T % FrameMod * Bounds.X));
}
public class BlueNoiseSampler3(string path, ushort frames = 0) : BlueNoiseSampler<(ushort X, ushort Y, ushort T)>(path, 4, frames) {
	protected override uint ParsePath(string[] dims, out (ushort X, ushort Y, ushort T) size) {
		Parse(dims[size.T = 0], out size.X, "width");
		Parse(dims[1], out size.Y, "height");
		return (uint)size.X * size.T;
	}
	// Loops: x % Bounds.X, y % Bounds.Y, frame % FrameMod
	public unsafe override float SampleSafe((ushort X, ushort Y, ushort T) coords)
		=> sampleFunc(this, (uint)(coords.X % Bounds.X + (coords.Y % Bounds.Y + coords.T % FrameMod * Bounds.Y) * Bounds.X));
	public unsafe override float Sample((ushort X, ushort Y, ushort T) coords)	
		=> sampleFunc(this, (uint)(coords.X + (coords.Y + coords.T % FrameMod * Bounds.Y) * Bounds.X));
}
public class BlueNoiseSampler4(string path, ushort frames = 0) : BlueNoiseSampler<(ushort X, ushort Y, ushort Z, ushort T)>(path, 5, frames) {
	protected override uint ParsePath(string[] dims, out (ushort X, ushort Y, ushort Z, ushort T) size) {
		Parse(dims[size.T = 0], out size.X, "width");
		Parse(dims[1], out size.Y, "height");
		Parse(dims[2], out size.Z, "depth");
		return (uint)size.X * size.T * size.Z;
	}
	// Loops: x % Bounds.X, y % Bounds.Y, z % Bounds.Z, frame % FrameMod
	public unsafe override float SampleSafe((ushort X, ushort Y, ushort Z, ushort T) coords)
		=> sampleFunc(this, (uint)(coords.X % Bounds.X + (coords.Y % Bounds.Y + (coords.Z % Bounds.Z + coords.T % FrameMod * Bounds.Z) * Bounds.Y) * Bounds.X));
	public unsafe override float Sample((ushort X, ushort Y, ushort Z, ushort T) coords)
		=> sampleFunc(this, (uint)(coords.X + (coords.Y + (coords.Z + coords.T % FrameMod * Bounds.Z) * Bounds.Y) * Bounds.X));
}
public class BlueNoiseSamplerX(string path, byte expectedDimensons, ushort frames = 0) : BlueNoiseSampler<ushort[]>(path, expectedDimensons, frames) {
	protected override uint ParsePath(string[] dims, out ushort[] size) {
		int i = dims.Length - 1;
		uint frameSize = 1;
		size = new ushort[dims.Length];
		size[i] = 0;
		while (0 <= --i) {
			Parse(dims[i], out var s, "dim" + (i + 1));
			frameSize *= size[i] = s;
		}
		return frameSize;
	}
	// Loops: coord[i] % Bounds[i], frame % FrameMod
	public unsafe override float SampleSafe(ushort[] coords) {
		short i = (short)Bounds.Length;
		uint s = (uint)(coords[--i] % FrameMod);
		while (0 <= --i) {
			var d = Bounds[i];
			s = s * d + (ushort)(coords[i] % d);
		}
		return sampleFunc(this, s);
	}
	public unsafe override float Sample(ushort[] coords) {
		short i = (short)Bounds.Length;
		uint s = (uint)(coords[--i] % FrameMod);
		while (0 <= --i)
			s = s * Bounds[i] + coords[i];
		return sampleFunc(this, s);
	}
}
#endregion


// The Sampler supports more than one linked dimension (supporting arbitrary looping intervals)
// But it is more complicated, and if universal, the single frame memory is not continuous, and can be a little a slower to sample
/*public abstract class BlueNoiseSamplerM {

	// PUBLIC:

	/// <summary>
	/// Initialize the Sampler.
	/// </summary>
	/// <param name="path">path to the texture file. It must exist, and support your needed time intervals</param>
	/// <param name="dims">number of dimensions, the user won't be calling this argument</param>
	/// <param name="frames">If 0, it will load in universal mode, and you will need to call PrecalculateLoop(frames) later intstead. Otherwise, it will only load that fixed length</param>
	public BlueNoiseSamplerM(string path, byte dims, uint[] frames) {
		FrameSize = ParsePath(TryPath(path, dims, out JumpSize, out MaxL), out Bounds);
		JumpSize2 = new ushort[linked = (byte)frames.Length];
		MinL = new ushort[linked];
		for (int i = 0; i < linked; ++i) {
			JumpSize2[i] = (ushort)(JumpSize[i] <= 0 ? 1 : JumpSize[i] << 1);
			MinL[i] = Math.Max((ushort)2, JumpSize[i]);
		}

		Load(path, out Blue, out BlueWhite, out BlueOffsets, frames);
		unsafe { sampleFunc = (IsFixed = frames > 0 || JumpSize == 0) ? &SampleSerialized_Fixed : &SampleSerialized_Universal; }
	}
	// Dimension bounds of the texture, the last value is the largest possible divisor of the time length interval
	public readonly S Bounds;

	#region Sample
	// precalculate blue noise texture interval and array offset, call this when user changes the animation length
	// must be called any time you want to change the time interval you are going to be using, only call this in universal mode
	public void PrecalculateLoop(uint[] frames) {
		if (IsFixed)
			return; // or throw if you want to avoid calling this when you shouldn't
		var newMod = FindInterval(frames, MaxL);
		if (FrameMod == newMod && UsingBlue.Length > 0)
			return;
		// Where the "texIndex=BlueFrameMod%BlueJumpSize2" starts + Add where our interval is starting to the offset (I hope I calculated this line right):
		BlueOffset = BlueOffsets[FrameMod = newMod];
		UsingBlue = Blue[newMod % JumpSize2];
	}
	// the safest way to sample, loops every coordinate, and used whatever mode was initialized. As long as you have properly initialized a fixed or universal mode, it should work:
	public abstract float SampleSafe(S coord);
	// only use this if your spatial coordinates aren't out of Bounds bounds (Coord[i] = Coord[i] % Bounds[i]):
	public abstract float Sample(S coord);
	// only use this if you are sure you have correctly calculated the serialized index of your noise:
	// example serialized index for BlueNoiseSampler4(coords=(X,Y,Z,T)): (X % Bounds.X) + ((Y % Bounds.Y) + ((Z % Bounds.Z) + T % FrameMod * Bounds.Z) * Bounds.Y) * Bounds.X;
	public unsafe float SampleUniversal(uint serialized) => sampleFunc(this, serialized);
	// only use this if you are sure you have initialized an universal mode: "new(path).PrecalculateLoop(frames);", and you have correctly calculated the serialized index of your noise:
	public float DirectSampleSerialized_Universal(uint serialized) => UsingBlue[BlueOffset + serialized] + BlueWhite[serialized];
	// only use this if you are sure you have initialized a fixed mode: "new(path, frames);", and you have correctly calculated the serialized index of your noise:
	public float DirectSampleSerialized_Fixed(uint serialized) => UsingBlue[serialized] + BlueWhite[serialized];

	private static float SampleSerialized_Universal(BlueNoiseSampler<S> sampler, uint serialized) => sampler.DirectSampleSerialized_Universal(serialized);
	private static float SampleSerialized_Fixed(BlueNoiseSampler<S> sampler, uint serialized) => sampler.DirectSampleSerialized_Fixed(serialized);
	#endregion

	// PRIVATE:

	#region Private Variables
	unsafe protected delegate*<BlueNoiseSampler<S>, uint, float> sampleFunc = &SampleSerialized_Universal;
	protected (ushort MinL, ushort MaxL, ushort JumpSize, ushort JumpSize2, ushort FrameMod, ushort FixedFrames)[] lVal;
	protected uint[] BlueOffset; // selected offsets for each linked dimension
	protected uint[][] BlueOffsets; // BlueOffset[linkedDim][FrameMod[linkedDim]]
	private byte[] UsingBlue = []; // which chunk is currently is use?
	private readonly byte[][] Blue = []; // loaded blue texture as a 1D array
	private readonly float[] BlueWhite = []; // precomputed white noise to enhance the noise precision depth
	private readonly uint FrameSize; // spatial volume size
	private readonly byte linked; // how many linked dimensions are there?
	#endregion


	#region Command Parse
	protected static string PF(string name, string parse)
		=> "Failed to parse " + name + ", string = " + parse + ". Must be an integer!";
	protected static string[] TryPath(string path, int expected, out ushort jumpSize, out ushort tempSize) {
		var fileExt = Path.GetFileName(path).Split('.');
		var x = fileExt.Length != 2 || fileExt[1] != "blue" ? throw new(fileExt + " is not a *.blue file!") : fileExt[0].Split('x');
		if (x.Length != expected + 1 || x[0] != "B")
			throw new("Invalid file name. Expected Bx<" + expected + "xBounds" + ">.blue");
		string parse = x[expected - 2];
		return ushort.TryParse(parse, out jumpSize)
			? ushort.TryParse(parse = x[expected - 1], out tempSize)
			? x
			: throw new(PF("tempSize", parse))
			: throw new(PF("jumpSize", parse));
	}
	protected static void Parse(string parse, out ushort value, string name) { if (!ushort.TryParse(parse, out value)) throw new(PF(name, parse)); }
	protected abstract uint ParsePath(string[] dims, out S size);
	#endregion

	#region Init
	private void Load(string path, out byte[][] blue, out float[] white, out uint[] offsets, ushort[] fixedLength) {
		uint maxSize;
		using var fs = File.OpenRead(path);
		if (JumpSize <= 0) {
			// no extra intervals, only a single torus, so simply load the whole file into the single chunk it is
			if (fixedLength > 0 && fixedLength % MaxL != 0)
				throw new("cannot load an interval that dones't have the temporal size as a divisor. This texture only supports a single looping interval of its own temporal size.");
			UsingBlue = new byte[maxSize = FrameSize * (FrameMod = MaxL)];
			fs.ReadExactly(UsingBlue, 0, (int)maxSize);
			blue = [];
			offsets = [];
		} else {
			ushort tex0 = (ushort)(MaxL % JumpSize2);
			int chunkIndex = 0;
			int InterChunk() => (tex0 + JumpSize2 - chunkIndex) % JumpSize2;
			var (chunks, shifts) = BlueNoiseOptimizer.BestShifts(MaxL, JumpSize);
			void ProcessChunks(ref byte[][] blue, Action<int, ushort, byte[][]> act) {
				for (ushort i = 0; i < JumpSize2; ++i) {
					chunkIndex = chunks.IndexOf(i);
					if (chunkIndex < 0)
						act(i, (ushort)(MaxL - i), blue);// not alraedy interlaced inside previous chunk
				}
			}
			if (fixedLength == 0) {
				// Load the who texture and save all the chunks, let PrecalculateLoop(frames) to switch between intervals later:
				(maxSize, blue) = (FrameSize * MaxL, new byte[JumpSize2][]);
				ProcessChunks(ref blue, (i, tempSize, blue) => {
					var chunkSize = tempSize * FrameSize;
					// we will always load the chunk when universal
					byte[] chunkBlue = blue[(tex0 + JumpSize2 - i) % JumpSize2] = new byte[chunkSize];
					fs.ReadExactly(chunkBlue, 0, (int)chunkSize);
					if (i < chunks.Count) {
						// there is another chunk interlaced in this one, so copy the correct intervals:
						(int topFrames, int bottomStart, int bottomFrames) = BlueNoiseOptimizer.GetInvertedIntervals(shifts[chunkIndex], JumpSize, tempSize, tempSize, (_, _) => { });
						int targetFrames = MaxL - chunks[chunkIndex], interTempSize = topFrames + bottomFrames;
						if (targetFrames != interTempSize)
							throw new("interlaced chunk does not match the supposed number of frames! TargetSum=" + targetFrames + ", Top=" + topFrames + ", Bottom:" + bottomFrames);
						byte[] interBlue = blue[InterChunk()] = new byte[interTempSize * FrameSize];
						Buffer.BlockCopy(chunkBlue, bottomStart * (int)FrameSize, interBlue, 0, bottomFrames * (int)FrameSize); // copy the bottom half of the source to the top half of the destination
						Buffer.BlockCopy(chunkBlue, 0, interBlue, bottomFrames * (int)FrameSize, topFrames * (int)FrameSize); // copy the top half of the source to the bottom half of the destination
					}
				});
				// precalculate offsets where each iterval starts for every chunk:
				offsets = new uint[MaxL];
				for (int b = JumpSize; b < MaxL; ++b)
					offsets[b] = (uint)(MaxL - b) / JumpSize2 * JumpSize;
			} else {
				// only find and load a single interval in the file:
				blue = [];
				UsingBlue = new byte[maxSize = FrameSize * fixedLength];
				// Where the "textIndex=BlueFrameMod%BlueJumpSize2" starts + Add where our interval is starting to the offset (I hope I calculated this line right):
				FrameMod = FindInterval(fixedLength, MaxL);
				ProcessChunks(ref blue, (i, tempSize, blue) => {
					uint chunkSize = tempSize * FrameSize;
					byte[] chunkBlue;
					if (i < chunks.Count) {
						// there is another chunk interlaced in this one, so copy the correct intervals:
						(int topFrames, int bottomStart, int bottomFrames) = BlueNoiseOptimizer.GetInvertedIntervals(shifts[chunkIndex], JumpSize, tempSize, FrameMod, (_, _) => { });
						if (InterChunk() == FrameMod % JumpSize2) {
							// this chunk contans the interlaced interval of our desired size FrameMod:
							// since we are now sure the frames are in this chunk - load it
							fs.ReadExactly(chunkBlue = new byte[chunkSize], 0, (int)chunkSize);
							// read both halves of our interval into UsingBlue
							Buffer.BlockCopy(chunkBlue, bottomStart * (int)FrameSize, UsingBlue, 0, bottomFrames * (int)FrameSize); // copy the bottom half of the source to the top half of the destination
							Buffer.BlockCopy(chunkBlue, 0, UsingBlue, bottomFrames * (int)FrameSize, topFrames * (int)FrameSize); // copy the top half of the source to the bottom half of the destination
							return; // we have found and loaded the chunk with our desired interval, no need to continue searching
						}
					}
					if ((tex0 + JumpSize2 - i) % JumpSize2 == FrameMod % JumpSize2) {
						// this chunk contains the middle (or whole chunk "l == 0") interval of our desired size FrameMod:
						// since we are now sure the frames are in this chunk - load it
						fs.ReadExactly(chunkBlue = new byte[chunkSize], 0, (int)chunkSize);
						for (int l = (tempSize - JumpSize) / JumpSize2; 0 <= l; --l) {
							int interval = tempSize - JumpSize2 * l;
							if (FrameMod == interval) {
								// found that interval: read it into UsingBlue
								Buffer.BlockCopy(chunkBlue, JumpSize * l * (int)FrameSize, UsingBlue, 0, interval * (int)FrameSize);
								return; // we have found and loaded the chunk with our desired interval, no need to continue searching
							}
						}
					}
					// our desired chunk is not this one, so skip to the next one without reading it:
					fs.Seek(chunkSize, SeekOrigin.Current);
				});
				// no offsets in the fixed mode, there is only one interval and it is BlockCopied without an offset
				offsets = [];
			}
		}
		// generate white noise to add random frac precision intervals:
		white = new float[maxSize];
		for (ushort i = 0; i < maxSize; ++i) {
			ulong z = i;
			z += 0x9E3779B97F4A7C15UL;
			z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
			z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
			z ^= z >> 31;
			white[i] = BitConverter.UInt32BitsToSingle((uint)(z >> 41) | 0x3F800000) - 1.0f;
		}
	}
	private ushort FindInterval(uint frames, ushort MaxT) { // temporal loop interval
		for (ushort i = MaxT; i >= MinL; --i)
			if (frames % i == 0)
				return i;
		throw new("No compatible temporal blue-noise period found. The animation length must have a divisor between 4 and " + MaxT + ". Consider changing the animation duration slightly");
	}
	#endregion
}*/


/* TODO test if "tempSize = (ushort)(BlueTempSize / BlueJumpSize2 * BlueJumpSize2)" is equivalent to this
int tempSize = -1;
for (int i = BlueTempSize; i > BlueTempSize - BlueJumpSize2; ++i) {
	if (i - (i - BlueJumpSize) / BlueJumpSize2 * BlueJumpSize2 == BlueJumpSize2)
		tempSize = i; // find which texture should be at index 0
}*/
// Skip dynamic tempSizes to get us to the index where our TempSize chunk starts:
/*var tempSize = (ushort)(TempSize / JumpSize2 * JumpSize2);
BlueOffsets = new uint[JumpSize2];
BlueOffsets[0] = 0; // first tex doesn't have an offset
for (int i = 0; ++i < JumpSize2; tempSize = (ushort)(tempSize >= TempSize ? tempSize + 1 - JumpSize2 : tempSize + 1))
	BlueOffsets[i] = (o += tempSize) * s; // tempSize*BlueWidth*BlueHeight voxels each chunk
*/