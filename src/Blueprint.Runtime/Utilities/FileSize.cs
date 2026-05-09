using System.Globalization;

namespace Blueprint.Utilities;

/// <summary>
/// Represents the size of a file in bytes, providing formatting methods to convert
/// to an appropriate string representation in various unit sizes (B, KB, MB, GB, TB, PB).
/// </summary>
/// <remarks>
/// Use the static factory methods (<see cref="FromBytes"/>, <see cref="FromKilobytes"/>, etc.)
/// to create instances. The format string accepted by <see cref="ToString(string, IFormatProvider)"/>
/// supports either a unit suffix alone (<c>"MB"</c>) or a number format + unit (<c>"N4:GB"</c>).
/// The special format <c>"FS"</c> selects the largest appropriate unit automatically.
/// </remarks>
public readonly struct FileSize : IFormattable, IEquatable<FileSize>
{
    private const string FileSystemFormatSpecifier = "FS";

    private static readonly UnitSize _byteUnit = new(Math.Pow(2, 0), "B", "N0");
    private static readonly UnitSize _kilobyteUnit = new(Math.Pow(2, 10), "KB", "N0");
    private static readonly UnitSize _megabyteUnit = new(Math.Pow(2, 20), "MB", "N2");
    private static readonly UnitSize _gigabyteUnit = new(Math.Pow(2, 30), "GB", "N2");
    private static readonly UnitSize _terabyteUnit = new(Math.Pow(2, 40), "TB", "N2");
    private static readonly UnitSize _petabyteUnit = new(Math.Pow(2, 50), "PB", "N2");

    private static readonly UnitSize[] _allUnits =
    [
        _byteUnit,
        _kilobyteUnit,
        _megabyteUnit,
        _gigabyteUnit,
        _terabyteUnit,
        _petabyteUnit,
    ];

    private readonly UnitSize _unitSize;
    private readonly double _units;

    private FileSize(UnitSize unitSize, double units)
    {
        this._unitSize = unitSize;
        this._units = units;
    }

    /// <summary>
    /// Gets the number of bytes this <see cref="FileSize" /> represents.
    /// </summary>
    public double Bytes => this._unitSize.ConvertTo(_byteUnit, this._units);

    /// <summary>
    /// Gets the number of kilobytes this <see cref="FileSize" /> represents.
    /// </summary>
    public double Kilobytes => this._unitSize.ConvertTo(_kilobyteUnit, this._units);

    /// <summary>
    /// Gets the number of megabytes this <see cref="FileSize" /> represents.
    /// </summary>
    public double Megabytes => this._unitSize.ConvertTo(_megabyteUnit, this._units);

    /// <summary>
    /// Gets the number of gigabytes this <see cref="FileSize" /> represents.
    /// </summary>
    public double Gigabytes => this._unitSize.ConvertTo(_gigabyteUnit, this._units);

    /// <summary>
    /// Gets the number of terabytes this <see cref="FileSize" /> represents.
    /// </summary>
    public double Terabytes => this._unitSize.ConvertTo(_terabyteUnit, this._units);

    /// <summary>
    /// Gets the number of petabytes this <see cref="FileSize" /> represents.
    /// </summary>
    public double Petabytes => this._unitSize.ConvertTo(_petabyteUnit, this._units);

    /// <summary>
    /// Determines whether two <see cref="FileSize"/> values represent the same byte count.
    /// </summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><c>true</c> if the byte counts are equal.</returns>
    public static bool operator ==(FileSize left, FileSize right)
    {
        return left.Bytes == right.Bytes;
    }

    /// <summary>
    /// Determines whether two <see cref="FileSize"/> values represent different byte counts.
    /// </summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><c>true</c> if the byte counts differ.</returns>
    public static bool operator !=(FileSize left, FileSize right)
    {
        return !(left == right);
    }

    /// <summary>
    /// Creates a new <see cref="FileSize"/> representing the given number of bytes.
    /// </summary>
    /// <param name="bytes">The number of bytes.</param>
    /// <returns>A new <see cref="FileSize"/> instance.</returns>
    public static FileSize FromBytes(double bytes)
    {
        Guard.GreaterThanOrEqual(bytes, 0.0);
        return new FileSize(_byteUnit, bytes);
    }

    /// <summary>
    /// Creates a new <see cref="FileSize"/> representing the given number of kilobytes.
    /// </summary>
    /// <param name="kilobytes">The number of kilobytes.</param>
    /// <returns>A new <see cref="FileSize"/> instance.</returns>
    public static FileSize FromKilobytes(double kilobytes)
    {
        Guard.GreaterThanOrEqual(kilobytes, 0.0);
        return new FileSize(_kilobyteUnit, kilobytes);
    }

    /// <summary>
    /// Creates a new <see cref="FileSize"/> representing the given number of megabytes.
    /// </summary>
    /// <param name="megabytes">The number of megabytes.</param>
    /// <returns>A new <see cref="FileSize"/> instance.</returns>
    public static FileSize FromMegabytes(double megabytes)
    {
        Guard.GreaterThanOrEqual(megabytes, 0.0);
        return new FileSize(_megabyteUnit, megabytes);
    }

    /// <summary>
    /// Creates a new <see cref="FileSize"/> representing the given number of gigabytes.
    /// </summary>
    /// <param name="gigabytes">The number of gigabytes.</param>
    /// <returns>A new <see cref="FileSize"/> instance.</returns>
    public static FileSize FromGigabytes(double gigabytes)
    {
        Guard.GreaterThanOrEqual(gigabytes, 0.0);
        return new FileSize(_gigabyteUnit, gigabytes);
    }

    /// <summary>
    /// Creates a new <see cref="FileSize"/> representing the given number of terabytes.
    /// </summary>
    /// <param name="terabytes">The number of terabytes.</param>
    /// <returns>A new <see cref="FileSize"/> instance.</returns>
    public static FileSize FromTerabytes(double terabytes)
    {
        Guard.GreaterThanOrEqual(terabytes, 0.0);
        return new FileSize(_terabyteUnit, terabytes);
    }

    /// <summary>
    /// Creates a new <see cref="FileSize"/> representing the given number of petabytes.
    /// </summary>
    /// <param name="petabytes">The number of petabytes.</param>
    /// <returns>A new <see cref="FileSize"/> instance.</returns>
    public static FileSize FromPetabytes(double petabytes)
    {
        Guard.GreaterThanOrEqual(petabytes, 0.0);
        return new FileSize(_petabyteUnit, petabytes);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is FileSize other && this.Equals(other);
    }

    /// <inheritdoc />
    public bool Equals(FileSize other)
    {
        return this.Bytes == other.Bytes;
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return this.Bytes.GetHashCode();
    }

    /// <summary>
    /// Returns a string using the <c>"FS"</c> format that selects the largest
    /// appropriate unit (e.g. 1023B → "1023B", 1024B → "1KB").
    /// </summary>
    /// <returns>A human-readable file size string.</returns>
    public override string ToString()
    {
        return this.ToString(FileSystemFormatSpecifier, null);
    }

    /// <summary>
    /// Returns a formatted string representation.
    /// </summary>
    /// <remarks>
    /// The format modifier is either a unit suffix alone (<c>"B"</c>, <c>"KB"</c>, <c>"MB"</c>,
    /// <c>"GB"</c>, <c>"TB"</c>, <c>"PB"</c>, or <c>"FS"</c>) or a number format and unit separated
    /// by a colon (<c>"N4:GB"</c>).
    /// </remarks>
    /// <param name="format">The format specifier.</param>
    /// <param name="formatProvider">An optional format provider.</param>
    /// <returns>A formatted file size string.</returns>
    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        if (formatProvider?.GetFormat(typeof(FileSize)) is ICustomFormatter formatter)
        {
            return formatter.Format(format, this, formatProvider);
        }

        return GetUnitSizeForFormat(format, this.Bytes).Format(this.Bytes);
    }

    private static UnitSize GetUnitSizeForFormat(string? format, double sizeInBytes)
    {
        if (format != null && format.Contains(':'))
        {
            var parts = format.Split(':');
            var unit = GetDefaultUnitSizeForFormat(parts[1], sizeInBytes);

            return new UnitSize(unit.UnitValue, unit.Suffix, parts[0]);
        }

        return GetDefaultUnitSizeForFormat(format, sizeInBytes);
    }

    private static UnitSize GetDefaultUnitSizeForFormat(string? unitFormat, double bytes)
    {
        unitFormat = (unitFormat ?? FileSystemFormatSpecifier).ToUpperInvariant();

        if (unitFormat.Equals(FileSystemFormatSpecifier, StringComparison.Ordinal) ||
            !_allUnits.Any(u => u.Suffix == unitFormat))
        {
            // FS: smallest unit where bytes >= that unit. Fall back to bytes for <1B.
            return _allUnits.LastOrDefault(u => bytes >= u.UnitValue) ?? _byteUnit;
        }

        return _allUnits.Single(m => m.Suffix == unitFormat);
    }

    private sealed record UnitSize(double UnitValue, string Suffix, string FormatModifier)
    {
        public string Format(double bytes)
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                "{0:" + this.FormatModifier + "}{1}",
                bytes / this.UnitValue,
                this.Suffix);
        }

        public double ConvertTo(UnitSize target, double units)
        {
            if (target.UnitValue > this.UnitValue)
            {
                return units / (target.UnitValue / this.UnitValue);
            }

            if (target.UnitValue < this.UnitValue)
            {
                return units * (this.UnitValue / target.UnitValue);
            }

            return units;
        }
    }
}
