using System.Security.Cryptography;
using System.Text.Json;

if (args.Length != 1 || !(args[0].StartsWith("mailto:") || args[0].StartsWith("https://")))
{
    Console.Error.WriteLine("Usage: dotnet run --project inventory/tools/GenerateVapidKeys -- mailto:admin@your-domain.example");
    Console.Error.WriteLine("Run once on a trusted machine. Keep the private key in a secret store, never Git or chat.");
    return 1;
}
using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
var p = key.ExportParameters(true);
static string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, string>
{
    ["VAPID_SUBJECT"] = args[0],
    ["VAPID_PUBLIC_KEY"] = Encode(new byte[] { 4 }.Concat(p.Q.X!).Concat(p.Q.Y!).ToArray()),
    ["VAPID_PRIVATE_KEY"] = Encode(p.D!)
}, new JsonSerializerOptions { WriteIndented = true }));
return 0;
