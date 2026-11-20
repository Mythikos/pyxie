using System.Runtime.InteropServices;

namespace Pyxie.Runtime.Media;

[StructLayout(LayoutKind.Explicit, Size = 52)]
internal struct MediaWindowsStat
{
	[FieldOffset(0)]
	internal uint Attributes;
	[FieldOffset(40)]
	internal uint Links;
}
