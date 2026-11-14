// Stand-in emulator for automated launch tests.
// Usage: FakeEmulator --record OUT.json [--exit-code N] [--sleep SECONDS] -g GAME
using System.Text.Json;

string? record = null;
int exitCode = 0;
double sleep = 0;
var rest = new List<string>();
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--record": record = args[++i]; break;
        case "--exit-code": exitCode = int.Parse(args[++i]); break;
        case "--sleep": sleep = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
        default: rest.Add(args[i]); break;
    }
}
int g = rest.IndexOf("-g");
string? game = g >= 0 && g + 1 < rest.Count ? rest[g + 1] : null;
if (record != null)
{
    File.WriteAllText(record, JsonSerializer.Serialize(new
    {
        args = rest,
        game,
        game_exists = game != null && File.Exists(game),
        cwd = Environment.CurrentDirectory,
    }));
}
Thread.Sleep(TimeSpan.FromSeconds(sleep));
return exitCode;
