namespace DNMG.Audio;

internal sealed class WaveChannel
{
	private const int MaxLength = 256;
	private const int SampleCount = 32;

	private static ReadOnlySpan<byte> VolumeShifts => [4, 0, 1, 2];

	private readonly byte[] _waveRam = new byte[SampleCount / 2];
	private readonly LengthCounter _length = new();

	private int _volumeShift = VolumeShifts[0];
	private int _periodValue;
	private bool _isLengthEnabled;

	private int _timer;
	private int _sampleIndex;
	private int _sampleBuffer;

	private int _sample;

	private int Period => (2048 - _periodValue) * 2;

	public bool IsEnabled { get; private set; }

	public bool IsDacEnabled { get; private set; }

	public int Sample => IsEnabled ? _sample : 0;

	public void Write(int register, byte value)
	{
		switch (register)
		{
			case 0:
				IsDacEnabled = (value & 0b1000_0000) != 0;
				break;
			case 1:
				_length.Load(value, MaxLength);
				break;
			case 2:
				_volumeShift = VolumeShifts[(value >> 5) & 0b11];
				break;
			case 3:
				_periodValue = (_periodValue & 0x700) | value;
				break;
			case 4:
				_periodValue = (_periodValue & 0xFF) | ((value & 0b111) << 8);
				_isLengthEnabled = (value & 0b0100_0000) != 0;
				if ((value & 0b1000_0000) != 0)
					Trigger();
				break;
		}
	}

	public void WriteWaveRam(int index, byte value) => _waveRam[index] = value;

	public void Step(int tCycles)
	{
		if (!IsDacEnabled)
			IsEnabled = false;

		_timer -= tCycles;
		while (_timer <= 0)
		{
			_timer += Period;
			_sampleIndex = (_sampleIndex + 1) % SampleCount;

			var value = _waveRam[_sampleIndex / 2];
			_sampleBuffer = (_sampleIndex & 1) == 0 ? value >> 4 : value & 0b1111;
		}

		_sample = _sampleBuffer >> _volumeShift;
	}

	public void Reset()
	{
		IsEnabled = false;
		IsDacEnabled = false;
		_volumeShift = VolumeShifts[0];
		_periodValue = 0;
		_isLengthEnabled = false;
	}

	public void ClockLength()
	{
		if (_length.Clock(_isLengthEnabled))
			IsEnabled = false;
	}

	private void Trigger()
	{
		IsEnabled = IsDacEnabled;
		_length.Reset(MaxLength);

		_timer = Period;
		_sampleIndex = 0;
	}
}
