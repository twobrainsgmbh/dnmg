namespace DNMG.App.Avalonia.Models;

internal interface IColorPalette
{
	/// <summary>
	/// The first color (brightest one)
	/// </summary>
	uint Color1 { get; }

	/// <summary>
	/// The second color (second-brightest one)
	/// </summary>
	uint Color2 { get; }

	/// <summary>
	/// The third color (second-darkest one)
	/// </summary>
	uint Color3 { get; }

	/// <summary>
	/// The forth color (darkest one)
	/// </summary>
	uint Color4 { get; }
}
