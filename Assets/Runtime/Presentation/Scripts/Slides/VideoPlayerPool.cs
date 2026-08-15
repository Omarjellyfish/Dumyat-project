using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;

namespace DumyatRuntime
{
    /// <summary>
    /// Hands out a playing <see cref="VideoPlayer"/> and a render texture sized for where it will
    /// actually be drawn, and reuses both across slides.
    ///
    /// WHY THE TEXTURE IS SIZED PER CLIP RATHER THAN ONCE FOR THE DECK:
    /// there used to be one shared 1920x1080 target for every video slide, which is wrong in both
    /// directions at once — a 4K source was thrown away before it reached the screen, and a quarter-
    /// width tile in a 2x2 grid still paid for a full 1080p buffer. Sizing from the clip AND from the
    /// on-screen width fixes both: nothing is upscaled from a smaller buffer than it is shown at, and
    /// nothing allocates more than its source contains.
    ///
    /// WHY EACH PLAYER GETS ITS OWN CHILD GAMEOBJECT:
    /// several clips on one slide means several players, and giving each its own object keeps their
    /// lifetimes independent and legible in the hierarchy while a slide is up.
    ///
    /// THE COST THIS DOES NOT REMOVE: one decoder per playing clip. Four simultaneous 1080p streams is
    /// about the ceiling on a presenting laptop, which is why the grid caps at six and the deck
    /// validator warns past four — the memory is cheap, the decoding is not.
    /// </summary>
    public sealed class VideoPlayerPool
    {
        private sealed class Slot
        {
            public GameObject Host;
            public VideoPlayer Player;
            public RenderTexture Target;
            public bool InUse;
        }

        private readonly Transform _parent;
        private readonly List<Slot> _slots = new List<Slot>();

        /// <summary>
        /// Textures a slot has stopped using but which may still be referenced by draw commands already
        /// submitted for this frame. They are destroyed on <see cref="Dispose"/> and never before.
        ///
        /// This list is the fix for a hard Editor crash. Releasing a RenderTexture the moment a slot
        /// stopped needing it freed GPU memory that UI Toolkit still had bound as a background image,
        /// and the render thread then crashed registering a shader resource view for it —
        /// TexturesD3D12::AllocateAndCreateSRV, on the GfxDeviceWorker thread, under both the AMD and
        /// NVIDIA drivers. A texture is cheap; a crash mid-presentation is not.
        /// </summary>
        private readonly List<RenderTexture> _retired = new List<RenderTexture>();

        private readonly int _maxWidth;

        public VideoPlayerPool(Transform parent, int maxWidth)
        {
            _parent = parent;
            _maxWidth = Mathf.Clamp(maxWidth, 256, 4096);
        }

        /// <summary>The first claimed player — what the progress bar and the time readout follow.</summary>
        public VideoPlayer Primary
        {
            get
            {
                foreach (Slot s in _slots)
                    if (s.InUse && s.Player != null && s.Player.clip != null) return s.Player;
                return null;
            }
        }

        public bool AnyPlaying
        {
            get
            {
                foreach (Slot s in _slots)
                    if (s.InUse && s.Player != null && s.Player.isPlaying) return true;
                return false;
            }
        }

        /// <summary>Marks every slot free. Call before laying a slide out, then Acquire per clip.</summary>
        public void BeginSlide()
        {
            foreach (Slot s in _slots) s.InUse = false;
        }

        /// <summary>
        /// Stops and clears every slot not claimed during the slide just laid out. Stopped rather than
        /// paused: a clip left running behind another slide keeps a decoder busy for nothing.
        /// </summary>
        public void EndSlide()
        {
            foreach (Slot s in _slots)
            {
                if (s.InUse || s.Player == null) continue;
                s.Player.Stop();
                s.Player.clip = null;
            }
        }

