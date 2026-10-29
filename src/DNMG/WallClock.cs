using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace DNMG;

public sealed partial class WallClock
{
	[SupportedOSPlatform("windows")]
	internal static partial class Winmm
	{
		[LibraryImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
		public static partial uint TimeBeginPeriod(uint uPeriod);
	}

	public const int MasterClockHz = 4_194_304;
	public const int CyclesPerSecond = MasterClockHz / 4;
	public const int CyclesPerFrame = 70_224 / 4;

	private const int MaxLagMs = -100;

	private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
	private long _emulatedCycles;
	private int _frameCycles;

	static WallClock()
	{
		if (OperatingSystem.IsWindows())
			Winmm.TimeBeginPeriod(1);
	}

	public void Wait(int cycles)
	{
		_emulatedCycles += cycles;
		_frameCycles += cycles;
		if (_frameCycles < CyclesPerFrame)
			return;

		_frameCycles -= CyclesPerFrame;

		var ahead = TimeSpan.FromSeconds(_emulatedCycles / (double)CyclesPerSecond) - _stopwatch.Elapsed;
		if (ahead > TimeSpan.Zero)
		{
			Thread.Sleep(ahead);
		}
		else if (ahead < TimeSpan.FromMilliseconds(MaxLagMs))
		{
			Console.WriteLine("Wallclock delayed over 100ms. Resetting...");
			_stopwatch.Restart();
			_emulatedCycles = 0;
		}
	}
}
