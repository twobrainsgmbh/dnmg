namespace DNMG.Audio;

internal sealed class NoiseChannel
{
	private const int MaxLength = 64;
	private const int LfsrWidth = 15, ShortLfsrWidth = 7;

	private static ReadOnlySpan<byte> Divisors => [8, 16, 32, 48, 64, 80, 96, 112];

	private readonly LengthCounter _length = new();
	private readonly Envelope _envelope = new();

	private byte _envelopeSettings;
	private byte _randomness;
	private bool _isLengthEnabled;

	private int _timer;
	private int _lfsr;

	private int _sample;

	private bool IsShortMode => (_randomness & 0b1000) != 0;
	private int Period => Divisors[_randomness & 0b111] << (_randomness >> 4);

	public bool IsEnabled { get; private set; }

	public bool IsDacEnabled => (_envelopeSettings & 0b1111_1000) != 0;

	public int Sample => IsEnabled ? _sample : 0;

	public void Write(int register, byte value)
	{
		switch (register)
		{
			case 1:
				_length.Load(value, MaxLength);
				break;
			case 2:
				_envelopeSettings = value;
				break;
			case 3:
				_randomness = value;
				break;
			case 4:
				_isLengthEnabled = (value & 0b0100_0000) != 0;
				if ((value & 0b1000_0000) != 0)
					Trigger();
				break;
		}
	}

	public void Step(int tCycles)
	{
		if (!IsDacEnabled)
			IsEnabled = false;

		_timer -= tCycles;
		while (_timer <= 0)
		{
			_timer += Period;
			ShiftRegister(IsShortMode);
		}

		var waveform = ~_lfsr & 1;
		_sample = _envelope.Apply(waveform);
	}

	public void Reset()
	{
		IsEnabled = false;
		_envelopeSettings = 0;
		_randomness = 0;
		_isLengthEnabled = false;
	}

	public void ClockLength()
	{
		if (_length.Clock(_isLengthEnabled))
			IsEnabled = false;
	}

	public void ClockEnvelope() => _envelope.Clock(_envelopeSettings);

	private void Trigger()
	{
		IsEnabled = IsDacEnabled;
		_length.Reset(MaxLength);
		_envelope.Reset(_envelopeSettings);

		_timer = Period;
		_lfsr = (1 << LfsrWidth) - 1;
	}

	private void ShiftRegister(bool isShortMode)
	{
		var newBit = (_lfsr & 1) ^ ((_lfsr >> 1) & 1);
		_lfsr >>= 1;
		_lfsr = SetBit(_lfsr, LfsrWidth - 1, newBit);

		// short mode folds the register in half, so it repeats soon enough to sound tonal rather than noisy
		if (isShortMode)
			_lfsr = SetBit(_lfsr, ShortLfsrWidth - 1, newBit);

		return;

		static int SetBit(int value, int index, int bit) => (value & ~(1 << index)) | (bit << index);
	}
}
