using System.Diagnostics;
using osu.Framework.Allocation;
using osu.Framework.Graphics;

namespace osu.Game.Rulesets.SteamPresence;

public static class Helper
{
    public static void InjectDependency<T>(this OsuGame game, out T? instance, Func<T> createInstance)
        where T : Drawable
    {
        if (game.Dependencies.TryGet<T>(out instance))
            return;

        var dependencies = game.Dependencies as DependencyContainer;

        Debug.Assert(dependencies != null);

        instance = createInstance != null ? createInstance()
            : throw new InvalidOperationException($"No existing instance of {typeof(T)} found and no {nameof(createInstance)} provided to create one.");

        game.Add(instance);
        dependencies.CacheAs(instance);
    }
}
