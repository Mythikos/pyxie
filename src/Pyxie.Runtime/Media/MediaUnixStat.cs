using System.Runtime.InteropServices;

namespace Pyxie.Runtime.Media;

[StructLayout(LayoutKind.Explicit, Size = 256)]
internal struct MediaUnixStat
{
	[FieldOffset(16)]
	internal uint Links;
	[FieldOffset(28)]
	internal ushort Mode;
}
