using osu.Game.Configuration;
using osu.Game.Rulesets.Configuration;

namespace osu.Game.Rulesets.SteamPresence;

public partial class PresenceConfigManager : RulesetConfigManager<PresenceRulesetSettings>
{
    public PresenceConfigManager(SettingsStore store, RulesetInfo ruleset)
        : base(store, ruleset, null)
    {
    }

    protected override void InitialiseDefaults()
    {
        base.InitialiseDefaults();

        SetDefault(PresenceRulesetSettings.AutoStart, true);
        SetDefault(PresenceRulesetSettings.RetrySteamConnection, true);
    }
}
