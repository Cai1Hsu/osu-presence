using System.Runtime.CompilerServices;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Game.Screens;

namespace osu.Game.Rulesets.SteamPresence;

public partial class ScreenStackObserver : Drawable
{
    [Resolved]
    private OsuGame? game { get; set; } = null!;

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "ScreenStack")]
    private static extern ref OsuScreenStack getOsuScreenStack(OsuGame game);

    public OsuScreenStack? ScreenStack
    {
        get
        {
            if (game == null)
                return null;

            return getOsuScreenStack(game);
        }
    }
}
