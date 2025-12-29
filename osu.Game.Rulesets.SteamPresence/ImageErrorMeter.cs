using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;
using osuTK;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.IO.Stores;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using osu.Game.Configuration;
using osu.Framework.Bindables;
using osu.Framework.Threading;
using osu.Framework.Graphics.Pooling;
using osu.Game.Screens.Play.HUD.HitErrorMeters;
using System.Diagnostics;
using osu.Framework.Logging;

namespace osu.Game
{
    [Cached]
    public partial class ImageHitErrorMeter : HitErrorMeter
    {
        private Container spriteContainer = null!;

        private readonly Random random = new Random();

        private TextureStore textures = null!;
        private TextureSource? textureSource = null!;

        [SettingSource("Image Directory")]
        public Bindable<string> ImageDirectory { get; } = new Bindable<string>(string.Empty);

        [SettingSource("Combo Limit", "The minimum combo required for an image to be shown on a miss.")]
        public Bindable<int> ComboLimit { get; } = new BindableInt(30)
        {
            MinValue = 0,
            MaxValue = 200,
            Default = 30,
        };

        [SettingSource("Scale")]
        public Bindable<float> ImageScale { get; } = new BindableFloat()
        {
            MinValue = 0.1f,
            MaxValue = 5f,
            // although overridden in LoadComplete, we set a meaningful value here to avoid any potential issues.
            Default = 1f,
            Precision = 0.1f,
        };

        [SettingSource("Always show on combo break", "If enabled, images will also be shown on combo breaks regardless of combo.")]
        public Bindable<bool> AlwaysShowOnComboBreak { get; } = new BindableBool(true);

        [SettingSource("Trigger Mode", "The condition under which an image is shown.")]
        public Bindable<TriggerMode> ImageTriggerMode { get; } = new Bindable<TriggerMode>(TriggerMode.Miss);

        [Resolved]
        private ScoreProcessor scoreProcessor { get; set; } = null!;

        [BackgroundDependencyLoader]
        private void load(TextureStore textures)
        {
            this.textures = textures;

            RelativeSizeAxes = Axes.None;
            // Arbitrary size, non-zero to allow user selection in editor.
            Size = new Vector2(400, 80);

            AddInternal(spriteContainer = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Anchor = Anchor.BottomCentre,
                Origin = Anchor.BottomCentre,
            });

            AddInternal(spritePool);

            ImageDirectory.BindValueChanged(v =>
            {
                refreshTask?.Cancel();

                // TODO: maybe offload to thread pool as this action could be heavy?
                refreshTask = Schedule(() =>
                {
                    refreshTextureSource();
                    refreshTask = null;
                });
            }, false);

            // load source here to avoid blocking upload thread.
            refreshTextureSource();
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            ImageScale.Value = Scale.X; // assume uniform scale
            ImageScale.Default = ImageScale.Value;

            ImageScale.BindValueChanged(scale =>
            {
                Scale = new Vector2(scale.NewValue);
            }, true);
        }

        private ScheduledDelegate? refreshTask;

        private void disposeExistingSource(bool disposing = false)
        {
            if (textureSource is null)
                return;

            if (!disposing)
                // existing sprites may reference disposed textures
                Clear();

            textures.RemoveTextureStore(textureSource);
            textureSource.Dispose();
            textureSource = null;
        }

        private void refreshTextureSource()
        {
            disposeExistingSource();

            string path = ImageDirectory.Value.Trim();

            if (!Directory.Exists(path))
                return;

            string[] images = Directory.GetFiles(path);

            textureSource = new TextureSource(images);
            textures.AddTextureSource(textureSource);
        }

        public override void Clear() => spriteContainer.Clear(false);

        private readonly DrawablePool<PooledSprite> spritePool = new DrawablePool<PooledSprite>(10);

        private partial class PooledSprite : PoolableDrawable
        {
            public Sprite Sprite { get; private set; } = null!;

            [BackgroundDependencyLoader]
            private void load()
            {
                InternalChild = Sprite = new Sprite()
                {
                    Anchor = Anchor.BottomCentre,
                    Origin = Anchor.BottomCentre,
                };

                RelativeSizeAxes = Axes.Both;
            }

            protected override void FreeAfterUse()
            {
                base.FreeAfterUse();

                Sprite.Texture = null;
            }
        }

        private JudgementResult? lastMiss = null;

        public enum TriggerMode
        {
            Miss,
            ComboMilestone,
        }

