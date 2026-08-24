namespace DNMG;

public sealed class Ppu
{
	[Flags]
	public enum LcdcBits : byte
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
	public enum StatBits : byte
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
	public enum OAMAttributeBits : byte
	{
		None,
		DMGPalette = 1 << 4,
		XFlip = 1 << 5,
		YFlip = 1 << 6,
		Priority =  1 << 7
	}

	/// <summary>
	/// Populated once per line on the display to avoid scanning all 40 OAM entries for each pixel in a line
	/// </summary>
	readonly struct OAMRenderData
	{
		public readonly int X;
		public readonly byte TileIndex;
		public readonly int TileY;

		public bool HasPriority => _attributes.HasFlag(OAMAttributeBits.Priority);
		public bool XFlip => _attributes.HasFlag(OAMAttributeBits.XFlip);
		public bool UseDMGPalette => _attributes.HasFlag(OAMAttributeBits.DMGPalette);

		private readonly OAMAttributeBits _attributes;

		public OAMRenderData(OAMAttributeBits attributes, int x, byte tileIndex, int tileY)
		{
			_attributes = attributes;
			X = x;
			TileIndex = tileIndex;
			TileY = tileY;
		}
	}

	private readonly Cpu _cpu;
	private int _currentScanLineDots;

	/// <summary>
	/// LCD Control
	/// </summary>
	public LcdcBits LCDC { get => (LcdcBits)_cpu.Memory[0xFF40]; set => _cpu.Memory[0xFF40] = (byte)value; } // LCD Control Register

	/// <summary>
	/// LCD Status
	/// </summary>
	public StatBits STAT { get => (StatBits)_cpu.Memory[0xFF41]; set => _cpu.Memory[0xFF41] = (byte)value; } // LCD status Register

	/// <summary>
	/// Background viewport Y position
	/// </summary>
	private byte SCY => _cpu.Memory[0xFF42];

	/// <summary>
	/// Background viewport X position
	/// </summary>
	private byte SCX => _cpu.Memory[0xFF43];

	/// <summary>
	/// Window Y position, X position plus 7
	/// </summary>
	private byte WY => _cpu.Memory[0xFF4A];

	/// <summary>
	/// Window X position plus 7
	/// </summary>
	private byte WX => _cpu.Memory[0xFF4B];

	/// <summary>
	/// LCD Y coordinate [read-only]. Values range from 0->153. 144->153 is the VBlank period.
	/// </summary>
	public byte LY { get => _cpu.Memory[0xFF44]; set => _cpu.Memory[0xFF44] = value; }

	/// <summary>
	/// LY Compare. When LY=LYC, STATF_LYCF will be set in STAT and (if enabled) a STAT interrupt is fired.
	/// </summary>
	private byte LYC => _cpu.Memory[0xFF45];

	private const ushort TilePaletteAddress = 0xFF47;
	private const ushort OAMAttributesStartAddress = 0xFE00;
	private const ushort OAMAttributesEndAddress = 0xFE9F;
	private const ushort OBP0PaletteAddress = 0xFF48;
	private const ushort OBP1PaletteAddress = 0xFF49;

	public const int ScreenWidth = 160;
	public const int ScreenHeight = 144;
	public readonly byte[,] FrameBuffer = new byte[ScreenWidth, ScreenHeight];
	private bool _statInterruptLine;

	public Ppu(Cpu cpu)
	{
		_cpu = cpu;
		_cpu.OnMemoryWrite[0x41] = value => _cpu.Memory[0xFF41] = (byte)((_cpu.Memory[0xFF41] & 0b1000_0111) | (value & ~0b1000_0111)); // STAT
		_cpu.OnMemoryWrite[0x46] = value => { _cpu.Memory[0xFF46] = value; Array.Copy(_cpu.Memory, value << 8, _cpu.Memory, 0xFE00, 0xA0); }; // DMA: OAM DMA source address & start
		STAT = StatBits.Reserved7;
		LCDC = LcdcBits.BackgroundAndWindowEnable | LcdcBits.BackgroundAndWindowTileDataSelect | LcdcBits.LCDEnable;
	}

