namespace ChudLink.Host;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var address = args.Length > 0 ? args[0] : "127.0.0.1";
        await using var host = new HostRuntime(); await host.StartAsync(address);
        Console.WriteLine($"ChudLink 0.1.0 Alpha\n{host.Address}\nSHA256 {host.Fingerprint}\nCommands: pair, pending, approve <ticket>, deny <ticket>, devices, revoke <id>, quit");
        while (Console.ReadLine() is { } line)
        {
            var cmd = line.Split(' ', 2);
            try
            {
                switch (cmd[0])
                {
                    case "pair": Console.WriteLine(host.NewPairingCode()); break;
                    case "pending": foreach (var p in host.Pairings) Console.WriteLine($"{p.Key} {p.Value}"); break;
                    case "approve" when cmd.Length > 1: host.Approve(cmd[1], true); break;
                    case "deny" when cmd.Length > 1: host.Approve(cmd[1], false); break;
                    case "revoke" when cmd.Length > 1: host.Revoke(cmd[1]); break;
                    case "devices": foreach (var d in host.Devices()) Console.WriteLine(d.Id + " " + d); break;
                    case "quit": return;
                }
            }
            catch (Exception ex) { Console.WriteLine(ex.Message); }
        }
    }
}
