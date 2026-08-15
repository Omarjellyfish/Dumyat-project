using UnityEngine;
using UnityEngine.UIElements;

namespace DumyatRuntime
{
    /// <summary>
    /// Builds the diagram vocabulary out of plain elements.
    ///
    /// UI Toolkit has no SVG, no clip-path and no polygon, so every symbol here is rectangles, borders
    /// and border-radius — a circle is a square with half its width as radius, a line is a thin box, and
    /// a diagonal is a thin box with `rotate`. That is genuinely all that is available, and it is the
    /// reason the icon set is a fixed enum rather than arbitrary artwork: each one had to be drawable
    /// within those primitives, and each one was.
    ///
    /// Everything is expressed relative to <paramref name="size"/> so a symbol scales as one piece, and
    /// every element is picking-ignored — a diagram is never a ray target.
    /// </summary>
    public static class DiagramIcons
    {
        public static VisualElement Build(DiagramIcon icon, float size, Color colour, Color accent)
        {
            VisualElement root = Box(size, size);
            root.style.position = Position.Absolute;

            switch (icon)
            {
                case DiagramIcon.Microphone: Microphone(root, size, colour, accent); break;
                case DiagramIcon.Waveform: Waveform(root, size, colour); break;
                case DiagramIcon.Chip: Chip(root, size, colour); break;
                case DiagramIcon.SpeechBubble: SpeechBubble(root, size, colour, accent); break;
                case DiagramIcon.Speaker: Speaker(root, size, colour, accent); break;
                case DiagramIcon.Subtitle: Subtitle(root, size, colour); break;
                case DiagramIcon.Balance: Balance(root, size, colour, accent); break;
                case DiagramIcon.ConfirmGate: ConfirmGate(root, size, colour, accent); break;
                case DiagramIcon.Outcome: Outcome(root, size, accent); break;
                case DiagramIcon.Ledger: Ledger(root, size, accent); break;
                case DiagramIcon.Blocked: Blocked(root, size, colour); break;
            }

            return root;
        }

        // ---- symbols -----------------------------------------------------------------------

        private static void Microphone(VisualElement r, float s, Color c, Color accent)
        {
            Ring(r, s * 0.5f, s * 0.5f, s * 0.86f, s * 0.02f, Fade(accent, 0.55f));

            // Capsule, cradle, stem.
            Add(r, Rect(s * 0.5f, s * 0.40f, s * 0.17f, s * 0.30f, c, s * 0.085f));
            Add(r, Rect(s * 0.5f, s * 0.585f, s * 0.34f, s * 0.035f, c, s * 0.02f));
            Add(r, Rect(s * 0.395f, s * 0.545f, s * 0.03f, s * 0.10f, c, 0f));
            Add(r, Rect(s * 0.605f, s * 0.545f, s * 0.03f, s * 0.10f, c, 0f));
            Add(r, Rect(s * 0.5f, s * 0.68f, s * 0.03f, s * 0.11f, c, 0f));

            // Consent, riding the rim.
            Ring(r, s * 0.80f, s * 0.20f, s * 0.26f, s * 0.022f, accent);
            Tick(r, s * 0.80f, s * 0.20f, s * 0.15f, accent, s * 0.028f);
        }

        private static void Waveform(VisualElement r, float s, Color c)
        {
            Ring(r, s * 0.5f, s * 0.5f, s * 0.86f, s * 0.02f, Fade(c, 0.35f));

            float[] h = { 0.22f, 0.44f, 0.60f, 0.38f, 0.50f, 0.19f };
            for (int i = 0; i < h.Length; i++)
            {
                float x = 0.23f + i * 0.108f;
                Add(r, Rect(s * x, s * 0.5f, s * 0.038f, s * h[i], c, s * 0.019f));
            }
        }

        private static void Chip(VisualElement r, float s, Color c)
        {
            Add(r, Outline(s * 0.5f, s * 0.5f, s * 0.52f, s * 0.52f, c, s * 0.032f, s * 0.06f));
            Add(r, Rect(s * 0.5f, s * 0.5f, s * 0.21f, s * 0.21f, c, s * 0.035f));

            // Legs on all four sides.
            for (int i = -1; i <= 1; i++)
            {
                float o = i * s * 0.16f;
                Add(r, Rect(s * 0.5f + o, s * 0.185f, s * 0.032f, s * 0.12f, c, 0f));
                Add(r, Rect(s * 0.5f + o, s * 0.815f, s * 0.032f, s * 0.12f, c, 0f));
                Add(r, Rect(s * 0.185f, s * 0.5f + o, s * 0.12f, s * 0.032f, c, 0f));
                Add(r, Rect(s * 0.815f, s * 0.5f + o, s * 0.12f, s * 0.032f, c, 0f));
            }
        }

