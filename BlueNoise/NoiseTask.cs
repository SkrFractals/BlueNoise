/*using System.Numerics;
using System.Runtime.CompilerServices;

namespace BlueNoise;
internal class NoiseTask {
	// evey first SingleDots call should set this (typically this.Buffer, but for MultiBuffer OfDepth, it can be redirected to generator.buffer[taskIndex]):
	internal Vector3[] UseBuffer = [];  // Reference which buffer to apply dots to

	private Task
#if NULLABLE
		?
#endif
		task;							// Parallel Animation Tasks
	internal TaskState State;           // States of Animation Tasks
	internal LoopMap Map;

	internal readonly Queue<uint> Locations = [];
	internal readonly List<uint> Unplaced = [];
	internal readonly List<uint> Placed = [];
	internal readonly List<Vector3> Vectors = [];
	public NoiseTask(LoopMap map) {
		Map = map;
	}

	internal void FindKernel(uint from) {
		Vectors.Clear();
		Placed.Clear();
		Unplaced.Clear();
		Locations.Clear();
		AddLocation(from, Vector3.Zero);
	
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void AddLocation(uint from, Vector3 v) {
		Locations.Enqueue(from);
		Vectors.Add(v);
		(Map.Values[from] == 0 ? Unplaced : Placed).Add(from);

	}

	
	
	
	
	internal byte[] Buffer = [];        // Buffer for points to print into bmp
	internal float[] Kernel = [];
	internal int[] VoidDepth = [];      // Depths of Void
	internal int[] VoidDepthN = [];     // Depths of Void
	internal Vector3[]
#if NULLABLE
		?
#endif
		VoidNoise = null;   // Randomized noise samples for the void noise
	internal Float3[]
#if NULLABLE
		?
#endif
		VoidNoiseF = null;  // Randomized noise samples for the void nois
	internal int[] VoidQueue = [];      // Void depth calculating Dijkstra queue
	internal Dictionary<long, Vector3[]>
		ColorBlends = [];               // ???
	internal readonly Dictionary<long, Vector3[]>
		FinalColors = [];               // Mixed children color
	internal int BitmapIndex;           // The bitmapIndex of which the task is working on
	internal short TaskIndex,           // The task index
		ApplyWidth, ApplyHeight,        // can be smaller for previews
		WidthBorder, HeightBorder;      // slightly below the width and height
	internal float Bloom0, Bloom1;      // = selectBloom (0,+1);
	internal double
	RightEnd, DownEnd,                  // slightly beyond width and depth, to ensure even bloomed pixels don't cut off too early
	ApplyDetail,                        // allocated detail
	UpLeftStart;                        // = -selectBloom;
	internal float LightNormalizer,     // maximum brightness found in the buffer, for normalizing the final image brightness
		VoidDepthMax;                   // maximum reached void depth during the dijkstra search, for normalizing the void intensity
	internal (double, double, (double, double)[])[]
		PreIterate = [];                // (childSize, childDetail, childSpread, (childX,childY)[])


	internal ushort StripeHeight, StripeCount;
	internal uint PredictedBinsPerStripe;
	private List<(long, float, float, byte)[]>[]
#if NULLABLE
		?
#endif
		bin = null;
	private ushort[] filledBins = [];   // Which bidId is being filled in this stripeId?
	private uint[] filledStripes = [];  // How many dots are in the latest bin of this stripeId?
	internal uint BinSize;

	internal FractalTask() { }
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal bool IsStillRunning() {
		return State == TaskState.Done ? Join() : State != TaskState.Free;
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal bool Join() {
		Stop(); // Stop the thread
		State = TaskState.Free; // Mark it as free to be started again
		return false;//taskStarted = false; // (used this to be doubly sure when I had bugs in the control, but probably redundant at this point)
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void Start(int bitmap, Action action) {
		Stop();
		//taskStarted = true;
		BitmapIndex = bitmap;
		State = TaskState.Running;
		task = Task.Run(action);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void Stop() {
		if (task == null)
			return;
		task.Wait();
		task = null;
	}
}*/