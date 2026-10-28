using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using DNMG.App.Avalonia.Audio;
using DNMG.App.Avalonia.Models;
using DNMG.App.Avalonia.Models.ColorPalettes;

namespace DNMG.App.Avalonia.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
	public event Action? FrameUpdated;
	public event Action? RomFileLoaded;

	private CancellationTokenSource? _cancellationTokenSource;
	private Task? _emulationTask;
	private Input? _input;
	private NAudioOutput? _audioOutput;
	private int? _sampleRateSetting;
	private IStorageProvider? _storageProvider;
	private IColorPalette _colorPalette = new DmgColorPalette();

	private readonly Dictionary<Key, Input.JoypadButtons> _joypadKeyMapping = new()
	{
		{ Key.Return, Input.JoypadButtons.Start },
		{ Key.Space, Input.JoypadButtons.Select },
		{ Key.A, Input.JoypadButtons.A },
		{ Key.S, Input.JoypadButtons.B },
		{ Key.Up, Input.JoypadButtons.UpArrow },
		{ Key.Down, Input.JoypadButtons.DownArrow },
		{ Key.Left, Input.JoypadButtons.LeftArrow },
		{ Key.Right, Input.JoypadButtons.RightArrow },
	};

	private FilePickerFileType AllRomFiles { get; } = new("Game Boy ROMs")
	{
		Patterns = ["*.gb", "*.gbc"],
		AppleUniformTypeIdentifiers = ["public.data"],
		MimeTypes = ["application/octet-stream"]
	};

	public static bool IsLowLatencyAudioSupported => OperatingSystem.IsWindows();

	[ObservableProperty]
	public partial bool ReduceSpeed { get; set; } = true;

	[ObservableProperty]
	public partial double Volume { get; set; } = 1;

	[ObservableProperty]
	public partial bool IsMono { get; set; }

	[ObservableProperty]
	public partial bool IsLowLatencyAudio { get; set; }

	[ObservableProperty]
	public partial bool IsEmulationRunning { get; set; }

	[ObservableProperty]
	public partial string ErrorMessageFromCrashedEmulationTask { get; set; } = string.Empty;

	public WriteableBitmap GameBoyScreenBuffer { get; } = new(new PixelSize(Ppu.ScreenWidth, Ppu.ScreenHeight), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);

	public void SetStorageProvider(IStorageProvider storageProvider)
	{
		_storageProvider = storageProvider;
	}

	public async Task LoadAndExecuteRomFile()
	{
		ArgumentNullException.ThrowIfNull(_storageProvider);

		var files = await _storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions()
		{
			Title = "Load ROM file",
			FileTypeFilter = [AllRomFiles, FilePickerFileTypes.All],
			AllowMultiple = false
		});

		if (!files.Any()) return;

		if (_cancellationTokenSource is not null)
		{
			await _cancellationTokenSource.CancelAsync();
			if (_emulationTask is not null)
				await Task.WhenAny(_emulationTask);
			_cancellationTokenSource.Dispose();
		}

		ErrorMessageFromCrashedEmulationTask = string.Empty;
		_cancellationTokenSource = new CancellationTokenSource();
		var token = _cancellationTokenSource.Token;

		var romFile = files[0].Path.LocalPath;
		RomFileLoaded?.Invoke();
		try
		{
			_emulationTask = Task.Run(() => RunEmulation(romFile, token), token);
			await _emulationTask;
		}
		catch (Exception e)
		{
			ErrorMessageFromCrashedEmulationTask = e.ToString();
		}

		IsEmulationRunning = false;
	}

	public static void QuitApplication()
	{
		if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopApp)
		{
			desktopApp.Shutdown();
		}
	}

	public async Task SaveScreenshot()
	{
		ArgumentNullException.ThrowIfNull(_storageProvider);

		var fileSaveResult = await _storageProvider.SaveFilePickerWithResultAsync(new FilePickerSaveOptions()
		{
			Title = "Save screenshot",
			DefaultExtension = "png",
			SuggestedFileName = "screenshot",
			FileTypeChoices =
			[
				new FilePickerFileType("Images")
				{
					Patterns = ["*.png"]
				}
			]
		});

		if (fileSaveResult.File is not null)
		{
			GameBoyScreenBuffer.Save(fileSaveResult.File.Path.LocalPath, PngBitmapEncoderOptions.Default);
		}
	}

	public void OnKeyDown(KeyEventArgs e) => OnKey(e.Key, false);
	public void OnKeyUp(KeyEventArgs e) => OnKey(e.Key, true);

	public void SetColorPalette(string palette)
	{
		_colorPalette = palette switch
		{
			"Dmg" => new DmgColorPalette(),
			"Pocket" => new PocketColorPalette(),
			"Crimson" => new CrimsonColorPalette(),
			_ => throw new ArgumentOutOfRangeException(nameof(palette), palette, null)
		};
	}

	public void SetSampleRate(string sampleRate)
	{
		_sampleRateSetting = sampleRate == "Auto" ? null : int.Parse(sampleRate);
		_audioOutput?.Configure(_sampleRateSetting, IsLowLatencyAudio);
	}

	partial void OnIsLowLatencyAudioChanged(bool value) => _audioOutput?.Configure(_sampleRateSetting, value);

	private void RunEmulation(string romFile, CancellationToken token)
	{
		var rom = File.ReadAllBytes(romFile);

		using var audioOutput = new NAudioOutput(_sampleRateSetting, IsLowLatencyAudio);
		_audioOutput = audioOutput;
		var wallClock = new WallClock();

		var cpu = new Cpu(rom);
		var ppu = new Ppu(cpu);
		var timer = new Timer(cpu);
		_input = new Input(cpu);
		var apu = new Apu(cpu, audioOutput.FramesPerBuffer);

		IsEmulationRunning = true;

		const int PpuModeMask = 0b0000_0011;
		const int VBlankPpuMode = 0b0000_0001;
		var lastPpuMode = cpu.Memory[0xFF41] & PpuModeMask;
		while (!token.IsCancellationRequested)
		{
			var cycles = cpu.ExecuteSingleStep();
			timer.ExecuteSingleStep(cycles);
			ppu.ExecuteSingleStep(cycles);
			apu.ExecuteSingleStep(cycles, audioOutput.SampleRate);

			var ppuMode = cpu.Memory[0xFF41] & PpuModeMask;
			if (ppuMode != lastPpuMode && ppuMode == VBlankPpuMode) // Render a new picture once the LCD status register changed to VBlank
			{
				UpdatePixels(ppu);
				FrameUpdated?.Invoke();
			}

			lastPpuMode = ppuMode;

			if (ReduceSpeed)
			{
				if (apu.TryTakeBuffer(out var buffer))
					audioOutput.Queue(buffer, (float)Volume, IsMono);

				wallClock.Wait(cycles);
			}
		}
	}

	private void OnKey(Key key, bool isUp)
	{
		if (_input is null) return;
		if (!_joypadKeyMapping.TryGetValue(key, out var joyPadKey)) return;

		if (isUp)
			_input.PressedButtons &= ~joyPadKey;
		else
			_input.PressedButtons |= joyPadKey;
	}

	private void UpdatePixels(Ppu ppu)
	{
		using var fb = GameBoyScreenBuffer.Lock();
		unsafe
		{
			var ptr = (uint*)fb.Address;
			for (int y = 0; y < Ppu.ScreenHeight; y++)
			{
				for (int x = 0; x < Ppu.ScreenWidth; x++)
				{
					var pixelColor = ppu.FrameBuffer[y, x];
					ptr[y * Ppu.ScreenWidth + x] = pixelColor switch
					{
						0 => _colorPalette.Color1,
						1 => _colorPalette.Color2,
						2 => _colorPalette.Color3,
						_ => _colorPalette.Color4
					};
				}
			}
		}
	}
}
