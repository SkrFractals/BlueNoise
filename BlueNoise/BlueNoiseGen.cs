namespace BlueNoise;
public class Candidate<V> {
	public float rating;
	public ushort size;
	public uint coord;
	public Dictionary<uint, (V dir, int distSqr)> q = [];
	public List<(uint coord, int distSqr)> unplaced = [], placed = [];
}
internal class BlueNoiseGen {
	private readonly static Random random = new();
	/*internal static ushort[] TempSizesP = [
		89, 113, 139, 181, 199, 211, 241, 283, 293, 317, 337, 409, 421, 467, 509, 523, 547, 577, 619, 631, 661, 691, 709, 773, 839, 863, 887, 1069, 1129
	]; // all best prime choices that have a large following gap (after the first 89 at least 10)
	internal static ushort[] TempSizes3 = [
		93, 117, 141, 183, 201, 213, 243, 285, 297, 321, 339, 411, 423, 471, 513, 525
	]; // all best prime choices that have a large following gap (after the first 89 at least 10) - optimized for JumpSize=3
	*/

	#region Rating
	// get the ratings for every maxT value up to maxT, considering the multiplied and tossed chunks, and the prime ratings
	internal static float[] RatingStorages(short maxLength, short jumpSize) {
		if (jumpSize <= 0)
			return new float[maxLength];
		var r = RatingStorage_PrimeGaps(maxLength); // only run the sieve once and return all its rating values
		for (int i = r.Length; 0 <= --i; r[i] *= RatingStorage_InterlacingToss(maxLength, jumpSize)) { }; // multiply every prime rating with the interlacing rating
		return r;
	}
	internal static float RatingStorage_InterlacingToss(short maxLength, short jumpSize)
		=> jumpSize <= 0 ? 1.0f : 1.0f / ((jumpSize << 1) - BlueNoiseOptimizer.Best(maxLength, jumpSize).chunks.Count);
	internal static float[] RatingStorage_PrimeGaps(short maxLength) {
		int eL = maxLength + 210, // capped at prime gap 210, no short value for length has a bigger one.
			primes = 0, previousPrime = 0, currentPrime = 1; // get bent, math, lol!
		var e = new bool[eL];
		var r = new float[eL];
		void Rate() {
			float rating = currentPrime + primes;//(currentPrime << 1) - previousPrime + primes;
			for (int ir = previousPrime; ir < currentPrime; ++ir)
				r[ir] = rating / ir;
		}
		while (e[++currentPrime] == false && previousPrime < maxLength)
			if (e[currentPrime] == false) {
				var p = previousPrime < maxLength;
				Rate();
				previousPrime = currentPrime;
				++primes;
				if (p)
					break;
				e[currentPrime] = true;
				for (uint s, m = (uint)currentPrime; (s = m * (uint)currentPrime) < eL; ++m)
					e[s] = true;
			}
		Rate();
		return r;
	}
	// returns how many intervals does each maxT value contain, up to upToMaxT. Divided by maxT to get the efficiency ratio. 
	internal static float[] RatingStorage_PrimeGaps_Counting(short upToMaxLength, ushort jumpSize, uint sieveSize = 1000000) {
		uint eL = Math.Max(sieveSize, (uint)(upToMaxLength * upToMaxLength)), // capped at prime gap 210, no short value for length has a bigger one.
			previousPrime = 0, currentPrime = jumpSize; // will have to start thesieve from jumpSize, because divisiors below that are not in the interval set!
		uint rating = 0;
		var r = new float[upToMaxLength];
		void Rate() {
			for (uint ir = previousPrime; ir < currentPrime; ++ir)
				r[ir] = (float)rating / ir;
		}
		for (var e = new bool[eL]; currentPrime < upToMaxLength; ++currentPrime)
			if (e[currentPrime] == false) {
				// rate the interval up to this found prime
				Rate();
				uint s = previousPrime = currentPrime;
				void Sieve() {
					if (!e[s]) {
						e[s] = true;
						++rating;
					}
				}
				// the actual found prime:
				Sieve();
				// composites from 2 to jumpSize (as those were no longer accounted for with the missing 2..jumpSize-1 sieved primes)
				for (uint m = 2; m < jumpSize && (s = m * currentPrime) < eL; ++m)
					Sieve();
				// composites from p*p to the end of array
				for (uint m = currentPrime; (s = m * currentPrime) < eL; ++m)
					Sieve();
			}
		// rate the last unrated interval from the last prime
		Rate();
		return r;
	}

	#endregion

