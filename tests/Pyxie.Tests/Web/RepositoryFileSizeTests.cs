using System.Globalization;

using Pyxie.Web.Components.Repository.Display;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class RepositoryFileSizeTests
{
	[Theory]
	[InlineData(0, "0 B")]
	[InlineData(1, "1 B")]
	[InlineData(1023, "1023 B")]
	[InlineData(1024, "1.0 KB")]
	[InlineData(1536, "1.5 KB")]
	[InlineData(524_288, "512.0 KB")]
	[InlineData(1_048_575, "1.0 MB")]
	[InlineData(1_048_576, "1.0 MB")]
	[InlineData(3_221_225_472, "3.0 GB")]
	[InlineData(1_649_267_441_664, "1.5 TB")]
	[InlineData(-5, "0 B")]
	public void Format_UsesTheLargestUnitThatKeepsTheSizeUnder1024(long bytes, string expected)
	{
		Assert.Equal(expected, FileSize.Format(bytes));
	}

	[Fact]
	public void Format_StaysInTerabytesForHugeSizes()
	{
		Assert.Equal("8388608.0 TB", FileSize.Format(long.MaxValue));
	}

	[Fact]
	public void Format_WritesTheDecimalPointTheSameWayInEveryCulture()
	{
		CultureInfo original = CultureInfo.CurrentCulture;

		try
		{
			CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

			Assert.Equal("1.5 KB", FileSize.Format(1536));
		}
		finally
		{
			CultureInfo.CurrentCulture = original;
		}
	}
}
