using System;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using DNMG.Audio;
using NAudio.Wave;

namespace DNMG.App.Avalonia.Audio;

internal sealed class QueuedWaveProvider : IWaveProvider
{
	private readonly Channel<short[]> _queue;
	private readonly int _startBuffers;
	private short[] _current = [];
	private int _position;
	private bool _isBuffering = true;

	public QueuedWaveProvider(int sampleRate, int capacity)
	{
		WaveFormat = new(sampleRate, 16, 2);
		_startBuffers = capacity / 2;
		_queue = Channel.CreateBounded<short[]>(new BoundedChannelOptions(capacity)
		{
			SingleReader = true,
			SingleWriter = true
		});
	}

	public WaveFormat WaveFormat { get; }

	public void Queue(AudioFrame[] buffer, float volume, bool isMono)
	{
		var pcm = new short[buffer.Length * 2];
		for (var i = 0; i < buffer.Length; i++)
		{
			var left = buffer[i].Left * volume;
			var right = buffer[i].Right * volume;
			if (isMono)
				left = right = (left + right) / 2;

			pcm[i * 2] = ToPcm(left);
			pcm[(i * 2) + 1] = ToPcm(right);
		}

		_queue.Writer.TryWrite(pcm);
	}

	public void Clear()
	{
		while (_queue.Reader.Count > 0)
			_queue.Reader.TryRead(out _);
	}

	int IWaveProvider.Read(Span<byte> buffer)
	{
		var reader = _queue.Reader;
		var output = MemoryMarshal.Cast<byte, short>(buffer);
		output.Clear();
		if (_isBuffering && !TryStartPlayback())
			return buffer.Length;

		var written = 0;
		while (written < output.Length)
		{
			if (_position == _current.Length)
			{
				if (!reader.TryRead(out var next))
				{
					_isBuffering = true;
					break;
				}

				_current = next;
				_position = 0;
			}

			var count = Math.Min(output.Length - written, _current.Length - _position);
			_current.AsSpan(_position, count).CopyTo(output[written..]);
			written += count;
			_position += count;
		}

		return buffer.Length;
	}

	private bool TryStartPlayback()
	{
		var reader = _queue.Reader;
		if (reader.Count < _startBuffers)
			return false;

		while (reader.Count > _startBuffers)
			reader.TryRead(out _);

		_isBuffering = false;
		return true;
	}

	private static short ToPcm(float sample) => (short)(Math.Clamp(sample, -1f, 1f) * short.MaxValue);
}
