using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Logging;

namespace osu.Game.Rulesets.SteamPresence;

public partial class OsuHookDrawable : CompositeDrawable
{
    [BackgroundDependencyLoader]
    private void load(OsuGame? game)
    {
        if (game is null)
        {
            Logger.Log($"OsuGame instance is null in {nameof(OsuHookDrawable)} load.", LoggingTarget.Runtime, LogLevel.Important);
            return;
        }

        try
        {
            var presenceProvider = new PresenceProvider();

            Scheduler.Add(() => game.Add(presenceProvider));
        }
        catch (Exception e)
        {
            Logger.Log(e.Message, LoggingTarget.Runtime, LogLevel.Error);
        }
    }

    private Drawable? content;

    public Drawable? Content
    {
        get => content;
        set
        {
            if (content == value)
                return;

            content = value;

            if (content is null)
                ClearInternal(false);
            else
                InternalChild = content;
        }
    }
}