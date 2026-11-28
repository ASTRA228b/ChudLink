using ChudLink.Host;

sealed class FakeUsbAdb
{
    public bool Attached = true, Wireless, Mapped, MissingApp;
    public string State = "device";
    public int Opens, Removed;
    string local = "";
    public Task<AdbResult> Run(string[] args, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var text = string.Join(' ', args);
        AdbResult result;
        if (text == "devices -l") result = new(0, "List of devices attached\n" + (Attached ? "TEST_USB " + State + " model:Test\n" : "") + (Wireless ? "192.168.1.2:5555 device model:Wireless\n" : ""));
        else if (text.EndsWith("get-devpath")) result = new(0, "usb:1-1");
        else if (text.EndsWith("reverse --list")) result = new(0, Mapped ? "UsbFfs tcp:47831 " + local : "");
        else if (text.Contains("reverse --no-rebind")) { local = args[^1]; Mapped = true; result = new(0, ""); }
        else if (text.Contains("reverse --remove")) { Mapped = false; Removed++; result = new(0, ""); }
        else if (text.Contains("shell am start")) { Opens++; result = MissingApp ? new(1, "Error: Activity class does not exist") : new(0, "Status: ok"); }
        else throw new InvalidOperationException("Unexpected test ADB command");
        return Task.FromResult(result);
    }
}
