using System.Runtime.CompilerServices;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Logging;
using osu.Framework.Testing;
using osu.Framework.Threading;
using osu.Game.Configuration;

namespace osu.Game.Rulesets.SteamPresence;

public partial class OsuHookDrawable : CompositeDrawable
{
    private Ruleset ruleset;

    public OsuHookDrawable(Ruleset ruleset)
    {
        this.ruleset = ruleset;
    }

    [BackgroundDependencyLoader]
    private void load(OsuGame game, IRulesetConfigCache rulesetConfigCache, IBindable<RulesetInfo>? currentRuleset)
    {
        var config = (PresenceConfigManager?)rulesetConfigCache.GetConfigFor(ruleset);

        if (config is null)
        {
            Logger.Log($"config is null", LoggingTarget.Runtime, LogLevel.Important);
            return;
        }

        if (game.ChildrenOfType<PresenceProvider>().Any())
            return;

        var launchMode = config.Get<LaunchMode>(PresenceRulesetSettings.LaunchMode);

        if (launchMode == LaunchMode.AutoStart)
        {
            if (game.ChildrenOfType<PresenceProvider>().Any())
                return;

            // use the game's scheduler to ensure code executed on the update thread
            var scheduler = GetScheduler(game);

            scheduler.Add(() =>
            {
                game.Add(new PresenceProvider());
            });
        }

        if (currentRuleset is Bindable<RulesetInfo> rulesetBindable)
        {
            // Prevent selecting this ruleset
            rulesetBindable.BindValueChanged(v =>
            {
                if (v.NewValue.Equals(ruleset.RulesetInfo))
                {
                    var disabled = rulesetBindable.Disabled;
                    rulesetBindable.Disabled = false;

                    rulesetBindable.Value = v.OldValue;

                    rulesetBindable.Disabled = disabled;
                }
            });
        }
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Scheduler")]
    private extern static Scheduler GetScheduler(Drawable drawable);

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