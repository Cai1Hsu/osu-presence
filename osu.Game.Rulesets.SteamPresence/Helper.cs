using System.Diagnostics;
using osu.Framework.Allocation;

namespace osu.Game.Rulesets.SteamPresence;

public static class Helper
{
    public static void InjectPresenceProvider(this OsuGame game, out PresenceProvider presenceProvider)
    {
        if (game.Dependencies.TryGet<PresenceProvider>(out presenceProvider))
            return;

        var dependencies = game.Dependencies as DependencyContainer;

        Debug.Assert(dependencies != null);

        presenceProvider = new PresenceProvider();

        game.Add(presenceProvider);
        dependencies.CacheAs(presenceProvider);
    }
}