        private static void SpeechBubble(VisualElement r, float s, Color c, Color accent)
        {
            Add(r, Outline(s * 0.5f, s * 0.46f, s * 0.80f, s * 0.52f, c, s * 0.032f, s * 0.10f));
            // Tail: a small rotated square tucked under the lower edge.
            VisualElement tail = Rect(s * 0.36f, s * 0.71f, s * 0.13f, s * 0.13f, c, 0f);
            tail.style.rotate = new Rotate(45f);
            Add(r, tail);
            Add(r, Rect(s * 0.36f, s * 0.68f, s * 0.20f, s * 0.10f, Fade(c, 0f), 0f));

            for (int i = -1; i <= 1; i++)
                Add(r, Rect(s * (0.5f + i * 0.15f), s * 0.46f, s * 0.085f, s * 0.085f, accent, s * 0.05f));
        }

        private static void Speaker(VisualElement r, float s, Color c, Color accent)
        {
            Add(r, Rect(s * 0.30f, s * 0.5f, s * 0.16f, s * 0.26f, c, s * 0.02f));
            VisualElement cone = Rect(s * 0.42f, s * 0.5f, s * 0.22f, s * 0.54f, c, s * 0.03f);
            Add(r, cone);

            Ring(r, s * 0.56f, s * 0.5f, s * 0.44f, s * 0.03f, accent);
            Ring(r, s * 0.56f, s * 0.5f, s * 0.72f, s * 0.03f, Fade(accent, 0.7f));
            // The rings are full circles; a plate hides their left halves so they read as arcs.
            Add(r, Rect(s * 0.24f, s * 0.5f, s * 0.50f, s * 0.90f, Fade(c, 0f), 0f));
        }

        private static void Subtitle(VisualElement r, float s, Color c)
        {
            Add(r, Outline(s * 0.5f, s * 0.5f, s * 0.92f, s * 0.50f, c, s * 0.026f, s * 0.055f));
            Add(r, Rect(s * 0.5f, s * 0.42f, s * 0.62f, s * 0.045f, c, s * 0.023f));
            Add(r, Rect(s * 0.5f, s * 0.58f, s * 0.44f, s * 0.045f, Fade(c, 0.5f), s * 0.023f));
        }

        private static void Balance(VisualElement r, float s, Color c, Color accent)
        {
            Add(r, Rect(s * 0.5f, s * 0.5f, s * 0.032f, s * 0.62f, c, 0f));
            Add(r, Rect(s * 0.5f, s * 0.24f, s * 0.66f, s * 0.032f, c, s * 0.016f));
            Add(r, Rect(s * 0.5f, s * 0.80f, s * 0.34f, s * 0.032f, c, s * 0.016f));

            // Pans, as outlined squares rotated to read as bowls.
            foreach (float x in new[] { 0.19f, 0.81f })
            {
                VisualElement pan = Outline(s * x, s * 0.36f, s * 0.20f, s * 0.20f, accent, s * 0.024f, s * 0.03f);
                pan.style.rotate = new Rotate(45f);
                Add(r, pan);
            }
        }

        private static void ConfirmGate(VisualElement r, float s, Color c, Color accent)
        {
            DashedRing(r, s * 0.5f, s * 0.5f, s * 0.88f, s * 0.028f, accent, 18);
            Ring(r, s * 0.5f, s * 0.5f, s * 0.46f, s * 0.032f, c);
            // The break in the inner ring plus the tick reads as "round once, then confirm".
            Add(r, Rect(s * 0.5f, s * 0.27f, s * 0.22f, s * 0.09f, Fade(c, 0f), 0f));
            Tick(r, s * 0.5f, s * 0.52f, s * 0.24f, c, s * 0.034f);
        }

        private static void Outcome(VisualElement r, float s, Color accent)
        {
            Ring(r, s * 0.5f, s * 0.5f, s * 0.90f, s * 0.026f, accent);
            Ring(r, s * 0.5f, s * 0.5f, s * 0.48f, s * 0.040f, accent);
            Add(r, Rect(s * 0.5f, s * 0.5f, s * 0.26f, s * 0.040f, accent, s * 0.02f));
            Add(r, Rect(s * 0.5f, s * 0.5f, s * 0.040f, s * 0.26f, accent, s * 0.02f));
        }