        /// <summary>
        /// A render texture carrying <paramref name="clip"/>, playing.
        ///
        /// The size comes from the CLIP, never from how wide it happens to be drawn. Sizing it to the
        /// on-screen width meant every navigation between a grid tile and a full-bleed slide reallocated
        /// the texture, which is what opened the use-after-free window described on <see cref="_retired"/>.
        /// Clip-native is also simply better: the GPU downsamples with filtering when the tile is smaller,
        /// whereas a tile-sized buffer throws the source detail away before it ever reaches the screen.
        /// </summary>
        public RenderTexture Acquire(VideoClip clip, bool loop, bool audio, bool autoPlay)
        {
            if (clip == null) return null;

            Slot slot = Claim(clip);
            Vector2Int size = TargetSize(clip);

            if (slot.Target == null || slot.Target.width != size.x || slot.Target.height != size.y)
            {
                // Stop BEFORE the target is taken away. A player left decoding into a texture that is
                // being swapped out is the other half of the same crash.
                if (slot.Player.clip != null) slot.Player.Stop();
                slot.Player.targetTexture = null;

                if (slot.Target != null) _retired.Add(slot.Target);

                slot.Target = new RenderTexture(size.x, size.y, 0, RenderTextureFormat.ARGB32)
                {
                    name = "RT_PresentationVideo_" + clip.name,
                };
                slot.Target.Create();
                slot.Player.targetTexture = slot.Target;
            }

            slot.Player.isLooping = loop;
            slot.Player.audioOutputMode = audio ? VideoAudioOutputMode.Direct : VideoAudioOutputMode.None;

            if (slot.Player.clip != clip)
            {
                slot.Player.Stop();
                slot.Player.clip = clip;
            }

            // Re-Play a slot that is merely idle: a repaint of the same slide must not leave a clip
            // that was already loaded sitting on its first frame.
            if (autoPlay && !slot.Player.isPlaying) slot.Player.Play();

            return slot.Target;
        }

        /// <summary>Prefers a slot already holding this clip, so a repaint does not restart playback.</summary>
        private Slot Claim(VideoClip clip)
        {
            foreach (Slot s in _slots)
            {
                if (s.InUse || s.Player == null || s.Player.clip != clip) continue;
                s.InUse = true;
                return s;
            }

            foreach (Slot s in _slots)
            {
                if (s.InUse) continue;
                s.InUse = true;
                return s;
            }

            var host = new GameObject("PresentationVideo_" + _slots.Count);
            host.transform.SetParent(_parent, false);

            var player = host.AddComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.renderMode = VideoRenderMode.RenderTexture;
            player.skipOnDrop = true;

            var created = new Slot { Host = host, Player = player, InUse = true };
            _slots.Add(created);
            return created;
        }

        /// <summary>
        /// The clip's own resolution, capped. Deterministic for a given clip, which is what makes the
        /// allocation happen once and never again — and a texture that is never reallocated is a texture
        /// that can never be freed while something is still drawing it.
        /// </summary>
        private Vector2Int TargetSize(VideoClip clip)
        {
            int width = clip.width > 0 ? (int)clip.width : 1920;
            width = Mathf.Clamp(Mathf.Min(width, _maxWidth), 256, 4096);

            float aspect = clip.height > 0 ? clip.width / (float)clip.height : 16f / 9f;
            int height = Mathf.Clamp(Mathf.RoundToInt(width / Mathf.Max(aspect, 0.01f)), 144, 4096);

            return new Vector2Int(width, height);
        }

        public void StopAll()
        {
            foreach (Slot s in _slots)
            {
                if (s.Player == null) continue;
                s.Player.Stop();
                s.InUse = false;
            }
        }

        /// <summary>
        /// Pauses or resumes every claimed clip together. On a grid they were started together and
        /// read as one shot, so pausing half of it would be the surprising behaviour.
        /// </summary>
        public void TogglePause()
        {
            bool anyPlaying = AnyPlaying;

            foreach (Slot s in _slots)
            {
                if (!s.InUse || s.Player == null || s.Player.clip == null) continue;
                if (anyPlaying) s.Player.Pause(); else s.Player.Play();
            }
        }

        /// <summary>
        /// Tears the pool down. This is the ONLY place a render texture is released — by now the panel
        /// is going away with it, so nothing can still be drawing one.
        /// </summary>
        public void Dispose()
        {
            foreach (Slot s in _slots)
            {
                if (s.Player != null)
                {
                    s.Player.Stop();
                    s.Player.targetTexture = null;
                }
                Release(s.Target);
                s.Target = null;
                if (s.Host != null) Object.Destroy(s.Host);
            }
            _slots.Clear();

            foreach (RenderTexture rt in _retired) Release(rt);
            _retired.Clear();
        }

        private static void Release(RenderTexture rt)
        {
            if (rt == null) return;
            rt.Release();
            Object.Destroy(rt);
        }
    }
}