	private PpuMode GetMode() => LY < ScreenHeight ? _currentScanLineDots switch
	{
		< 80 => PpuMode.OAMScan,
		< 369 => PpuMode.LCDTransfer,
		_ => PpuMode.HBlank
	} : PpuMode.VBlank;

	public void ExecuteSingleStep(int cycleDelta)
	{
		if (!LCDC.HasFlag(LcdcBits.LCDEnable))
		{
			// When LCD is disabled, LY is set to 0, and the mode is set to HBlank
			LY = 0;
			_currentScanLineDots = 0;
			STAT &= (StatBits)0b1111_1000;
			return;
		}

		var prevMode = GetMode();

		const int dotsPerScanline = 456;
		_currentScanLineDots += cycleDelta * 4; // 4 dots per CPU cycle
		if (_currentScanLineDots > dotsPerScanline)
		{
			_currentScanLineDots -= dotsPerScanline;
			if (++LY > 153)
				LY = 0;
			if (LY == ScreenHeight) // VBlank?
				_cpu.RequestInterrupt(Cpu.IFBits.VBlank);
		}

		var mode = GetMode();

		var stat = STAT;
		stat &= (StatBits)0b1111_1000;
		stat |= (StatBits)mode;
		stat |= LYC == LY ? StatBits.LYCeqLY : 0;
		STAT = stat;

		var statInterruptLine = (stat.HasFlag(StatBits.LYCeqLY) && stat.HasFlag(StatBits.LYCIntEnable)) || (mode < PpuMode.LCDTransfer && (stat.HasFlag((StatBits)((int)StatBits.Mode0IntEnable << (int)mode))));
		if (statInterruptLine && !_statInterruptLine) // set on rising edge
			_cpu.RequestInterrupt(Cpu.IFBits.LCD);
		_statInterruptLine = statInterruptLine;

		// if we didn't just enter LCD Transfer mode, skip drawing (essentially moves to a new line (LY++) if it does NOT return)
		if (mode != PpuMode.LCDTransfer || mode == prevMode)
			return;

		var screenY = LY;
		var lcdc = LCDC;

		// Tile index addresses. 0 = 9800–9BFF; 1 = 9C00–9FFF - 32x32 tile indices. Total size = 1k
		var backgroundTileIndexBaseAddr = lcdc.HasFlag(LcdcBits.BackgroundTileMapSelect) ? 0x9C00 : 0x9800;
		var windowTileIndexBaseAddr = lcdc.HasFlag(LcdcBits.WindowTileMapSelect) ? 0x9C00 : 0x9800;
		var palette = _cpu.Memory[TilePaletteAddress];

		const int oamWidth = 8;
		const int oam8By16Height = 16;
		int oamHeight = lcdc.HasFlag(LcdcBits.ObjectSize) ? oam8By16Height : oamWidth;
		var screenYForOAM = screenY + 16;

		// Max 40 OAM entries; Only 10 can actually be visible on a given scanline.
		Span<OAMRenderData> visibleOAMsInLine = stackalloc OAMRenderData[40];
		int visibleOamCount = 0;

		if (lcdc.HasFlag(LcdcBits.ObjectEnable))
		{
			var u = OAMAttributesStartAddress;
			while (u <= OAMAttributesEndAddress)
			{
				var y = _cpu.Memory[u++];
				if (screenYForOAM < y || screenYForOAM >= y + oamHeight)
				{
					u += 3;
					continue;
				}

				var x = _cpu.Memory[u++];
				var tileIndex = _cpu.Memory[u++];
				var attributes = (OAMAttributeBits)_cpu.Memory[u++];

				var tileIsTopTile = true; // Only relevant for 8x16 OAM with an upper and lower title in a single OAM - defines if the current screenY position lies in the upper tile (non-Y-flipped)
				if (oamHeight == oam8By16Height)
				{
					tileIsTopTile = screenYForOAM - 8 < y;
					tileIndex = (tileIsTopTile && !attributes.HasFlag(OAMAttributeBits.YFlip)) || (!tileIsTopTile && attributes.HasFlag(OAMAttributeBits.YFlip)) ?
						(byte)(tileIndex & 0xFE) : (byte)(tileIndex | 0x01);
				}

				// Pre-compute the Y coordinate within the tile, because it is constant for the entire scanline
				var tileY = attributes.HasFlag(OAMAttributeBits.YFlip)
					? y + (tileIsTopTile ? 7 : 15) - screenYForOAM
					: screenYForOAM - (tileIsTopTile ? 0 : 8) - y;

				visibleOAMsInLine[visibleOamCount++] = new OAMRenderData(attributes, x, tileIndex, tileY);
			}
		}

		for (var screenX = 0; screenX < ScreenWidth; screenX++)
		{
			var color = 0;
			var backgroundAndWindowColorIndex = 0;
			if (lcdc.HasFlag(LcdcBits.BackgroundAndWindowEnable))
			{
				backgroundAndWindowColorIndex = GetBackgroundTileMapPixelIndex(backgroundTileIndexBaseAddr, (byte)(SCX + screenX), (byte)(SCY + screenY));
				if (lcdc.HasFlag(LcdcBits.WindowEnable))
				{
					var x = screenX - (WX - 7);
					var y = screenY - WY;
					if (x >= 0 && y >= 0)
						backgroundAndWindowColorIndex = GetBackgroundTileMapPixelIndex(windowTileIndexBaseAddr, x, y);
				}
				color = (palette >> (backgroundAndWindowColorIndex * 2)) & 0b11;
			}

			// Render only pre-filtered OAM sprites for this pixel
			var screenXForOAM = screenX + 8;
			for (var i = 0; i < visibleOamCount; i++)
			{
				ref var oam = ref visibleOAMsInLine[i];

				if (screenXForOAM < oam.X || screenXForOAM >= oam.X + oamWidth || (oam.HasPriority && backgroundAndWindowColorIndex != 0))
					continue;

				var oamColorIndex = GetTilePixelColorIndex(oam.TileIndex, oam.XFlip ? oam.X + 7 - screenXForOAM : screenXForOAM - oam.X, oam.TileY);

				if (oamColorIndex != 0)
				{
					color = _cpu.Memory[oam.UseDMGPalette ? OBP1PaletteAddress : OBP0PaletteAddress] >> (oamColorIndex * 2);
					color &= 0b11;
				}
			}

			FrameBuffer[screenX, screenY] = (byte)color;
		}
	}

	private int GetBackgroundTileMapPixelIndex(int tileIndexBaseAddr, int x, int y)
	{
		var tilemapX = x / 8;
		var tilemapY = y / 8;

		var tileIndexAddress = tileIndexBaseAddr + (tilemapY * 32) + tilemapX;
		int tileIndex = _cpu.Memory[tileIndexAddress];
		if (!LCDC.HasFlag(LcdcBits.BackgroundAndWindowTileDataSelect))
			tileIndex = 256 + (sbyte)tileIndex;
		return GetTilePixelColorIndex(tileIndex, x % 8, y % 8);
	}

	private int GetTilePixelColorIndex(int tileIndex, int x, int y)
	{
		// Each tile is 16 bytes (2 bytes per row for 8 rows). 256 Tiles. Total size = 4k
		var offs = 0x8000 + (tileIndex * 16) + (y * 2);
		var l = _cpu.Memory[offs];
		var h = _cpu.Memory[offs + 1];

		var shift = 7 - x;
		l >>= shift;
		l &= 0b1;

		h >>= shift;
		h &= 0b1;
		h <<= 1;

		return l | h;
	}
}
