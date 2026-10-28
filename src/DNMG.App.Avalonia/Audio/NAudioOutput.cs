using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using DNMG.Audio;
using NAudio.CoreAudioApi;
using NAudio.MacOS.CoreAudio;
using NAudio.Wave;
using NAudio.Wave.Alsa;

namespace DNMG.App.Avalonia.Audio;

internal sealed class NAudioOutput : IDisposable
{
	private static readonly double MinQueuedSeconds = OperatingSystem.IsLinux() ? 0.15 : 0.1;

	public readonly int FramesPerBuffer = 512;

	private readonly Lock _lock = new();
	private readonly MMDeviceNotificationClient? _deviceNotifications;
	private volatile QueuedWaveProvider _provider;
	private volatile IWavePlayer? _player;
	private int? _sampleRateSetting;
	private bool _isLowLatency;
	private bool _isDisposed;

	public int SampleRate => _provider.WaveFormat.SampleRate;

	public NAudioOutput(int? sampleRate, bool isLowLatency)
	{
		_sampleRateSetting = sampleRate;
		_isLowLatency = isLowLatency;
		_provider = CreateProvider(sampleRate ?? 48_000);

		if (OperatingSystem.IsWindows())
		{
			_deviceNotifications = new MMDeviceEnumerator().CreateNotificationClient(useSynchronizationContext: false);
			_deviceNotifications.DefaultDeviceChanged += OnDefaultDeviceChanged;
		}
		else if (OperatingSystem.IsMacOS())
		{
			AudioSystemObject.Instance.DefaultOutputDeviceChanged += OnDefaultOutputDeviceChanged;
		}

		RestartPlayer();
	}

	public void Queue(AudioFrame[] buffer, float volume, bool isMono) => _provider.Queue(buffer, volume, isMono);

	public void Configure(int? sampleRate, bool isLowLatency)
	{
		lock (_lock)
		{
			_sampleRateSetting = sampleRate;
			_isLowLatency = isLowLatency;
		}

		_ = Task.Run(RestartPlayer);
	}

	[SuppressMessage("Interoperability", "CA1416:Validate platform compatibility",
		Justification = "_deviceNotifications is null when on unsupported platform")]
	public void Dispose()
	{
		_deviceNotifications?.Dispose();
		if (OperatingSystem.IsMacOS())
			AudioSystemObject.Instance.DefaultOutputDeviceChanged -= OnDefaultOutputDeviceChanged;

		lock (_lock)
		{
			_isDisposed = true;
			StopPlayer();
		}
	}

	[SupportedOSPlatform("windows")]
	private void OnDefaultDeviceChanged(object? sender, DefaultDeviceChangedEventArgs e)
	{
		if (e.Flow == DataFlow.Render && e.Role == Role.Console)
			_ = Task.Run(RestartPlayer);
	}

	[SupportedOSPlatform("macos")]
	private void OnDefaultOutputDeviceChanged(AudioObject sender) => _ = Task.Run(RestartPlayer);

	private void OnPlaybackStopped(object? sender, StoppedEventArgs e) => _ = Task.Run(RestartPlayer);

	private void RestartPlayer()
	{
		lock (_lock)
		{
			StopPlayer();
			if (_isDisposed)
				return;

			try
			{
				var (player, deviceSampleRate) = CreatePlayer(_isLowLatency);
				_player = player;
				_player.PlaybackStopped += OnPlaybackStopped;
				UseSampleRate(_sampleRateSetting ?? (deviceSampleRate % 44_100 == 0 ? 44_100 : 48_000));
				_player.Init(_provider);
				_player.Play();

				if (OperatingSystem.IsWindows() && _player is WasapiPlayer { LowLatencyUnavailableReason: { } reason })
					Trace.WriteLine(reason);
			}
			catch (Exception e)
			{
				Trace.WriteLine(e);
				StopPlayer();
				_provider.Clear();
			}
		}
	}

	private void StopPlayer()
	{
		if (_player is null)
			return;

		_player.PlaybackStopped -= OnPlaybackStopped;
		_player.Dispose();
		_player = null;
	}

	private void UseSampleRate(int sampleRate)
	{
		if (sampleRate == SampleRate)
			return;

		var oldProvider = _provider;
		_provider = CreateProvider(sampleRate);
		oldProvider.Clear();
	}

	private QueuedWaveProvider CreateProvider(int sampleRate)
		=> new(sampleRate, (int)Math.Ceiling(sampleRate * MinQueuedSeconds / FramesPerBuffer));

	private static (IWavePlayer Player, int DeviceSampleRate) CreatePlayer(bool isLowLatency)
	{
		if (OperatingSystem.IsWindows())
		{
			// 30ms is just a value that worked so far. The default is 200ms which is too high.
			var builder = new WasapiPlayerBuilder().WithLatency(30);
			if (isLowLatency)
				builder = builder.WithLowLatency();

			var player = builder.Build();
			return (player, player.DeviceMixFormat.SampleRate);
		}

		if (OperatingSystem.IsMacOSVersionAtLeast(10, 5))
		{
			var device = AudioSystemObject.Instance.DefaultOutputDevice;
			return (new CoreAudioPlayer(device), (int)device.NominalSampleRate);
		}

		if (OperatingSystem.IsLinux())
			return (new AlsaOut(), 48_000);

		throw new PlatformNotSupportedException();
	}
}
