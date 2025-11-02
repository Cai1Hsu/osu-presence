using osu.Game.Beatmaps;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.SteamPresence;

public partial class PresenceDifficultyCalculator : DifficultyCalculator
{
    public PresenceDifficultyCalculator(IRulesetInfo ruleset, IWorkingBeatmap beatmap)
        : base(ruleset, beatmap)
    {
    }

    protected override DifficultyAttributes CreateDifficultyAttributes(IBeatmap beatmap, Mod[] mods, Skill[] skills, double clockRate)
        => new DifficultyAttributes(mods, 0);

    protected override IEnumerable<DifficultyHitObject> CreateDifficultyHitObjects(IBeatmap beatmap, double clockRate)
        => Array.Empty<DifficultyHitObject>();

    protected override Skill[] CreateSkills(IBeatmap beatmap, Mod[] mods, double clockRate)
        => Array.Empty<Skill>();
}
