namespace BlueNoise;
internal enum TaskState : byte {
	Free = 0,       // The task has not been started yet, or already finished and joined and ready to be started again
	Done = 1,       // The task if finished and ready to join without waiting
	Running = 2     // The task is running
}