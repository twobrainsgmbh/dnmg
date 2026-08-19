namespace DNMG;

// SEE: https://gbdev.io/pandocs/Timer_and_Divider_Registers.html
public sealed class Timer
{
	private readonly Cpu _cpu;
	private ushort _cycleCounter;

	private byte DIV { get => _cpu.Memory[0xFF04]; set => _cpu.Memory[0xFF04] = value; } // Divider Register
	private byte TIMA { get => _cpu.Memory[0xFF05]; set => _cpu.Memory[0xFF05] = value; } // Timer Counter
	private byte TMA => _cpu.Memory[0xFF06]; // Timer Modulo
	private byte TAC => _cpu.Memory[0xFF07]; // Timer Control

	public Timer(Cpu cpu)
	{
		_cpu = cpu;
		_cpu.OnMemoryWrite[0x04] = _ => { _cycleCounter = 0; DIV = 0; }; // DIV
		_cpu.OnMemoryWrite[0x07] = value => _cpu.Memory[0xFF07] = (byte)(value | 0b1111_1000); // TAC
	}

	public void ExecuteSingleStep(int cycleDelta)
	{
		// check if TIMA increments are enabled
		var tac = TAC;
		if ((tac & 0b100) != 0)
		{
			// determine the divider based on the TAC input clock select bits
			tac &= 0b11;
			var dividerBit = 1 << (tac == 0 ? 8 : (tac * 2));

			var counter = _cycleCounter / 4;
			for (var i = 0; i < cycleDelta; i++)
			{
				if ((counter & dividerBit) != (++counter & dividerBit) && ++TIMA == 0) // increment TIMA for each overflow of the selected divider
				{
					TIMA = TMA;
					_cpu.RequestInterrupt(Cpu.IFBits.Timer);
				}
			}
		}

		_cycleCounter += (ushort)(cycleDelta * 4); // the timer runs at 4 MHz (T-Cycles, not M-Cycles)
		DIV = (byte)(_cycleCounter >> 8);
	}
}
