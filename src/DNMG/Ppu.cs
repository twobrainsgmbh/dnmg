using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DNMG;

public sealed class Ppu
{
	[Flags]
	private enum LcdcBits : byte
	{
		None,
		BackgroundAndWindowEnable = 1 << 0,
		ObjectEnable = 1 << 1,
		ObjectSize = 1 << 2,
		BackgroundTileMapSelect = 1 << 3,
		BackgroundAndWindowTileDataSelect = 1 << 4,
		WindowEnable = 1 << 5,
		WindowTileMapSelect = 1 << 6,
		LCDEnable = 1 << 7
	}

	private enum PpuMode
	{
		HBlank = 0,
		VBlank = 1,
		OAMScan = 2,
		LCDTransfer = 3
	}

	[Flags]
	private enum StatBits : byte
	{
		None = 0,
		Mode1 = 1 << 0,
		Mode2 = 1 << 1,
		Mode3 = Mode1 | Mode2,
		LYCeqLY = 1 << 2,
		Mode0IntEnable = 1 << 3,
		Mode1IntEnable = 1 << 4,
		Mode2IntEnable = 1 << 5,
		LYCIntEnable = 1 << 6,
		Reserved7 = 1 << 7
	}

	[Flags]
	private enum OamBits : byte
	{
		None,
		DMGPalette = 1 << 4,
		XFlip = 1 << 5,
		YFlip = 1 << 6,
		Priority = 1 << 7
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	private readonly struct OamEntry
	{
		public readonly byte Y, X, TileIndex;
		public readonly OamBits Attributes;
	}

	private readonly Cpu _cpu;
	private int _currentScanLineDots;

	// LCD Control
	private LcdcBits LCDC { get => (LcdcBits)_cpu.Memory[0xFF40]; set => _cpu.Memory[0xFF40] = (byte)value; }

	// LCD Status
	private StatBits STAT { get => (StatBits)_cpu.Memory[0xFF41]; set => _cpu.Memory[0xFF41] = (byte)value; }

	// Background viewport Y position
	private byte SCY => _cpu.Memory[0xFF42];

	// Background viewport X position
	private byte SCX => _cpu.Memory[0xFF43];

	// Window Y position
	private byte WY => _cpu.Memory[0xFF4A];

	// Window X position plus 7
	private byte WX => _cpu.Memory[0xFF4B];

	// LCD Y coordinate [read-only]. Values range from 0..153. 144..153 is the VBlank period.
	private byte LY { get => _cpu.Memory[0xFF44]; set => _cpu.Memory[0xFF44] = value; }

	// LY Compare. When LY=LYC, STATF_LYCF will be set in STAT and (if enabled) a STAT interrupt is fired.
	private byte LYC => _cpu.Memory[0xFF45];

	public const int ScreenWidth = 160;
	public const int ScreenHeight = 144;
	public readonly byte[,] FrameBuffer = new byte[ScreenHeight, ScreenWidth]; // 0 = White, 1 = LightGray, 2 = DarkGray, 3 = Black
	private PpuMode _mode;
	private bool _statInterruptLine;
	private readonly int[] _spriteSortKeys = new int[10];

	public Ppu(Cpu cpu)
	{
		if (!BitConverter.IsLittleEndian)
			throw new PlatformNotSupportedException("Big-endian platforms are not supported (tile decoding assumes little-endian).");

		_cpu = cpu;
		_cpu.OnMemoryWrite[0x41] = value => _cpu.Memory[0xFF41] = (byte)((_cpu.Memory[0xFF41] & 0b1000_0111) | (value & ~0b1000_0111)); // STAT
		_cpu.OnMemoryWrite[0x46] = value => { _cpu.Memory[0xFF46] = value; Array.Copy(_cpu.Memory, value << 8, _cpu.Memory, 0xFE00, 0xA0); }; // DMA. This should be done in a more cycle-accurate way, but that would require a lot of changes to the CPU and memory system
		STAT = StatBits.Reserved7;
		LCDC = LcdcBits.BackgroundAndWindowEnable | LcdcBits.BackgroundAndWindowTileDataSelect | LcdcBits.LCDEnable;
	}

