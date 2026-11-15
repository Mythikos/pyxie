using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Pyxie.Data.Persistence.Converters;

/// <summary>Stores <see cref="DateTime"/> values as UTC and restores them with <see cref="DateTimeKind.Utc"/>, which providers otherwise read back as unspecified.</summary>
public sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
	/// <summary>Initializes a new instance of the <see cref="UtcDateTimeConverter"/> class.</summary>
	public UtcDateTimeConverter()
		: base(value => value.ToUniversalTime(), value => DateTime.SpecifyKind(value, DateTimeKind.Utc))
	{
	}
}