	#region Generate Wrappers
	// tempSize = jumpSize..maxT; filename = Bx<J>x<L>.blue
	private static bool GenerateBlueFile1(byte jumpSize, short maxLength, float diff, out string fail) 
		=> Test(jumpSize, 0, 16, "jump size", out fail) 
			|| Generate(1, "B", 1, jumpSize, maxLength, diff, (length) => new LoopMap1(length, 2), out fail);
	// W = 2^widthExp, tempSize = jumpSize..maxT; filename = Bx<W>x<J>x<L>.blue
	private static bool GenerateBlueFile2(byte widthExp, byte jumpSize, short maxLength, float diff, out string fail) {
		short w = (short)(1 << widthExp);
		return Test(widthExp, "width", out fail) 
			|| Test(jumpSize, out fail) 
			|| Generate(2, "Bx" + widthExp, (uint)w, jumpSize, maxLength, diff, 
				(length) => new LoopMap2((w, length), 2), out fail);
	}

	// WxH = 2^widthExp x 2^heightExp, tempSize = jumpSize..maxT; filename = Bx<W>x<H>x<J>x<L>.blue
	private static bool GenerateBlueFile3(byte widthExp, byte heightExp, byte jumpSize, short maxLength, float diff, out string fail) {
		short w = (short)(1 << widthExp), h = (short)(1 << heightExp);
		return Test(widthExp, "width", out fail) 
			|| Test(heightExp, "height", out fail) 
			|| Test(jumpSize, out fail) 
			|| Generate(3, "Bx" + widthExp + "x" + heightExp, (uint)w * (uint)h, jumpSize, maxLength, diff,
				(length) => new LoopMap3((w,h,length), 3), out fail);
	}

	// WxHxD = 2^widthExp x 2^heightExp x 2^depthExp, length = jumpSize..maxT; filename = Bx<W>x<H>x<D>x<J>x<L>.blue
	private static bool GenerateBlueFile4(byte widthExp, byte heightExp, byte depthExp, byte jumpSize, short maxLength, float diff, out string fail) {
		short w = (short)(1 << widthExp), h = (short)(1 << heightExp), d = (short)(1 << depthExp);
		return Test(widthExp, "width", out fail)
			|| Test(heightExp, "height", out fail) 
			|| Test(depthExp, "height", out fail)
			|| Test(jumpSize, out fail) 
			|| Generate(4,"Bx" + widthExp + "x" + heightExp + "x" + depthExp, (uint)w * (uint)h * (uint)d, jumpSize, maxLength, diff,
				(length) => new LoopMap4((w,h,d,length), 2), out fail);
	}
	private static readonly string[] DimNames = ["width", "height", "depth"];
	private static bool GenerateBlueFileX(byte[] dimExp, byte jumpSize, short maxLength, float diff, out string fail) {
		if (Test(jumpSize, out fail))
			return true;
		byte totalDims = (byte)(dimExp.Length + 1);
		short[] S = new short[totalDims];
		string filePrefix = "B";
		fail = "";
		uint spatialSize = 1;
		for (int i = 0; i < dimExp.Length; fail = Test(dimExp[i], i < DimNames.Length ? DimNames[i++] : "dim" + ++i, out var e) ? fail + "\n" + e : fail) {
			filePrefix += "x" + dimExp[i];
			spatialSize *= (uint)(S[i] = (short)(1 << dimExp[i]));
		}
		S[dimExp.Length] = maxLength;
		if (fail != "") {
			fail = "failed these dimensions:" + fail; 
			return true;
		}
		return Generate(totalDims, filePrefix, spatialSize, jumpSize, maxLength, diff, (length) => new LoopMapX(S, (byte)dimExp.Length, 2), out fail);
	}
	#endregion

