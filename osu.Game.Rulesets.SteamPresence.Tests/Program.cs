using osu.Framework;
using osu.Game.Tests;

using (var host = Host.GetSuitableDesktopHost(@"MyApp", new HostOptions()))
{
    host.Run(new OsuTestBrowser());
}