        private static bool isComboMilestone(int combo) => combo is 30 or 60 || (combo >= 100 && combo % 50 == 0);

        private bool isMissResult(JudgementResult judgement) =>
            judgement.Type is HitResult.Miss or
                HitResult.ComboBreak or
                HitResult.IgnoreMiss or
                HitResult.LargeTickMiss or
                HitResult.SmallTickMiss &&
            judgement.ComboAfterJudgement is 0 &&
            judgement.ComboAtJudgement >= ComboLimit.Value;

        private bool shouldTrigger(JudgementResult judgement)
        {
            switch (ImageTriggerMode.Value)
            {
                case TriggerMode.Miss:
                    return isMissResult(judgement);
                case TriggerMode.ComboMilestone:
                    return isComboMilestone(judgement.ComboAfterJudgement) &&
                        judgement.ComboAfterJudgement > judgement.ComboAtJudgement;
                default:
                    return false;
            }
        }

        protected override void OnNewJudgement(JudgementResult judgement)
        {
            if (textureSource is null || textureSource.Count == 0)
                return;

            if (!shouldTrigger(judgement))
                return;

            int id = random.Next(0, textureSource.Count);

            try
            {
                // FIXME: image source should also be taken into account for uniqueness
                var texture = textures.Get(id.ToString());

                var pooled = spritePool.Get();

                if (pooled is null)
                    return;

                pooled.Sprite.Texture = texture;

                spriteContainer.Add(pooled);

                // move in from bottom and then fade out
                pooled.MoveToY(pooled.Sprite.Height)
                    .FadeInFromZero(500)
                    .MoveToY(0, 500, Easing.OutCubic)
                    .Then()
                    .FadeOut(500)
                    .Expire();
            }
            catch (Exception e)
            {
                Logger.Log($"Failed to load texture: {e.Message}", LoggingTarget.Runtime, LogLevel.Error);
            }
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            if (isDisposing)
            {
                disposeExistingSource(true);
            }
        }

        private partial class TextureSource : IResourceStore<TextureUpload>
        {
            private readonly string[] textures_files;

            private readonly Image<Rgba32>?[] textures;

            private string[]? available_resources_cache = null;

            public int Count => textures_files.Length;

            public TextureSource(string[] files)
            {
                textures_files = files;

                textures = new Image<Rgba32>?[Count];

                for (int i = 0; i < Count; i++)
                    Load(i);
            }

            public void Load(int index)
            {
                if (index < 0 || index >= Count)
                    return;

                if (textures[index] is not null)
                    return;

                string file_path = textures_files[index];

                using (var fs = new FileStream(file_path, FileMode.Open, FileAccess.Read))
                {
                    textures[index] = SixLabors.ImageSharp.Image.Load<Rgba32>(fs);
                }
            }

            public TextureUpload Get(string name)
            {
                if (!int.TryParse(name, out int index) || index < 0 || index >= textures_files.Length)
                    throw new FileNotFoundException($"Texture with name {name} not found.");

                Load(index);

                var texture = textures[index];

                Debug.Assert(texture is not null);

                return new TextureUpload(texture);
            }

            public async Task<TextureUpload> GetAsync(string name, CancellationToken cancellationToken = default)
            {
                if (!int.TryParse(name, out int index) || index < 0 || index >= textures_files.Length)
                    throw new FileNotFoundException($"Texture with name {name} not found.");

                if (textures[index] is not null)
                    return new TextureUpload(textures[index]!);

                string file_path = textures_files[index];
                using (var fs = new FileStream(file_path, FileMode.Open, FileAccess.Read))
                {
                    var image = await SixLabors.ImageSharp.Image.LoadAsync<Rgba32>(fs, cancellationToken)
                        .ConfigureAwait(false);

                    textures[index] = image;

                    return new TextureUpload(image);
                }
            }

            public IEnumerable<string> GetAvailableResources()
                => available_resources_cache ??= Enumerable.Range(0, textures_files.Length)
                    .Select(i => i.ToString())
                    .ToArray();

            public Stream GetStream(string name)
            {
                if (!int.TryParse(name, out int index) || index < 0 || index >= textures_files.Length)
                    throw new FileNotFoundException($"Texture with name {name} not found.");

                string file_path = textures_files[index];
                return new FileStream(file_path, FileMode.Open, FileAccess.Read);
            }

            public void Dispose()
            {
                foreach (var texture in textures)
                    texture?.Dispose();
            }
        }
    }
}