	#region Generate Core
	private static bool Test(byte test, out string fail) => Test(test, 0, 16, "jumpSize", out fail);
	private static bool Test(byte test, string name, out string fail) => Test(test, 2, 15, name, out fail);
	private static bool Test(byte test, byte min, byte max, string name, out string fail) {
		if (test < min) {
			fail = "way too small " + name + " - must be 2 min";
			return true;
		}
		if (test > max) {
			fail = "way too large " + name + " - must be 15 max";
			return true;
		}
		fail = "";
		return false;
	}
	private static bool Test(short maxLength, byte jump2, uint size, out string fail) {
		fail = "texture would have more than int.MaxValue bytes!";
		return ((float)maxLength) * jump2 * size >= int.MaxValue;
	}
	public enum GenerateActions : byte {
		Nothing = 0,
		GenerateDebug = 1,
		GenerateValues = 2,
		WriteFile = 4
	}
	private static bool Generate<V>(byte dims, string dimsName, uint frameSize, byte jumpSize, short maxLength, float desiredDifference,
		Func<short, LoopMap<V>> New, 
		out string fail, 
		byte actions = (byte)(GenerateActions.GenerateValues | GenerateActions.WriteFile)) {

		byte jumpSize2 = (byte)(jumpSize <= 0 ? 1 : jumpSize << 1);
		if (maxLength - jumpSize2 + 1 - jumpSize < 0) {
			fail = "the ratio of (TempSize / JumpSize) is too small! The last chunks couldn't fit a single interval!";
			return true;
		}
		// tests if the total size of the loaded texture could fit 32bit addresses
		if(Test(maxLength, jumpSize2, frameSize, out fail))
			return true;

		var (chunks, shifts) = BlueNoiseOptimizer.Best(maxLength, jumpSize);

		// count totalFrames:
		uint totalFrames = 0;
		for (ushort i = 0; i < jumpSize2; ++i) {
			int chunkIndex = chunks.IndexOf(i);
			if (chunkIndex >= 0)
				continue; // toss the chunks that are interlaced in another
			totalFrames += (uint)(maxLength - i);
		}
		// prepare the byte buffer for writing:
		byte[] bytes = new byte[totalFrames * frameSize];
		uint byteIndex = 0;

		List<int> intervals = []; // DEBUG count used intervals
		for (ushort i = 0; i < jumpSize2; ++i) {
			int chunkIndex = chunks.IndexOf(i);
			if (chunkIndex >= 0)
				continue; // toss the chunks that are interlaced in another
			short lengthSize = (short)(maxLength - i);
			// Noie map with spatial looping
			var m = New(lengthSize);
			m.DesiredDifference = desiredDifference;
			// only make loop links if jumpSize is greater than 0
			if (jumpSize > 0) {
				var minLength = Math.Max((ushort)2, jumpSize);
				// find all middle intervals: temporal loop links for intervals: 0+jumpSize*i <-> tempSize-jumpSize*i, where i = <0,loops(inclusive)>
				for (int l = (lengthSize - minLength) / jumpSize2; 0 < l; --l) {
					m.AddLink(((uint)(jumpSize * l), (uint)(lengthSize - jumpSize * l - 1)));
					intervals.Add(lengthSize - jumpSize2 * l); // DEBUG count used intervals
				}
				// there will be another chunk interlaced in this one, add its inverse links:
				if (i < chunks.Count) {
					(int topFrames, int bottomStart, int bottomFrames) = BlueNoiseOptimizer.GetInterlace(shifts[chunkIndex], jumpSize, (ushort)lengthSize, (ushort)lengthSize, (topFrames, bottomStart) => {
						m.AddLink(((uint)bottomStart, (uint)(topFrames - 1)));
						intervals.Add(topFrames + lengthSize - bottomStart);
						//intervals.Add(tempSize - (tempSize - jumpSize2 * l - a - b) + 2); // DEBUG count used intervals
					});
				}
				// Verify that we have prepared the correct set of interlaced intervals:
				string DumpIntervals() {
					string f = "\n";
					foreach (var val in intervals)
						f += val + ", ";
					if (intervals.Count > 0)
						f = f[..^2];
					return f;
				}
				var expectedCount = 1 + maxLength - jumpSize;
				if (intervals.Count != expectedCount) {
					fail = "Incorrect count of intervals. Expected=" + expectedCount + ", Found=" + intervals.Count + DumpIntervals();
					return true;
				}
				intervals.Sort();
				for (int j = jumpSize; j <= maxLength; ++j) {
					var found = intervals[j - jumpSize];
					if (found != j) {
						fail = "Incorrect sizes of intervals. Target=" + jumpSize + "-" + maxLength + ", Expected=" + j + ", Found=" + found + DumpIntervals();
						return true;
					}
				}
			}
			if((actions & (byte)GenerateActions.GenerateValues) > 0)
				GenerateBlueValues(m, new (float, V, uint)[1 << (dims << 1)]);
			//if ((actions & (byte)GenerateActions.GenerateDebug) > 0)
			//	GenerateBlueDebugs(m, new (float, V, uint)[1 << (dims << 1)]);
			uint denom = (uint)m.Values.Length;
			for (int b = 0; b < denom; ++b)
				bytes[byteIndex++] = (byte)((ulong)m.Values[b] * 254 / denom); // TODO specify min and max value, or even the bytesize?
		}
		// Write the file
		if ((actions & (byte)GenerateActions.WriteFile) > 0) {
			using FileStream fs = new(dimsName + "x" + jumpSize + "x" + maxLength + ".blue",
				FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, (int)byteIndex);
			fs.Write(bytes, 0, (int)byteIndex);
			fs.Flush();
			fs.Close();
		}
		fail = "SUCCESS";
		return false;
	}
	#endregion

