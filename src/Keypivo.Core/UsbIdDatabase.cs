using System.Globalization;

namespace Keypivo.Core;

// usb.ids uses one tab for products, two for interfaces, and separate
// top-level sections for classes and other non-device identifiers.
public sealed class UsbIdDatabase
{
    private readonly Dictionary<ushort, string> _vendors = [];
    private readonly Dictionary<(ushort Vendor, ushort Product), string> _products = [];
    private static readonly Lazy<UsbIdDatabase> Bundled = new(LoadBundled);
    public static UsbIdDatabase Default => Bundled.Value;

    public static UsbIdDatabase Parse(TextReader reader)
    {
        var database = new UsbIdDatabase();
        ushort? vendor = null;
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
            if (line.StartsWith("\t\t", StringComparison.Ordinal)) continue;
            var product = line.StartsWith('\t');
            var entry = product ? line.AsSpan(1) : line.AsSpan();
            if (entry.Length < 6 || !char.IsWhiteSpace(entry[4]) || !TryId(entry[..4], out var id) || entry[5..].Trim().IsEmpty)
            {
                if (!product) vendor = null;
                continue;
            }
            var name = entry[5..].Trim().ToString();
            if (product)
            {
                if (vendor is { } parent) database._products[(parent, id)] = name;
            }
            else
            {
                vendor = id;
                database._vendors[id] = name;
            }
        }
        return database;
    }

    public string? VendorName(string vendorId) =>
        TryId(vendorId, out var vendor) ? _vendors.GetValueOrDefault(vendor) : null;

    public string? ProductName(string vendorId, string productId) =>
        TryId(vendorId, out var vendor) && TryId(productId, out var product)
            ? _products.GetValueOrDefault((vendor, product)) : null;

    private static bool TryId(ReadOnlySpan<char> text, out ushort id)
    {
        id = 0;
        return text.Length == 4 && text.IndexOfAnyExcept("0123456789abcdefABCDEF") < 0 &&
            ushort.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out id);
    }

    private static UsbIdDatabase LoadBundled()
    {
        using var stream = typeof(UsbIdDatabase).Assembly.GetManifestResourceStream("Keypivo.Core.usb.ids")
            ?? throw new InvalidOperationException("The bundled USB ID database is missing.");
        using var reader = new StreamReader(stream);
        return Parse(reader);
    }
}
