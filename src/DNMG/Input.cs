namespace DNMG;

// SEE: https://gbdev.io/pandocs/Joypad_Input.html
public sealed class Input
{
	[Flags]
	public enum JoypadButtons // lower nibble is dpad, upper nibble is action buttons
	{
		None,
		RightArrow = 1 << 0,
		LeftArrow = 1 << 1,
		UpArrow = 1 << 2,
		DownArrow = 1 << 3,
		A = 1 << 4,
		B = 1 << 5,
		Select = 1 << 6,
		Start = 1 << 7
	}

	private readonly Cpu _cpu;

	private byte JoyPadRegister { get => _cpu.Memory[0xFF00]; set => _cpu.Memory[0xFF00] = value; }

	public JoypadButtons PressedButtons { get; set { field = value; UpdateJoyPadRegister(JoyPadRegister); } }

	public Input(Cpu cpu)
	{
		_cpu = cpu;
		cpu.OnMemoryWrite[0x00] = UpdateJoyPadRegister;
		UpdateJoyPadRegister(0);
	}

	private void UpdateJoyPadRegister(byte value)
	{
		var pressedButtons = (int)PressedButtons;
		var buttonMask = 0; // default for no buttons pressed
		if ((value & (1 << 4)) == 0) // dpad buttons selected?
			buttonMask = pressedButtons;
		if ((value & (1 << 5)) == 0) // action buttons selected?
			buttonMask |= pressedButtons >> 4;
		// buttons are low active!
		buttonMask = ~buttonMask & 0xF;
		// request joypad interrupt if no buttons were pressed before and now at least one is
		if ((JoyPadRegister & 0xF) == 0xF && buttonMask != 0xF)
			_cpu.RequestInterrupt(Cpu.IFBits.Joypad);
		// upper 2 bits are always 1, next 2 bits select button group (writable by the program), lower 4 bits are button states
		JoyPadRegister = (byte)(0b1100_0000 | (value & 0x30) | buttonMask);
	}
}
