namespace DNMG;

public sealed class Input
{
	[Flags]
	public enum JoypadKeys // lower nibble is dpad, upper nibble is action buttons
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

	public JoypadKeys PressedKeys { get; set { field = value; UpdateJoyPadRegister(JoyPadRegister); } }

	public Input(Cpu cpu)
	{
		_cpu = cpu;
		cpu.OnMemoryWrite[0x00] = UpdateJoyPadRegister;
	}

	private void UpdateJoyPadRegister(byte value)
	{
		// only bits 4 and 5 (button group select) are writable by the program
		value &= 0x30;
		// upper 2 bits are always 1, next 2 bits select button group, lower 4 bits are button states (low active)
		JoyPadRegister = (byte)(0xC0 | value | value switch
		{
			0x10 => (~(int)PressedKeys >> 4) & 0xF, // action buttons
			0x20 => (~(int)PressedKeys) & 0xF, // dpad buttons
			_ => 0xF // neither selected
		});
	}
}
