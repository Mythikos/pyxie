using System.Text;

namespace Pyxie.Runtime.Media;

/// <summary>Conservative preview types established from signatures. Everything else is downloaded as an opaque file.</summary>
public static class MediaContent
{
	/// <summary>Identifies a small allowlist of media signatures without executing or decoding file contents.</summary>
	/// <param name="header">The first bytes of the file.</param>
	/// <returns>A safe media type, or application/octet-stream.</returns>
	public static string Detect(ReadOnlySpan<byte> header)
	{
		if (header.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
		{
			return "image/png";
		}
		if (header.StartsWith(new byte[] { 255, 216, 255 }))
		{
			return "image/jpeg";
		}
		if (header.StartsWith("GIF87a"u8) || header.StartsWith("GIF89a"u8))
		{
			return "image/gif";
		}
		if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8))
		{
			return header.Slice(8, 4).SequenceEqual("WEBP"u8) ? "image/webp" : header.Slice(8, 4).SequenceEqual("WAVE"u8) ? "audio/wav" : "application/octet-stream";
		}
		if (header.Length >= 12 && header.Slice(4, 4).SequenceEqual("ftyp"u8) && (header.Slice(8, 4).SequenceEqual("isom"u8) || header.Slice(8, 4).SequenceEqual("mp42"u8) || header.Slice(8, 4).SequenceEqual("mp41"u8)))
		{
			return "video/mp4";
		}
		if (header.StartsWith(new byte[] { 26, 69, 223, 163 }) && Encoding.ASCII.GetString(header).Contains("webm", StringComparison.Ordinal))
		{
			return "video/webm";
		}
		return "application/octet-stream";
	}

	/// <summary>Checks a display filename independently of either operating system's path syntax.</summary>
	/// <param name="name">The proposed name.</param>
	/// <returns>Whether it is a bounded, printable filename.</returns>
	public static bool ValidName(string? name) => !string.IsNullOrWhiteSpace(name) && name.Length <= Data.Media.MediaLimits.MaxNameLength && !name.EndsWith('.') && !name.EndsWith(' ') && !name.Any(character => char.IsControl(character) || character is '/' or '\\' or ':' or '<' or '>' or '"' or '|' or '?' or '*');
}
