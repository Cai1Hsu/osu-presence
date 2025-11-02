using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.UserInterface;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Tests.Visual;
using osuTK;

namespace osu.Game.Rulesets.SteamPresence.Tests;

public partial class TestSceneSteamPresence : OsuTestScene
{
    private SteamConnector connector = null!;

    private FillFlowContainer flowContainer = null!;
    private OsuTextBox textBox = null!;

    [BackgroundDependencyLoader]
    private void load()
    {
        Child = flowContainer = new FillFlowContainer
        {
            RelativeSizeAxes = Axes.Both,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, 10),
            Padding = new MarginPadding(10),
            Children = new Drawable[]
            {
                textBox = new OsuTextBox
                {
                    PlaceholderText = "Enter Steam Rich Presence...",
                    Width = 300,
                    Margin = new MarginPadding { Bottom = 10 },
                },
                new RoundedButton
                {
                    Text = "Set Rich Presence",
                    Width = 200,
                    Action = () => SetPresence(textBox.Text),
                }
            }
        };

        AddStep("create connector", createConnector);
        AddStep("destroy connector", destroyConnector);
    }

    private void SetPresence(string presence)
    {
        if (connector is null)
        {
            flashText("Connector not created!");
            return;
        }
        else if (!connector.IsInitialized)
        {
            flashText("Steam API not initialized!");
            return;
        }

        connector?.SetPresence(presence);
    }

    private void flashText(string message)
    {
        OsuSpriteText text;

        flowContainer.Add(text = new OsuSpriteText
        {
            Text = message,
            Colour = Colour4.White,
            Font = OsuFont.Default.With(size: 20),
            Margin = new MarginPadding { Top = 10 },
        });

        text.FlashColour(Colour4.Red, 1000, Easing.OutQuint).Expire();
    }

    private void createConnector()
    {
        destroyConnector();
        Add(connector = new SteamConnector());
    }

    private void destroyConnector()
    {
        if (connector == null)
            return;

        Remove(connector, true);
        connector = null!;
    }
}