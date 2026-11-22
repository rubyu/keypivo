namespace Keypivo.Core;

public sealed record DeviceDetails(string Product, string Vendor, string Description, string Connection,
    string Sharing, string BusId, string PersistedGuid)
{
    public static DeviceDetails From(UsbDevice? device, UsbIdDatabase? database = null)
    {
        if (device is null) return new("—", "—", "—", "—", "—", "—", "—");
        database ??= UsbIdDatabase.Default;
        return new(
            WithId(database.ProductName(device.VendorId, device.ProductId) ?? "Unknown product", device.ProductId),
            WithId(database.VendorName(device.VendorId) ?? "Unknown vendor", device.VendorId),
            string.IsNullOrWhiteSpace(device.Description) ? "—" : device.Description,
            !device.IsConnected ? "Not connected" : device.IsAttached ? "WSL" : "Windows",
            device.IsForced ? "Shared (forced)" : device.IsShared ? "Shared" : "Not shared",
            device.BusId ?? "—", device.PersistedGuid?.ToString("D") ?? "—");
    }

    private static string WithId(string name, string id) => id.Length == 0 ? name : $"{name} ({id})";
}
