using osu.Framework.Allocation;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics;
using osu.Game.Screens.Footer;

namespace osu.Game.Rulesets.SteamPresence;

public partial class OpenInStableFooterButton : ScreenFooterButton
{
    public OpenInStableFooterButton()
    {
        Text = "Play in stable";
        Icon = FontAwesome.Solid.PlayCircle;
    }

    [Resolved]
    private OpenInStableAction? openInStable { get; set; } = null!;

    [BackgroundDependencyLoader]
    private void load(OsuColour colours)
    {
        AccentColour = colours.Pink;

        if (openInStable is null)
            return;

        Action = openInStable.Action;
        openInStable.Beatmap.BindValueChanged(_ => updateButtonState(), true);
    }

    private void updateButtonState()
    {
        Enabled.Value = openInStable?.StablePath is not null &&
            openInStable.Beatmap.Value.BeatmapInfo.OnlineID > 0;

        if (openInStable is not null && openInStable.StablePath is null)
            TooltipText = "Stable installation not found, you can set in the first-run setup.";
        else if (openInStable?.Beatmap.Value.BeatmapInfo.OnlineID <= 0)
            TooltipText = "No beatmap selected or the beatmap is not valid online.";

        if (Enabled.Value)
            TooltipText = "Open the current beatmap in osu!stable via osu!direct protocol.";
    }
}
