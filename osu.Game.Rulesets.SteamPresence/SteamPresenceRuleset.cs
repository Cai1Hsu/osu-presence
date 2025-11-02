using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.UI;
using osuTK;

namespace osu.Game.Rulesets.SteamPresence;

public class SteamPresenceRuleset : Ruleset
{
    public override string Description => "Add steam presence to your osu! experience";

    public override string ShortName => "Steam";

    public override IBeatmapConverter CreateBeatmapConverter(IBeatmap beatmap)
        =>  new PresenceBeatmapConverter(beatmap, this);

    public override DifficultyCalculator CreateDifficultyCalculator(IWorkingBeatmap beatmap)
        => new PresenceDifficultyCalculator(RulesetInfo, beatmap);

    public override DrawableRuleset CreateDrawableRulesetWith(IBeatmap beatmap, IReadOnlyList<Mod>? mods = null)
        => throw new NotImplementedException();

    public override IEnumerable<Mod> GetModsFor(ModType type)
        => Array.Empty<Mod>();

    public override Drawable CreateIcon() => new OsuHookDrawable
    {
        Content = new SpriteIcon
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Icon = FontAwesome.Brands.SteamSymbol,
            Size = new Vector2(20)
        },
        RelativeSizeAxes = Axes.Both,
    };
}
