using System.Diagnostics;
using System.Runtime.CompilerServices;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Framework.Threading;
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

        SetDefault(PresenceRulesetSettings.LaunchMode, LaunchMode.AutoStart);
        SetDefault(PresenceRulesetSettings.RetrySteamConnection, true);
    }
}
