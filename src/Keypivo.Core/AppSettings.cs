using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Keypivo.Core;

public enum DisplayPosition { BottomRight, Center }

public sealed partial record DeviceFilter
{
    public string VendorId { get; }
    public string ProductId { get; }
    public static DeviceFilter Default { get; } = new("1050", "");

    public DeviceFilter(string vendorId, string? productId = null)
    {
        VendorId = (vendorId ?? "").Trim().ToUpperInvariant();
        ProductId = (productId ?? "").Trim().ToUpperInvariant();
        if (!HexIdPattern().IsMatch(VendorId))
            throw new ArgumentException("VID must contain exactly four hexadecimal digits (0–9, A–F).", nameof(vendorId));
        if (ProductId.Length > 0 && !HexIdPattern().IsMatch(ProductId))
            throw new ArgumentException("PID must be empty or contain exactly four hexadecimal digits (0–9, A–F).", nameof(productId));
    }

    public bool Matches(UsbDevice device) => device.VendorId == VendorId
        && (ProductId.Length == 0 || device.ProductId == ProductId);

    [GeneratedRegex(@"\A[0-9A-F]{4}\z", RegexOptions.CultureInvariant)]
    private static partial Regex HexIdPattern();
}

public sealed record AppSettings
{
    public string VendorId { get; init; } = "1050";
    public string ProductId { get; init; } = "";
    public DisplayPosition Position { get; init; } = DisplayPosition.BottomRight;
    public NotificationSettings Notifications { get; init; } = new();

    public AppSettings Normalize()
    {
        var filter = new DeviceFilter(VendorId, ProductId);
        if (!Enum.IsDefined(Position)) throw new ArgumentException("Invalid display position.", nameof(Position));
        return this with { VendorId = filter.VendorId, ProductId = filter.ProductId, Notifications = Notifications ?? new() };
    }

    public DeviceFilter ToFilter() => new(VendorId, ProductId);
}

public sealed record SettingsLoadResult(AppSettings Settings, bool RecoveredFromError);

public sealed class SettingsStore(string filePath, string? legacyFilePath = null)
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Keypivo", "settings.json");

    public static SettingsStore CreateDefault() => new(DefaultPath, Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YSwitch", "settings.json"));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter<DisplayPosition>() }
    };

    public SettingsLoadResult Load()
    {
        var notifications = new NotificationSettings();
        try
        {
            // Read earlier releases' settings until the user saves to the new product directory.
            // Never overwrite or delete the previous configuration during an upgrade.
            var readPath = File.Exists(filePath) ? filePath : legacyFilePath;
            if (readPath is null || !File.Exists(readPath)) return new(new(), false);
            var json = File.ReadAllText(readPath);
            // Preserve readable notification choices even if another setting is invalid.
            // A wholly unreadable document still falls back to the documented defaults.
            using (var document = JsonDocument.Parse(json))
            {
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                    foreach (var property in document.RootElement.EnumerateObject())
                        if (property.Name.Equals(nameof(AppSettings.Notifications), StringComparison.OrdinalIgnoreCase))
                            notifications = property.Value.Deserialize<NotificationSettings>(JsonOptions) ?? new();
            }
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                ?? throw new JsonException("Empty settings document.");
            return new(settings.Normalize(), false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            // Leave the original file intact. It is replaced only on an explicit Save.
            return new(new() { Notifications = notifications }, true);
        }
    }

    public void Save(AppSettings settings)
    {
        settings = settings.Normalize();
        var fullPath = Path.GetFullPath(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporaryPath); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }
}