	public void GetKernel<V>(LoopMap<V> m, Candidate<V> from) {
		// initialize BFS:
		Queue<uint> q = new Queue<uint>();
		q.Enqueue(from.coord);
		from.q[from.coord] = (m.Zero, 0);
		// as long as we have more points to process:
		while (q.TryDequeue(out var dequeued)) {
			if (!from.q.TryGetValue(dequeued, out var sync))
				throw new("queue not in sync!");
			// add to placed or unplaced list of reachable neighbors in the kernel:
			(m.Values[dequeued] == 0 ? from.unplaced : from.placed).Add((dequeued, sync.distSqr));
			if (sync.distSqr > from.size * from.size)
				continue; // do not search further if far away enough
			// get coords of all direct neighbors of that voxel, along with the delta direction to them 
			var steps = m.GetSteps(dequeued, sync.dir);
			for(int s = 0; s < steps.Length; ++s) {
				// adds sync.dir to stepDir (in place) and get its distance squared
				var (stepDir, stepCoord) = steps[s];
				var stepDistSqr = m.SizeSqr(m.AddTo(sync.dir, ref stepDir));
				if (from.q.TryGetValue(stepCoord, out var memory)) {
					// this probably shouldn't be needed in my case, but might be nice to have it here in general:
					if (stepDistSqr < memory.distSqr) // shorter path? update direction+distance
						from.q[stepCoord] = (stepDir, stepDistSqr); 
				} else {
					from.q[stepCoord] = (stepDir, stepDistSqr); // set direction+distance
					q.Enqueue(stepCoord); // put the found neighbor back into the queue to continue BFS
				}
			}
		}
	}

	private static void GenerateBlueValues<V>(LoopMap<V> map, Candidate<V>[] candidates) { // candidates[1<<(dims<<1)]
		uint H = map.Size >> 1;
		//V[] candidates = V[25];
		for (uint pointPairs = 0; pointPairs < H;) {
			void Place(uint color, byte countIndex) {
				var (voidestSize, voidestCoord) = map.GetVoidest(countIndex);
				(float rating, V coord, uint serialized) PickBest() {
					float r, best = 0; int bestP = 0;
					for (int t = 0; t < candidates.Length; ++t) {
						// TODO add a little bit of temperature
						if ((r = candidates[t].rating) > best)
							(best, bestP) = (r, t);
					}
					return candidates[bestP];
				}
				(float rating, V coord, uint serialized) GetBest((ushort size, V coord) point) {
					if (point.size <= 16) {
						map.GetRatedGrid(candidates, point, color); // return best 1<<(dims<<1) candidates found over every pixel on the remaining grid(+-size/2)
						return PickBest();
					}
					point.size /= 4; // quarter the size recursively
					for (int t = 0; t < candidates.Length; ++t)
						candidates[t] = map.Rate(map.GetRandom(point), point.size, color); // try 4^dim random points, Rate rates them over "size" kernel 
					return GetBest((point.size, PickBest().coord)); // recurse deeper
				}
				// gets 3x3x3x3...cube (size/8,size3/8,size5/8,size7/8) TODO makde leaf size 32
				// TODO GetBestCube: size *= Math.Floor((size << 1) * Math.Sqrt(map.dims / 4)); call Rate on all of them
				var (_, bestCoord, serialized) = GetBest((voidestSize, PickBest(map.GetRatedCube(candidates, (voidestSize, voidestCoord)))));
				// increment octTree coutner
				map.Place(bestCoord, countIndex); // TODO count up light or dark!
												  // place the color
				map.Values[serialized] = color;
			}
			Place(H + pointPairs, 0); // place gray->light
			Place(H - ++pointPairs, 1); // place gray->black
		}
	}
	#endregion
}

/*#region Rating_SingleValue
internal static float RatingStorage(short maxT, short jumpSize) 
	=> RatingStorage_InterlacingToss(maxT, jumpSize) * RatingStorage_PrimeGap(maxT);
internal static float RatingStorage_PrimeGap(short maxT) {
	uint eL = (uint)maxT + 210; // capped at prime gap 210, no short value for length has a bigger one.
	bool[] e = new bool[eL];
	int primes = 0, previousPrime = 1, currentPrime = 1; // get bent, math, lol!
	while (e[++currentPrime] == false && previousPrime < maxT)
		if (e[currentPrime] == false) {
			previousPrime = currentPrime;
			++primes;
			e[currentPrime] = true;
			for (uint s, m = (uint)currentPrime; (s = m * (uint)currentPrime) < eL; ++m)
				e[s] = true;
		}
	return (currentPrime + primes) / maxT;//((currentPrime << 1) - previousPrime + primes) / maxT;
}
#endregion*/