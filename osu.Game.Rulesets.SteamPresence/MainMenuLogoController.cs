using System.Diagnostics;
using System.Runtime.CompilerServices;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Threading;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Input;
using osu.Game.Screens.Menu;
using osuTK;

namespace osu.Game.Rulesets.SteamPresence;

public partial class MainMenuLogoController : CompositeDrawable
{
    [Resolved]
    private OsuGame game { get; set; } = null!;

    [Resolved]
    private OsuLogo? osuLogo { get; set; } = null!;

    [Resolved]
    private IdleTracker? idleTracker { get; set; } = null!;

    [Resolved]
    private IBindable<WorkingBeatmap> beatmap { get; set; } = null!;

    private TrackMetadataPanel trackMetadataPanel = null!;

    // FIXME: use relative sizing and anchoring for margin, same for other size related values.
    private const float margin = 20f;

    private ButtonSystem? buttonSystem;

    [BackgroundDependencyLoader]
    private void load()
    {
        RelativeSizeAxes = Axes.Both;

        InternalChildren = new Drawable[]
        {
            trackMetadataPanel = new TrackMetadataPanel
            {
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft,
                Margin = new MarginPadding(margin),
                Alpha = 0,
            }
        };

        beatmap.BindValueChanged(beatmapChanged, true);

        idleTracker?.IsIdle.BindValueChanged(v =>
        {
            if (v.NewValue)
            {
                // TODO: Only do this when main menu background is beatmap's or Storyboard/Video?
                // if (menuBackgroundSource.Value is
                //     BackgroundSource.Beatmap or
                //     BackgroundSource.BeatmapWithStoryboard)
                Scheduler.Add(() => preparePlayer(ActiveState.ActiveByIdle));
            }
            else
            {
                Scheduler.Add(restoreMenu);
            }
        });
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        if (Parent is MainMenu mainMenu)
            buttonSystem = GetMainMenuButtonSystem(mainMenu);
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "Buttons")]
    private static extern ref ButtonSystem GetMainMenuButtonSystem(MainMenu mainMenu);

    public override bool HandlePositionalInput => true;

    protected override bool OnMouseMove(MouseMoveEvent e)
    {
        const float edge_threshold = 5f;

        if (buttonSystem is null || 
            buttonSystem.State is ButtonSystemState.Initial or ButtonSystemState.Exit)
        {
            var parentBottomRight = Parent.ScreenSpaceDrawQuad.BottomRight;
            var mousePos = e.ScreenSpaceMousePosition;

            // if the mouse move's outside of the screen
            // active track info presentation immediately

            // since the game may be in a scale container,
            // mouse position can be outside of the screen bounds
            if (mousePos.X >= parentBottomRight.X - edge_threshold ||
                mousePos.Y >= parentBottomRight.Y - edge_threshold)
            {
                // FIXME: we should also update ButtonSystem's state to hide everything
                preparePlayer(ActiveState.ActiveByMouseMoveOut);
            }
            else if (activeState is ActiveState.ActiveByMouseMoveOut)
            {
                restoreMenu();
            }
        }

        return base.OnMouseMove(e);
    }

    private static string unicodeOrRomaji(string unicode, string romaji) => string.IsNullOrEmpty(unicode) ? romaji : unicode;

    private static bool isBeatmapNonSelected(WorkingBeatmap beatmap)
        // FIXME: This may not cover all non-selected cases.
        => !beatmap.BeatmapSetInfo.IsManaged && beatmap.BeatmapSetInfo.Beatmaps.Count is 0;

    private void beatmapChanged(ValueChangedEvent<WorkingBeatmap> @event)
    {
        var beatmap = @event.NewValue;

        Scheduler.Add(() =>
        {
            var metadata = beatmap.BeatmapInfo.Metadata;

            bool isNonSelected = isBeatmapNonSelected(beatmap);
            var cover = beatmap.GetBackground();

            void updateInfo()
            {
                if (isNonSelected)
                {
                    trackMetadataPanel.Title.Text = "No Beatmap Selected";
                    trackMetadataPanel.Artist.Text = "Unknown Artist";
                    trackMetadataPanel.Source.Text = "-";
                }
                else
                {
                    trackMetadataPanel.Title.Text = unicodeOrRomaji(metadata.TitleUnicode, metadata.Title);
                    trackMetadataPanel.Artist.Text = unicodeOrRomaji(metadata.ArtistUnicode, metadata.Artist);
                    trackMetadataPanel.Source.Text = metadata.Source;
                }

                if (cover is not null)
                {
                    trackMetadataPanel.Cover.CoverSprite.Texture = cover;
                    trackMetadataPanel.Cover.FadeIn();
                }
                else
                {
                    trackMetadataPanel.Cover.FadeOut();
                }
            }

            if (activeState is not ActiveState.Inactive)
            {
                trackMetadataPanel
                    .FadeOut(transition_duration, Easing.InQuint)
                    .Then()
                    .Schedule(updateInfo)
                    .FadeIn(transition_duration, Easing.OutQuint);
            }
            else
            {
                updateInfo();

                // not needed, as we keep it transparent when we hide it.
                // keep it hidden off screen
                // trackMetadataPanel.X = -(trackMetadataPanel.Width + margin);
            }
        });
    }

    private ActiveState activeState = ActiveState.Inactive;

    private enum ActiveState
    {
        Inactive = 0,
        Hiding = 1,
        ActiveByMouseMoveOut = 2,
        ActiveByIdle = 3,
    }

    private void presentTrackInfo()
    {
        // FIXME: zero size initially causes MoveToX like no ops.

        // Move in from left and fade in
        trackMetadataPanel
            .MoveToX(0, transition_duration, Easing.OutCubic)
            .FadeIn(transition_duration, Easing.OutQuint);
    }

    private void hideTrackInfo()
    {
        // Move out to left, but don't fade out
        trackMetadataPanel
            // TODO: this doesn't include margin
            // so technically it's not fully out of screen
            .MoveToX(-trackMetadataPanel.Width, transition_duration, Easing.OutCubic)
            .FadeOut(transition_duration, Easing.OutQuint);
    }

    private const double transition_duration = 400;

    private void preparePlayer(ActiveState state)
    {
        var oldState = activeState;
        activeState = (ActiveState)Math.Max((int)activeState, (int)state);

        if (oldState is not ActiveState.Inactive)
            return;

        osuLogo.FadeOut(transition_duration, Easing.OutQuint);
        game.Toolbar.Hide();
        presentTrackInfo();
    }

    private void restoreMenu()
    {
        if (activeState is ActiveState.Inactive)
            return;

        osuLogo.FadeIn(transition_duration, Easing.OutQuint);
        game.Toolbar.Show();
        hideTrackInfo();
        activeState = ActiveState.Inactive;
    }

    private partial class TrackMetadataPanel : CompositeDrawable
    {
        // TODO: Is this nested too much?
        public partial class TrackCover : CompositeDrawable
        {
            private Sprite coverSprite = null!;

            public Sprite CoverSprite => coverSprite;

            public TrackCover()
            {
                RelativeSizeAxes = Axes.Both;
                FillMode = FillMode.Fit;
                Anchor = Anchor.CentreLeft;
                Origin = Anchor.CentreLeft;

                InternalChild = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Masking = true,
                    CornerRadius = 5f,
                    Child = coverSprite = new Sprite
                    {
                        RelativeSizeAxes = Axes.Both,
                        FillMode = FillMode.Fill,
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                    }
                }.WithEffect(new BlurEffect
                {
                    DrawOriginal = true,
                    Colour = Colour4.Black.Opacity(0.7f),
                });
            }
        }

        public OsuSpriteText Title { get; private set; } = null!;
        public OsuSpriteText Artist { get; private set; } = null!;
        public OsuSpriteText Source { get; private set; } = null!;
        public TrackCover Cover { get; private set; } = null!;

        public TrackMetadataPanel()
        {
            AutoSizeAxes = Axes.X;
            RelativeSizeAxes = Axes.Y;
            Height = 0.15f;
            RelativePositionAxes = Axes.Y;

            InternalChildren = new Drawable[]
            {
                new FillFlowContainer
                {
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(10, 0),
                    RelativeSizeAxes = Axes.Y,
                    AutoSizeAxes = Axes.X,
                    Children = new Drawable[]
                    {
                        Cover = new TrackCover(),
                        new FillFlowContainer
                        {
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(0, 2),
                            AutoSizeAxes = Axes.Both,
                            Children = new Drawable[]
                            {
                                Title = new OsuSpriteText()
                                {
                                    Font = OsuFont.GetFont(size: 48, weight: FontWeight.SemiBold),
                                },
                                Artist = new OsuSpriteText()
                                {
                                    Font = OsuFont.GetFont(size: 24),
                                },
                                Source = new OsuSpriteText()
                                {
                                    Font = OsuFont.GetFont(size: 24),
                                },
                            }
                        }
                    }
                }
            };
        }
    }
}
