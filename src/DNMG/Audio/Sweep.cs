namespace DNMG.Audio;

internal sealed class Sweep
{
	private const int MaxPeriod = 2047;

	private int _shadowPeriod;
	private int _timer;
	private bool _isEnabled;

	public bool Reset(byte nrx0, int periodValue)
	{
		_shadowPeriod = periodValue;
		_timer = TimerReload(nrx0);
		_isEnabled = Pace(nrx0) != 0 || Step(nrx0) != 0;

		return Step(nrx0) != 0 && NextPeriod(nrx0) > MaxPeriod;
	}

	public bool Clock(byte nrx0, ref int periodValue)
	{
		if (--_timer > 0)
			return false;

		_timer = TimerReload(nrx0);
		if (!_isEnabled || Pace(nrx0) == 0)
			return false;

		var period = NextPeriod(nrx0);
		if (period > MaxPeriod)
			return true;

		if (Step(nrx0) == 0)
			return false;

		_shadowPeriod = period;
		periodValue = period;

		return NextPeriod(nrx0) > MaxPeriod;
	}

	private int NextPeriod(byte nrx0)
	{
		var delta = _shadowPeriod >> Step(nrx0);
		return _shadowPeriod + (IsNegated(nrx0) ? -delta : delta);
	}

	private static int Pace(byte nrx0) => (nrx0 >> 4) & 0b111;

	private static int Step(byte nrx0) => nrx0 & 0b111;

	private static bool IsNegated(byte nrx0) => (nrx0 & 0b1000) != 0;

	private static int TimerReload(byte nrx0) => Pace(nrx0) == 0 ? 8 : Pace(nrx0);
}
