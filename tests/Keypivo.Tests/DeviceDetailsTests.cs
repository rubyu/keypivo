using Keypivo.Core;

internal static class DeviceDetailsTests
{
    public static IEnumerable<(string Name, Func<Task> Run)> All =>
    [
        ("USB IDs: bundled snapshot resolves vendor and product offline", () =>
        {
            Equal("Yubico.com", UsbIdDatabase.Default.VendorName("1050"));
            Equal("Yubikey 4/5 OTP+U2F+CCID", UsbIdDatabase.Default.ProductName("1050", "0407"));
            Equal("Fry's Electronics", UsbIdDatabase.Default.VendorName("0001"));
            Equal(null, UsbIdDatabase.Default.VendorName("FFFF"));
            return Task.CompletedTask;
        }),
        ("USB IDs: scope products to their vendor and ignore interfaces and other sections", () =>
        {
            using var reader = new StringReader("# Test\n\t0001  Orphan\n00ab  Vendor & A\n\t0001  First product\n\t\t0002  Interface\n00cd  Vendor B\n\t0001  Second product\nC 00  Device class\n\t0002  Subclass\nAT 0100  Audio terminal\n\t0003  Terminal\n00ef  Last vendor\n\t0004  Last product\n");
            var database = UsbIdDatabase.Parse(reader);
            Equal("Vendor & A", database.VendorName("00AB"));
            Equal("First product", database.ProductName("00aB", "0001"));
            Equal("Second product", database.ProductName("00CD", "0001"));
            Equal(null, database.ProductName("00AB", "0002"));
            Equal(null, database.ProductName("00CD", "0002"));
            Equal(null, database.ProductName("00CD", "0003"));
            Equal("Last product", database.ProductName("00EF", "0004"));
            return Task.CompletedTask;
        }),
        ("USB IDs: reject malformed IDs and reset vendor after an invalid header", () =>
        {
            using var reader = new StringReader("0001  Known vendor\n\t0001  Valid\n\t0002  \n\tZZZZ  Bad product\ninvalid header\n\t0003  Orphan\n");
            var database = UsbIdDatabase.Parse(reader);
            foreach (var id in new[] { "", "001", "00001", " 001", "0001\n", "ZZZZ", "0x01" })
            {
                Equal(null, database.VendorName(id));
                Equal(null, database.ProductName("0001", id));
            }
            Equal(null, database.ProductName("0001", "0002"));
            Equal(null, database.ProductName("0001", "0003"));
            Equal("Valid", database.ProductName("0001", "0001"));
            return Task.CompletedTask;
        }),
        ("Details: exact IDs, Windows description, Windows/WSL and sharing are distinct", () =>
        {
            var device = new UsbDevice { InstanceId = @"USB\VID_1050&PID_0407\details", BusId = "4-4", Description = "USB Input Device, Smartcard Reader" };
            var windows = DeviceDetails.From(device);
            Equal("Yubikey 4/5 OTP+U2F+CCID (0407)", windows.Product);
            Equal("Yubico.com (1050)", windows.Vendor);
            Equal(device.Description, windows.Description);
            Equal("Windows", windows.Connection);
            Equal("Not shared", windows.Sharing);
            Equal("4-4", windows.BusId);
            Equal("-", windows.PersistedGuid);
            var id = Guid.Parse("6f9619ff-8b86-4b80-b42d-00c04fc964ff");
            var shared = device with { PersistedGuid = id };
            Equal("Shared", DeviceDetails.From(shared).Sharing);
            Equal(id.ToString("D"), DeviceDetails.From(shared).PersistedGuid);
            Equal("WSL", DeviceDetails.From(shared with { ClientIPAddress = "172.22.0.2" }).Connection);
            Equal("Shared (forced)", DeviceDetails.From(shared with { IsForced = true }).Sharing);
            return Task.CompletedTask;
        }),
        ("Details: unknown names retain IDs and absent device clears all values", () =>
        {
            using var reader = new StringReader("1050  Known vendor\n");
            var database = UsbIdDatabase.Parse(reader);
            var knownVendor = DeviceDetails.From(new() { InstanceId = @"USB\VID_1050&PID_FFEE\test", BusId = "1-2", Description = "" }, database);
            Equal("Unknown product (FFEE)", knownVendor.Product);
            Equal("Known vendor (1050)", knownVendor.Vendor);
            Equal("-", knownVendor.Description);
            var unknown = DeviceDetails.From(new() { InstanceId = @"USB\VID_FFFF&PID_0001\test", BusId = "1-2" }, database);
            Equal("Unknown vendor (FFFF)", unknown.Vendor);
            Equal("Unknown product (0001)", unknown.Product);
            Equal("-", unknown.Description);
            Equal(new DeviceDetails("-", "-", "-", "-", "-", "-", "-"), DeviceDetails.From(null));
            return Task.CompletedTask;
        })
    ];

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}.");
    }
}
