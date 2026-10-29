using DNMG.Audio;

namespace DNMG;

// SEE: https://gbdev.io/pandocs/Audio_Registers.html
// SEE: https://gbdev.io/pandocs/Audio_details.html
// SEE: https://nightshade256.github.io/2021/03/27/gb-sound-emulation.html
public sealed class Apu
{
	private const double HighPassChargeFactor = 0.999958;

	private static ReadOnlySpan<byte> ReadMasks =>
	[
		0x80, 0x3F, 0x00, 0xFF, 0xBF, // NR10 .. NR14
		0xFF, 0x3F, 0x00, 0xFF, 0xBF, // NR20 .. NR24
		0x7F, 0xFF, 0x9F, 0xFF, 0xBF, // NR30 .. NR34
		0xFF, 0xFF, 0x00, 0x00, 0xBF, // NR40 .. NR44
		0x00, 0x00, 0x70,             // NR50 .. NR52
		0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, // FF27 .. FF2F
		0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
		0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00  // wave RAM
	];

	private readonly Cpu _cpu;
	private readonly PulseChannel _channel1;
	private readonly PulseChannel _channel2;
	private readonly WaveChannel _channel3;
	private readonly NoiseChannel _channel4;
	private readonly AudioFrame[] _nextBuffer;

	private int _sampleAccumulator;
	private int _frameCount;
	private bool _isBufferReady;
	private int _divApuStep;
	private byte _previousDiv;
	private bool _isPoweredOn;
	private float _capacitorLeft;
	private float _capacitorRight;

	private byte NR50 => _cpu.Memory[0xFF24];
	private byte NR51 => _cpu.Memory[0xFF25];

	public Apu(Cpu cpu, int framesPerBuffer)
	{
		_cpu = cpu;
		_nextBuffer = new AudioFrame[framesPerBuffer];
		_channel1 = new PulseChannel(hasSweep: true);
		_channel2 = new PulseChannel(hasSweep: false);
		_channel3 = new WaveChannel();
		_channel4 = new NoiseChannel();

		foreach (var address in Enumerable.Range(0xFF10, 0x30))
			cpu.OnMemoryWrite[address & 0xFF] = value => WriteRegister(address, value);

		ReadMasks.CopyTo(cpu.Memory.AsSpan(0xFF10));

		WriteRegister(0xFF26, 0x80);
		WriteRegister(0xFF24, 0x77);
		WriteRegister(0xFF25, 0xF3);
	}

	public void ExecuteSingleStep(int cycleDelta, int sampleRate)
	{
		ClockFrameSequencer();

		var tCycles = cycleDelta * 4;
		_channel1.Step(tCycles);
		_channel2.Step(tCycles);
		_channel3.Step(tCycles);
		_channel4.Step(tCycles);

		// TODO: with dynamic samplerate & a / speedMultiplier, we have some edge cases to worry about.
		// We might have a higher sample rate then 1 cycle or a lower rate then something, idk...
		// We probably just need a clamp
		_sampleAccumulator += tCycles * sampleRate;

		while (_sampleAccumulator >= WallClock.MasterClockHz)
		{
			_sampleAccumulator -= WallClock.MasterClockHz;
			EmitFrame(sampleRate);
		}

		UpdateNr52();
	}

	public bool TryTakeBuffer(out AudioFrame[] buffer)
	{
		buffer = _nextBuffer;
		if (!_isBufferReady)
			return false;

		_isBufferReady = false;
		return true;
	}

	private void WriteRegister(int address, byte value)
	{
		if (!_isPoweredOn && address < 0xFF26)
			return;

		_cpu.Memory[address] = (byte)(value | ReadMasks[address - 0xFF10]);

		switch (address)
		{
			case < 0xFF15: _channel1.Write(address - 0xFF10, value); break;
			case < 0xFF1A: _channel2.Write(address - 0xFF15, value); break;
			case < 0xFF1F: _channel3.Write(address - 0xFF1A, value); break;
			case < 0xFF24: _channel4.Write(address - 0xFF1F, value); break;
			case 0xFF26: SetPower((value & 0b1000_0000) != 0); break;
			case >= 0xFF30: _channel3.WriteWaveRam(address - 0xFF30, value); break;
		}
	}

