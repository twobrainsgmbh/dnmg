#if WINDOWS
using Windows.Win32.UI.Input.XboxController;
#endif

namespace DNMG.App.Console;

static internal class Program
{
	static void Main(string[] args)
	{
		var romFilename = args.Length > 0 ? args[0] : Path.GetFullPath("../../../../../demo.gb");
		var rom = File.ReadAllBytes(romFilename);

		var cpu = new Cpu(rom);
		var ppu = new Ppu(cpu);
		var timer = new Timer(cpu);
		var input = new Input(cpu);
		var totalCycles = 0;

		// Set default Joypad state to "No buttons pressed"
		input.PressedButtons = 0;

#if WINDOWS
		// calling XInputGetState comes with a cost we only want to pay if a controller is actually attached.
		bool useController = Windows.Win32.PInvoke.XInputGetState(0, out _) == 0;
#endif
		while (true)
		{
			var cycles = cpu.ExecuteSingleStep();
			timer.ExecuteSingleStep(cycles);
			ppu.ExecuteSingleStep(cycles);
			totalCycles += cycles;
			const int dotPerFrame = 70_224;
			const int cyclesPerFrame = dotPerFrame / 4;
			if (totalCycles >= cyclesPerFrame)
			{
				totalCycles -= cyclesPerFrame;
				// TODO: output frame buffer to console

#if WINDOWS
				if (useController)
				{
					// experimental XBOX controller support
					var keys = default(Input.JoypadButtons);
					Windows.Win32.PInvoke.XInputGetState(0, out var state);
					foreach (var vy in new ReadOnlySpan<ValueTuple<XINPUT_GAMEPAD_BUTTON_FLAGS, Input.JoypadButtons>>([
						(XINPUT_GAMEPAD_BUTTON_FLAGS.XINPUT_GAMEPAD_DPAD_LEFT, Input.JoypadButtons.LeftArrow),
						(XINPUT_GAMEPAD_BUTTON_FLAGS.XINPUT_GAMEPAD_DPAD_RIGHT, Input.JoypadButtons.RightArrow),
						(XINPUT_GAMEPAD_BUTTON_FLAGS.XINPUT_GAMEPAD_DPAD_UP, Input.JoypadButtons.UpArrow),
						(XINPUT_GAMEPAD_BUTTON_FLAGS.XINPUT_GAMEPAD_DPAD_DOWN, Input.JoypadButtons.DownArrow),
						(XINPUT_GAMEPAD_BUTTON_FLAGS.XINPUT_GAMEPAD_A, Input.JoypadButtons.A),
						(XINPUT_GAMEPAD_BUTTON_FLAGS.XINPUT_GAMEPAD_B, Input.JoypadButtons.B),
						(XINPUT_GAMEPAD_BUTTON_FLAGS.XINPUT_GAMEPAD_START, Input.JoypadButtons.Start)
					]))
					{
						if (state.Gamepad.wButtons.HasFlag(vy.Item1))
							keys |= vy.Item2;
					}
					input.PressedButtons = keys;
				}
#endif
			}
		}
	}
}