        private static void Ledger(VisualElement r, float s, Color accent)
        {
            Add(r, Outline(s * 0.5f, s * 0.5f, s * 0.74f, s * 0.80f, accent, s * 0.030f, s * 0.06f));
            Add(r, Rect(s * 0.5f, s * 0.28f, s * 0.44f, s * 0.042f, accent, s * 0.021f));

            for (int row = 0; row < 2; row++)
                for (int col = 0; col < 3; col++)
                    Add(r, Rect(s * (0.33f + col * 0.17f), s * (0.52f + row * 0.17f),
                                s * 0.075f, s * 0.075f, accent, s * 0.045f));
        }

        private static void Blocked(VisualElement r, float s, Color c)
        {
            Ring(r, s * 0.5f, s * 0.5f, s * 0.92f, s * 0.032f, c);
            VisualElement bar = Rect(s * 0.5f, s * 0.5f, s * 0.92f, s * 0.032f, c, 0f);
            bar.style.rotate = new Rotate(-45f);
            Add(r, bar);
        }

        // ---- primitives --------------------------------------------------------------------

        /// <summary>A box centred on (cx, cy). Everything is built from this.</summary>
        public static VisualElement Rect(float cx, float cy, float w, float h, Color colour, float radius)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.style.position = Position.Absolute;
            e.style.left = cx - w * 0.5f;
            e.style.top = cy - h * 0.5f;
            e.style.width = w;
            e.style.height = h;
            e.style.backgroundColor = colour;
            SetRadius(e, radius);
            return e;
        }

        /// <summary>A bordered box with nothing inside it.</summary>
        public static VisualElement Outline(float cx, float cy, float w, float h, Color colour,
                                            float thickness, float radius)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.style.position = Position.Absolute;
            e.style.left = cx - w * 0.5f;
            e.style.top = cy - h * 0.5f;
            e.style.width = w;
            e.style.height = h;

            e.style.borderTopWidth = thickness;
            e.style.borderBottomWidth = thickness;
            e.style.borderLeftWidth = thickness;
            e.style.borderRightWidth = thickness;
            e.style.borderTopColor = colour;
            e.style.borderBottomColor = colour;
            e.style.borderLeftColor = colour;
            e.style.borderRightColor = colour;
            SetRadius(e, radius);
            return e;
        }

        /// <summary>A circle: a square whose radius is half its width.</summary>
        public static void Ring(VisualElement parent, float cx, float cy, float d, float thickness, Color colour)
        {
            Add(parent, Outline(cx, cy, d, d, colour, thickness, d * 0.5f));
        }

        /// <summary>A ring of separate dots, since USS has no dashed border.</summary>
        public static void DashedRing(VisualElement parent, float cx, float cy, float d, float dot,
                                      Color colour, int count)
        {
            float radius = d * 0.5f;
            for (int i = 0; i < count; i++)
            {
                float a = i / (float)count * Mathf.PI * 2f;
                Add(parent, Rect(cx + Mathf.Cos(a) * radius, cy + Mathf.Sin(a) * radius,
                                 dot, dot, colour, dot * 0.5f));
            }
        }

        /// <summary>A tick, as two rotated bars.</summary>
        public static void Tick(VisualElement parent, float cx, float cy, float size, Color colour, float thickness)
        {
            VisualElement shortArm = Rect(cx - size * 0.22f, cy + size * 0.12f, size * 0.42f, thickness, colour, thickness * 0.5f);
            shortArm.style.rotate = new Rotate(45f);
            Add(parent, shortArm);

            VisualElement longArm = Rect(cx + size * 0.10f, cy, size * 0.78f, thickness, colour, thickness * 0.5f);
            longArm.style.rotate = new Rotate(-45f);
            Add(parent, longArm);
        }

        public static VisualElement Box(float w, float h)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.style.width = w;
            e.style.height = h;
            return e;
        }

        private static void Add(VisualElement parent, VisualElement child) => parent.Add(child);

        private static void SetRadius(VisualElement e, float r)
        {
            e.style.borderTopLeftRadius = r;
            e.style.borderTopRightRadius = r;
            e.style.borderBottomLeftRadius = r;
            e.style.borderBottomRightRadius = r;
        }

        private static Color Fade(Color c, float a) { c.a = a; return c; }
    }
}
