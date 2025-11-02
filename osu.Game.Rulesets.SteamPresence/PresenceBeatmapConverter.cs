using osu.Game.Beatmaps;
using osu.Game.Rulesets.Objects;

namespace osu.Game.Rulesets.SteamPresence;

public class PresenceBeatmapConverter : BeatmapConverter<HitObject>
{
    public PresenceBeatmapConverter(IBeatmap beatmap, Ruleset ruleset)
        : base(beatmap, ruleset)
    {
    }

    public override bool CanConvert() => true;
}