	private void SetPower(bool isPoweredOn)
	{
		_isPoweredOn = isPoweredOn;
		if (isPoweredOn)
			return;

		ReadMasks[..0x16].CopyTo(_cpu.Memory.AsSpan(0xFF10));
		_channel1.Reset();
		_channel2.Reset();
		_channel3.Reset();
		_channel4.Reset();
	}

	private void ClockFrameSequencer()
	{
		if (!TryAdvanceFrameSequencer())
			return;

		if ((_divApuStep & 1) == 0) // 256 Hz
		{
			_channel1.ClockLength();
			_channel2.ClockLength();
			_channel3.ClockLength();
			_channel4.ClockLength();
		}

		if (_divApuStep is 2 or 6) // 128 Hz
			_channel1.ClockSweep();

		if (_divApuStep == 7) // 64 Hz
		{
			_channel1.ClockEnvelope();
			_channel2.ClockEnvelope();
			_channel4.ClockEnvelope();
		}

		return;

		bool TryAdvanceFrameSequencer()
		{
			const byte divApuBit = 0b10000;

			var div = _cpu.Memory[0xFF04];
			var isFallingEdge = (_previousDiv & divApuBit) != 0 && (div & divApuBit) == 0;
			_previousDiv = div;

			if (isFallingEdge)
				_divApuStep = (_divApuStep + 1) & 0b111;

			return isFallingEdge;
		}
	}

	private void EmitFrame(int sampleRate)
	{
		Span<float> outputs =
		[
			Dac(_channel1.Sample, _channel1.IsDacEnabled),
			Dac(_channel2.Sample, _channel2.IsDacEnabled),
			Dac(_channel3.Sample, _channel3.IsDacEnabled),
			Dac(_channel4.Sample, _channel4.IsDacEnabled)
		];

		var panning = NR51;
		float left = 0, right = 0;
		for (var i = 0; i < outputs.Length; i++)
		{
			if ((panning & (0b0001_0000 << i)) != 0)
				left += outputs[i];

			if ((panning & (0b0000_0001 << i)) != 0)
				right += outputs[i];
		}

		left /= outputs.Length;
		right /= outputs.Length;

		var masterVolume = NR50;
		var highPassFactor = (float)Math.Pow(HighPassChargeFactor, WallClock.MasterClockHz / (double)sampleRate);
		left = ApplyHighPass(ref _capacitorLeft, ApplyMasterVolume(left, masterVolume >> 4), highPassFactor);
		right = ApplyHighPass(ref _capacitorRight, ApplyMasterVolume(right, masterVolume), highPassFactor);

		_nextBuffer[_frameCount++] = new AudioFrame { Left = left, Right = right };
		if (_frameCount < _nextBuffer.Length)
			return;

		_frameCount = 0;
		_isBufferReady = true;
	}

	private void UpdateNr52()
	{
		// SEE: https://gbdev.io/pandocs/Audio_Registers.html#ff26--nr52-audio-master-control

		var value = (_isPoweredOn ? 0b1000_0000 : 0)
			| 0b0111_0000
			| (_channel1.IsEnabled ? 0b0001 : 0)
			| (_channel2.IsEnabled ? 0b0010 : 0)
			| (_channel3.IsEnabled ? 0b0100 : 0)
			| (_channel4.IsEnabled ? 0b1000 : 0);

		_cpu.Memory[0xFF26] = (byte)value;
	}

	private static float Dac(int digitalSample, bool isDacEnabled) => isDacEnabled ? (digitalSample / 7.5f) - 1f : 0f;

	private static float ApplyMasterVolume(float sample, int volume) => sample * (((volume & 0b111) + 1) / 8f);

	private static float ApplyHighPass(ref float capacitor, float input, float factor)
	{
		var output = input - capacitor;
		capacitor = input - (output * factor);
		return output;
	}
}