	public void ExecuteSingleStep(int cycleDelta)
	{
		var lcdc = LCDC;
		if (!lcdc.HasFlag(LcdcBits.LCDEnable))
		{
			// When LCD is disabled, LY is set to 0, and the mode is set to HBlank
			LY = 0;
			_currentScanLineDots = 0;
			STAT &= (StatBits)0b1111_1000;
			return;
		}

		const int dotsPerScanline = 456;
		_currentScanLineDots += cycleDelta * 4; // 4 dots per CPU cycle
		if (_currentScanLineDots >= dotsPerScanline)
		{
			_currentScanLineDots -= dotsPerScanline;
			if (++LY > 153)
				LY = 0;
			if (LY == ScreenHeight) // VBlank?
				_cpu.RequestInterrupt(Cpu.IFBits.VBlank);
		}

		var mode = LY < ScreenHeight ? _currentScanLineDots switch
		{
			< 80 => PpuMode.OAMScan,
			< 369 => PpuMode.LCDTransfer,
			_ => PpuMode.HBlank
		} : PpuMode.VBlank;

		var stat = STAT;
		stat &= (StatBits)0b1111_1000;
		stat |= (StatBits)mode;
		stat |= LYC == LY ? StatBits.LYCeqLY : 0;
		STAT = stat;

		var statInterruptLine = (stat.HasFlag(StatBits.LYCeqLY) && stat.HasFlag(StatBits.LYCIntEnable)) || (mode < PpuMode.LCDTransfer && stat.HasFlag((StatBits)((int)StatBits.Mode0IntEnable << (int)mode)));
		if (statInterruptLine && !_statInterruptLine) // set on rising edge
			_cpu.RequestInterrupt(Cpu.IFBits.LCD);
		_statInterruptLine = statInterruptLine;

		// if the mode changed, and the new mode is LCDTransfer, then we need to render the current scanline
		(_mode, mode) = (mode, _mode);
		if (mode == _mode || _mode != PpuMode.LCDTransfer)
			return;

		// Read and sort OAM entries. The sorting is based on X position, then OAM index, and also includes the tile data for the current scanline as a
		// cache to avoid redundant calculations later. Only sprites that are within the vertical range of the current scanline are included, and a
		// maximum of 10 sprites are included since that's the maximum that can be displayed on a single scanline.
		int screenY = LY;
		var tileData = MemoryMarshal.Cast<byte, ushort>(new ReadOnlySpan<byte>(_cpu.Memory, 0x8000, 384 * sizeof(ushort) * 8));
		var oam = MemoryMarshal.Cast<byte, OamEntry>(_cpu.Memory.AsSpan(0xFE00, 40 * Unsafe.SizeOf<OamEntry>()));
		var spriteCount = 0;
		if (lcdc.HasFlag(LcdcBits.ObjectEnable)) // only gather sprites if they are enabled
		{
			var spriteHeightMinusOne = lcdc.HasFlag(LcdcBits.ObjectSize) ? 15 : 7;
			for (var i = 0; i < oam.Length; i++)
			{
				ref var entry = ref oam[i];
				var row = screenY + 16 - entry.Y; // sprite Y position is offset by 16, so we need to subtract that to get the effective row within the sprite
				if (row < 0 || row > spriteHeightMinusOne)
					continue;
				// in 8x16 mode, the lower bit of the tile index is ignored, and the row determines whether to use tileIndex or tileIndex + 1
				var tileIndex = spriteHeightMinusOne == 15 ? (entry.TileIndex & ~1) : entry.TileIndex;
				if (entry.Attributes.HasFlag(OamBits.YFlip)) // if the sprite is flipped vertically, invert the row to get the correct tile data
					row = spriteHeightMinusOne - row;
				// Sort by X position, then by OAM index. Also include tile data as a cache (not relevant for sorting). Merge everything
				// into a single int (but leave the sign bit unused) like: 0b00XX_XXXX_XXII_IIII_DDDD_DDDD_DDDD_DDDD
				_spriteSortKeys[spriteCount++] = (entry.X << 22) | (i << 16) | tileData[(tileIndex * 8) + row];
				if (spriteCount == _spriteSortKeys.Length)
					break;
			}
			if (spriteCount > 1)
				Array.Sort(_spriteSortKeys, 0, spriteCount);
		}

		// precalculate background related values that are shared across the entire scanline, to avoid redundant calculations within the pixel loop
		var backgroundTileRow = (SCY + screenY) & 0xFF; // background wraps around, use & 0xFF to simulate that behavior
		var backgroundTileMapRowAddr = (lcdc.HasFlag(LcdcBits.BackgroundTileMapSelect) ? 0x9C00 : 0x9800) + ((backgroundTileRow / 8) * 32); // each tile covers 8 pixels vertically, and there are 32 tile indices per row
		backgroundTileRow %= 8; // effective row within the background tile, between 0 and 7

		// precalculate window related values that are shared across the entire scanline
		var windowTileRow = screenY - WY;
		var windowTileMapRowAddr = (lcdc.HasFlag(LcdcBits.WindowTileMapSelect) ? 0x9C00 : 0x9800) + ((windowTileRow / 8) * 32); // each tile covers 8 pixels vertically, and there are 32 tile indices per row
		var windowX = (lcdc.HasFlag(LcdcBits.WindowEnable) && windowTileRow >= 0) ? WX - 7 : int.MaxValue; // setting it to int.MaxValue effectively disables the window for this scanline
		windowTileRow %= 8; // effective row within the tile, between 0 and 7. Do this last, as (-8, -16, etc) % 8 turns into +0 which would incorrectly make the window visible

		// cache other values that are statically known within this scanline
		var scx = SCX; // cache SCX since it's used for every pixel
		var tilePalette = _cpu.Memory[0xFF47]; // background palette
		var obp0Palette = _cpu.Memory[0xFF48]; // object palette 0
		var obp1Palette = _cpu.Memory[0xFF49]; // object palette 1
		var passedSprites = 0; // as we iterate through pixels from left to right, we can skip sprites that are already passed
		ref var currentTargetPixel = ref FrameBuffer[screenY, 0];
		for (var screenX = 0; screenX < ScreenWidth; screenX++)
		{
			var colorIndex = 0;
			int palette = tilePalette;

			// handle background and window (window has priority over background)
			if (lcdc.HasFlag(LcdcBits.BackgroundAndWindowEnable))
			{
				int tileMapRowAddr, x, y;
				if (screenX < windowX) // current pixel not within window region? use background values
				{
					tileMapRowAddr = backgroundTileMapRowAddr;
					x = (screenX + scx) & 0xFF; // background wraps around, use & 0xFF to simulate that behavior
					y = backgroundTileRow;
				}
				else
				{
					tileMapRowAddr = windowTileMapRowAddr;
					x = screenX - windowX; // always between 0 and ScreenWidth - 1
					y = windowTileRow;
				}
				int tileIndex = _cpu.Memory[tileMapRowAddr + (x / 8)]; // each tile covers 8x8 pixels
				if (!lcdc.HasFlag(LcdcBits.BackgroundAndWindowTileDataSelect))
					tileIndex = 256 + (sbyte)tileIndex;
				colorIndex = (tileData[(tileIndex * 8) + y] >> (7 - (x % 8))) & 0x0101;
			}

			// handle sprites
			for (var i = passedSprites; i < spriteCount; i++)
			{
				var key = _spriteSortKeys[i];
				ref var entry = ref oam[(key >> 16) & 0x3F]; // get oam index, use & 0x3F to ignore the bits from entry.X
				var col = screenX + 8 - entry.X; // sprite X position is offset by 8, so we need to subtract that to get the effective column within the sprite
				if (col < 0) // if this sprite is not yet visible, then the rest of the sprites will also not be visible
					break;
				if (col > 7)
				{
					++passedSprites; // if this sprite has already passed, we can skip it in future iterations
					continue;
				}
				var spriteColorIndex = (key >> (entry.Attributes.HasFlag(OamBits.XFlip) ? col : 7 - col)) & 0x0101;
				if (spriteColorIndex == 0) // do not process transparent pixels, and continue to the next sprite in the list
					continue;
				if (!entry.Attributes.HasFlag(OamBits.Priority) || colorIndex == 0) // sprite has priority over background when priority bit is not set, or when bg color index is 0
				{
					colorIndex = spriteColorIndex;
					palette = entry.Attributes.HasFlag(OamBits.DMGPalette) ? obp1Palette : obp0Palette;
				}
				break;
			}

			// calculate the final color and write it to the framebuffer
			colorIndex |= colorIndex >> 7; // combine the previously masked 0bH_0000_000L bits into a single 0bH_0000_00HL value
			currentTargetPixel = (byte)((palette >> (colorIndex * 2)) & 0b11); // intentionally not using "colorIndex & 0b11" before shifting, because shifts are "count % 32" by definition
			currentTargetPixel = ref Unsafe.Add(ref currentTargetPixel, 1);
		}
	}
}
