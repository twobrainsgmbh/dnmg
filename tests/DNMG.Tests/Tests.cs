using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Xunit;

namespace DNMG.Tests;

public class Tests
{
	private static readonly HttpClient s_http = new();

	[Theory]
	[InlineData("01-special.gb", "0A5C536FDC53EDD2FA542644347CBAA536111D2ADD34EBE7F9276FCDE464EAFE")]
	[InlineData("02-interrupts.gb", "7971C750E4E1B09C3DE06D2D83D317827B5833CBACCDD6368EA5266CF0AE2E78")]
	[InlineData("03-op sp,hl.gb", "BFA92CCAFB1C9A4D900D7D440CA09C5B16ED8AE5C3A4A7EDEE290137EE02956E")]
	[InlineData("04-op r,imm.gb", "B0AF9F3EEFEBF6981BC61F81443789FD1D0DB738B7C65AD8D4C9658447AD4E0D")]
	[InlineData("05-op rp.gb", "8424F96DB08F52934C8320ABEED06F94CE12E089D8ECFDDA1B478704FD21F7A8")]
	[InlineData("06-ld r,r.gb", "3489C56854727644C01B516A87ECC489C74234F3BFECE9618585B7CE410DBF4F")]
	[InlineData("07-jr,jp,call,ret,rst.gb", "E7E9F968DF849381031C6D8C7DCEB6732EAA8418C7C16CFEB891771EF024F843")]
	[InlineData("08-misc instrs.gb", "4C6A66F336190B2E59DA6EED35C6062ECF416F5E46B378AFE0894D2ED7B83EBD")]
	[InlineData("09-op r,r.gb", "49925A312138BE7D5B3267A26130CB23076A83F68FC155FE0A63387FEAAB9529")]
	[InlineData("10-bit ops.gb", "CC1AA590EE90713A75EA85B3EB5D59E25B5196C1A8F5DDCFB95C4F5E49A9FBF5")]
	[InlineData("11-op a,(hl).gb", "BB85BE8C21E55118DFEC0C7CE51884DE205C8FAF38699A44C80A0B231E5F6927")]
	public async Task Blarggs_CpuInstr(string fileName, string hash)
	{
		var url = "https://github.com/retrio/gb-test-roms/raw/refs/heads/master/cpu_instrs/individual/" + fileName;
		Assert.True(await ExecuteRom(url, hash));
	}

	[Fact]
	public async Task Blarggs_InstrTiming()
	{
		const string url = "https://github.com/retrio/gb-test-roms/raw/refs/heads/master/instr_timing/instr_timing.gb";
		Assert.True(await ExecuteRom(url, "282B58302BB274D4C91E7CCA3E6CBE6FBA19A4671E13B20CCC6D745B0D219683"));
	}

	[Fact]
	public async Task Dmg_Acid2()
	{
		const string url = "https://github.com/mattcurrie/dmg-acid2/releases/download/v1.0/dmg-acid2.gb";
		Assert.True(await ExecuteRom(url, "F844EA760A6F1FE137F7F992C7AB1C72D34C7FCD3A807B4174A78EB04A32A458"));
	}

	private static async Task<bool> ExecuteRom(string url, string expectedHash)
	{
		const int TimeoutCycles = 100_000_000;
		const int VerifyInterval = 1_000_000;

		var cpu = new Cpu(await s_http.GetByteArrayAsync(url));
		var ppu = new Ppu(cpu);
		var timer = new Timer(cpu);

		var totalCycles = 0;
		var cyclesUntilNextVerification = VerifyInterval;
		while (true)
		{
			var cycles = cpu.ExecuteSingleStep();
			timer.ExecuteSingleStep(cycles);
			ppu.ExecuteSingleStep(cycles);

			totalCycles += cycles;
			if (totalCycles > TimeoutCycles)
				return false;

			cyclesUntilNextVerification -= cycles;
			if (cyclesUntilNextVerification <= 0)
			{
				cyclesUntilNextVerification += VerifyInterval;
				var span = MemoryMarshal.CreateReadOnlySpan(ref ppu.FrameBuffer[0, 0], ppu.FrameBuffer.Length);
				var actualHash = Convert.ToHexString(SHA256.HashData(span));
				if (actualHash == expectedHash)
					return true;
			}
		}
	}
}
