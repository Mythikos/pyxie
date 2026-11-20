using System.Runtime.InteropServices;

using Microsoft.Win32.SafeHandles;

namespace Pyxie.Runtime.Media;

/// <summary>Opens only a single-link regular file beneath the sandbox's publication directory. Linux opens nonblocking so a pipe cannot hang the host.</summary>
internal static partial class MediaSource
{
	internal static FileStream Open(string root, string relative)
	{
		string[] parts = relative.Split('/');
		if (parts.Length < 2 || parts[0] != "files" || parts.Any(part => !MediaContent.ValidName(part) || part.EndsWith(' ') || part.EndsWith('.')))
		{
			throw new IOException("The publication path must stay under files/.");
		}
		CheckPath(root);
		string path = root;
		foreach (string part in parts)
		{
			path = Path.Combine(path, part);
			CheckPath(path);
		}
		return OpenRegular(path);
	}

	internal static FileStream OpenRegular(string path)
	{
		CheckPath(path);
		SafeFileHandle handle;
		if (OperatingSystem.IsLinux())
		{
			int descriptor = OpenNative(path, 0x800 | 0x20000 | 0x80000);
			if (descriptor < 0)
			{
				throw new IOException("The publication could not be opened.");
			}
			handle = new SafeFileHandle(descriptor, ownsHandle: true);
			if (StatNative(descriptor, "", 0x1000, 0x7ff, out MediaUnixStat stat) != 0 || (stat.Mode & 0xf000) != 0x8000 || stat.Links != 1)
			{
				handle.Dispose();
				throw new IOException("Only regular files without links can be attached.");
			}
		}
		else
		{
			handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read);
			if (OperatingSystem.IsWindows() && (!GetFileInformationByHandle(handle, out MediaWindowsStat stat) || stat.Links != 1 || (stat.Attributes & 0x410) != 0))
			{
				handle.Dispose();
				throw new IOException("Only regular files without links can be attached.");
			}
		}
		return new FileStream(handle, FileAccess.Read);
	}

	internal static void CheckPath(string path)
	{
		FileAttributes attributes = File.GetAttributes(path);
		if ((attributes & FileAttributes.ReparsePoint) != 0)
		{
			throw new IOException("Links cannot be attached.");
		}
	}

	[LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
	private static partial int OpenNative(string path, int flags);
	[LibraryImport("libc", EntryPoint = "statx", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
	private static partial int StatNative(int descriptor, string path, int flags, uint mask, out MediaUnixStat stat);
	[LibraryImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool GetFileInformationByHandle(SafeFileHandle handle, out MediaWindowsStat stat);
}
