namespace DNMG.App.Console;

static internal class Program
{
	static void Main(string[] args)
	{
		var romFilename = args.Length > 0 ? args[0] : Path.GetFullPath("../../../../../demo.gb");
		var rom = File.ReadAllBytes(romFilename);

		var cpu = new Cpu(rom);
		var totalCycles = 0;
		while (true)
		{
			var cycles = cpu.ExecuteSingleStep();
			totalCycles += cycles;
			const int dotPerFrame = 70_224;
			const int cyclesPerFrame = dotPerFrame / 4;
			if (totalCycles >= cyclesPerFrame)
			{
				totalCycles -= cyclesPerFrame;
				// TODO: output frame buffer to console
			}
		}
	}
}
