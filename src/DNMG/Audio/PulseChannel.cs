namespace DNMG.Audio;

internal sealed class PulseChannel
{
	private const int MaxLength = 64;

	private static ReadOnlySpan<byte> DutyPatterns => [0b1000_0000, 0b1000_0001, 0b1110_0001, 0b0111_1110];

	private readonly LengthCounter _length = new();
	private readonly Envelope _envelope = new();
	private readonly Sweep? _sweep;

	private byte _sweepSettings;
	private int _duty;
	private byte _envelopeSettings;
	private int _periodValue;
	private bool _isLengthEnabled;

	private int _timer;
	private int _dutyStep;

	private int _sample;

	public PulseChannel(bool hasSweep) => _sweep = hasSweep ? new Sweep() : null;

	private byte DutyPattern => DutyPatterns[_duty];
	private int Period => (2048 - _periodValue) * 4;

	public bool IsEnabled { get; private set; }

	public bool IsDacEnabled => (_envelopeSettings & 0b1111_1000) != 0;

	public int Sample => IsEnabled ? _sample : 0;

	public void Write(int register, byte value)
	{
		switch (register)
		{
			case 0:
				_sweepSettings = value;
				break;
			case 1:
				_duty = value >> 6;
				_length.Load(value, MaxLength);
				break;
			case 2:
				_envelopeSettings = value;
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

	public void Step(int tCycles)
	{
		if (!IsDacEnabled)
			IsEnabled = false;

		_timer -= tCycles;
		while (_timer <= 0)
		{
			_timer += Period;
			_dutyStep = (_dutyStep + 1) & 0b111;
		}

		var waveform = (DutyPattern >> _dutyStep) & 1;
		_sample = _envelope.Apply(waveform);
	}

	public void Reset()
	{
		IsEnabled = false;
		_sweepSettings = 0;
		_duty = 0;
		_envelopeSettings = 0;
		_periodValue = 0;
		_isLengthEnabled = false;
	}

	public void ClockLength()
	{
		if (_length.Clock(_isLengthEnabled))
			IsEnabled = false;
	}

	public void ClockEnvelope() => _envelope.Clock(_envelopeSettings);

	public void ClockSweep()
	{
		if (_sweep is not null && _sweep.Clock(_sweepSettings, ref _periodValue))
			IsEnabled = false;
	}

	private void Trigger()
	{
		IsEnabled = IsDacEnabled;
		_length.Reset(MaxLength);
		_envelope.Reset(_envelopeSettings);
		_timer = Period;

		if (_sweep is not null && _sweep.Reset(_sweepSettings, _periodValue))
			IsEnabled = false;
	}
}
