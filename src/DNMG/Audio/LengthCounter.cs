namespace DNMG.Audio;

internal sealed class LengthCounter
{
	private int _counter;

	public void Load(byte value, int maxLength) => _counter = maxLength - (value & (maxLength - 1));

	public void Reset(int maxLength)
	{
		if (_counter == 0)
			_counter = maxLength;
	}

	public bool Clock(bool isLengthEnabled) => isLengthEnabled && _counter > 0 && --_counter == 0;
}
