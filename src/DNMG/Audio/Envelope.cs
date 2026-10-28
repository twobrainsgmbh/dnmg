namespace DNMG.Audio;

internal sealed class Envelope
{
	private int _timer;
	private int _volume;

	public void Reset(byte nrx2)
	{
        _volume = nrx2 >> 4;
		_timer = nrx2 & 0b111;
	}

	public void Clock(byte nrx2)
	{
		var pace = nrx2 & 0b111;
		if (pace == 0 || --_timer > 0)
			return;

		_timer = pace;
		var next = _volume + ((nrx2 & 0b1000) != 0 ? 1 : -1);
		if (next is >= 0 and <= 15)
            _volume = next;
	}

	public int Apply(int waveform) => waveform * _volume;
}
