#if UNITY_EDITOR
using System.Collections.Generic;
using DumyatRuntime;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace DumyatEditor
{
    /// <summary>
    /// The deck's running order, as a numbered list you can drag.
    ///
    /// WHY THE NUMBER IS THE ROW POSITION AND NOT A FIELD ON THE SLIDE:
    /// a slide that stores its own "I am number 34" is a second copy of an ordering the deck list already
    /// holds, and the two drift the first time anything moves — you then have 55 assets to renumber by
    /// hand and no way to tell which copy is right. Here the number IS the index, so it cannot disagree
    /// with the order the projector actually walks. Filenames are the one place the number is duplicated,
    /// and <see cref="SlideAssetNaming"/> exists to re-sync them on a button rather than on faith.
    ///
    /// Rows are grouped by section header, because the thing you are usually doing in a 55-row list is
    /// "move this slide to the end of the UI section", and that is unanswerable from row numbers alone.
    /// </summary>
    [CustomEditor(typeof(PresentationDeckSO))]
    public sealed class PresentationDeckEditor : Editor
    {
        private const float RowHeight = 20f;
        private const float SectionHeaderHeight = 22f;

        private SerializedProperty _slides;
        private SerializedProperty _theme;
        private SerializedProperty _defaultBackground;
        private SerializedProperty _defaultTransition;
        private SerializedProperty _sectionNames;
        private SerializedProperty _residency;

        private ReorderableList _list;

        /// <summary>Cached: recomputing it per repaint would scan the whole AssetDatabase every frame.</summary>
        private List<SlideSO> _orphans;
        private bool _showOrphans;

        private void OnEnable()
        {
            _slides = serializedObject.FindProperty("_slides");
            _theme = serializedObject.FindProperty("_theme");
            _defaultBackground = serializedObject.FindProperty("_defaultBackground");
            _defaultTransition = serializedObject.FindProperty("_defaultTransition");
            _sectionNames = serializedObject.FindProperty("_sectionNames");
            _residency = serializedObject.FindProperty("_residency");

            BuildList();
            RescanOrphans();
        }

        private void BuildList()
        {
            _list = new ReorderableList(serializedObject, _slides, true, true, true, true)
            {
                drawHeaderCallback = r => EditorGUI.LabelField(r,
                    "Slides, in presentation order",
                    "drag, or ▲▼, to reorder"),

                elementHeightCallback = index =>
                    StartsSection(index) ? RowHeight + SectionHeaderHeight : RowHeight,

                drawElementCallback = DrawRow,

                // Unity's default "+" duplicates whatever the previous row held, which in a list of
                // object references silently puts the same slide in the deck twice. An added row should
                // be an empty slot waiting for a slide.
                onAddCallback = list =>
                {
                    int at = list.index >= 0 && list.index < _slides.arraySize
                        ? list.index + 1
                        : _slides.arraySize;

                    _slides.InsertArrayElementAtIndex(at);
                    _slides.GetArrayElementAtIndex(at).objectReferenceValue = null;
                    list.index = at;
                },

                onRemoveCallback = list =>
                {
                    // Via defaultBehaviours because deleting an object-reference element takes two
                    // DeleteArrayElementAtIndex calls — the first only nulls it.
                    ReorderableList.defaultBehaviours.DoRemoveButton(list);
                    RescanOrphans();
                },
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var deck = (PresentationDeckSO)target;

            DrawToolbar(deck);
            EditorGUILayout.Space(6);

            _list.DoLayoutList();

            EditorGUILayout.Space(6);
            DrawOrphans();

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Defaults — a slide's own override wins over these",
                                       EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_theme);
            EditorGUILayout.PropertyField(_defaultBackground);
            EditorGUILayout.PropertyField(_defaultTransition);

            EditorGUILayout.Space(6);
            EditorGUILayout.PropertyField(_sectionNames, true);
            EditorGUILayout.PropertyField(_residency);

            serializedObject.ApplyModifiedProperties();
        }

        // ---- header ---------------------------------------------------------------------------

        private void DrawToolbar(PresentationDeckSO deck)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Deck", EditorStyles.boldLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent("Renumber Assets To Deck Order",
                            "Renames every slide FILE so it leads with its position here — " +
                            "Slide_07_Art_Home_fan. GUIDs are untouched, so nothing breaks.\n\n" +
                            "Press this after reordering, or the Project window keeps showing the " +
                            "old running order.")))
                    {
                        serializedObject.ApplyModifiedProperties();
                        string preview = SlideAssetNaming.RenumberToDeckOrder(deck, false);
                        if (EditorUtility.DisplayDialog("Renumber slide assets", preview,
                                                        "Rename", "Cancel"))
                            Debug.Log(SlideAssetNaming.RenumberToDeckOrder(deck, true), deck);
                    }

                    if (GUILayout.Button(new GUIContent("Validate Deck",
                            "Checks for empty slots, blank titles, duplicated slides and section " +
                            "numbers no key can reach.")))
                    {
                        serializedObject.ApplyModifiedProperties();
                        Debug.Log(DeckValidator.Validate(deck), deck);
                    }
                }

                EditorGUILayout.LabelField(
                    deck.Count + " slide(s) across " + CountSections(deck) + " section(s).",
                    EditorStyles.miniLabel);
            }
        }

        private static int CountSections(PresentationDeckSO deck)
        {
            var sections = new HashSet<int>();
            for (int i = 0; i < deck.Count; i++)
            {
                SlideSO s = deck.SlideAt(i);
                if (s != null) sections.Add(s.SectionNumber);
            }
            return sections.Count;
        }

        // ---- rows -----------------------------------------------------------------------------

        /// <summary>Read through the SerializedProperty, not the target: after a drag the target is a
        /// repaint behind and the headers would land on the wrong rows.</summary>
        private SlideSO SlideAt(int index) =>
            index >= 0 && index < _slides.arraySize
                ? _slides.GetArrayElementAtIndex(index).objectReferenceValue as SlideSO
                : null;

        private bool StartsSection(int index)
        {
            if (index == 0) return true;

            SlideSO current = SlideAt(index);
            SlideSO previous = SlideAt(index - 1);
            if (current == null || previous == null) return false;

            return current.SectionNumber != previous.SectionNumber;
        }

        private void DrawRow(Rect rect, int index, bool active, bool focused)
        {
            var deck = (PresentationDeckSO)target;
            SlideSO slide = SlideAt(index);

            if (StartsSection(index))
            {
                var header = new Rect(rect.x, rect.y + 3f, rect.width, SectionHeaderHeight - 5f);
                string name = slide != null ? deck.ResolveSectionName(slide) : string.Empty;
                string label = slide != null ? "§" + slide.SectionNumber : "§?";
                if (!string.IsNullOrEmpty(name)) label += "   " + name;

                EditorGUI.LabelField(header, label.ToUpperInvariant(), SectionStyle);
                EditorGUI.DrawRect(new Rect(rect.x, header.yMax, rect.width, 1f),
                                   new Color(0.47f, 0.89f, 0.91f, 0.28f));
                rect.y += SectionHeaderHeight;
            }

            rect.height = RowHeight - 4f;
            rect.y += 2f;

            const float NumberWidth = 26f;
            const float ArrowWidth = 20f;
            const float BadgeWidth = 52f;

            var number = new Rect(rect.x, rect.y, NumberWidth, rect.height);
            var field = new Rect(number.xMax + 2f, rect.y,
                                 rect.width - NumberWidth - BadgeWidth - ArrowWidth * 2f - 10f,
                                 rect.height);
            var badge = new Rect(field.xMax + 4f, rect.y, BadgeWidth, rect.height);
            var up = new Rect(badge.xMax + 2f, rect.y, ArrowWidth, rect.height);
            var down = new Rect(up.xMax, rect.y, ArrowWidth, rect.height);

            EditorGUI.LabelField(number, (index + 1).ToString("00"), NumberStyle);

            EditorGUI.PropertyField(field, _slides.GetArrayElementAtIndex(index), GUIContent.none);

            if (slide == null)
                EditorGUI.LabelField(badge, "empty", EmptyStyle);
            else
                EditorGUI.LabelField(badge, TypeBadge(slide), BadgeStyle);

            using (new EditorGUI.DisabledScope(index == 0))
                if (GUI.Button(up, new GUIContent("▲", "Move up one place"), EditorStyles.miniButtonLeft))
                    Move(index, index - 1);

            using (new EditorGUI.DisabledScope(index >= _slides.arraySize - 1))
                if (GUI.Button(down, new GUIContent("▼", "Move down one place"),
                               EditorStyles.miniButtonRight))
                    Move(index, index + 1);
        }

        /// <summary>
        /// Deliberately does NOT apply here: OnInspectorGUI applies once at the end, and committing
        /// mid-draw would re-serialize the array while the list is still iterating it.
        /// </summary>
        private void Move(int from, int to)
        {
            _slides.MoveArrayElement(from, to);
            _list.index = to;
            GUI.changed = true;
        }

        private static string TypeBadge(SlideSO slide)
        {
            switch (slide)
            {
                case SceneShowcaseSlideSO _: return "Scene";
                case UiShowcaseSlideSO _: return "UI";
                case MediaSlideSO _: return "Media";
                case TextSlideSO _: return "Text";
                default: return slide.GetType().Name;
            }
        }

        // ---- slides not in the deck -----------------------------------------------------------

        private void RescanOrphans() => _orphans = DeckValidator.SlidesNotIn((PresentationDeckSO)target);

        private void DrawOrphans()
        {
            if (_orphans == null) RescanOrphans();

            using (new EditorGUILayout.HorizontalScope())
            {
                _showOrphans = EditorGUILayout.Foldout(_showOrphans,
                    _orphans.Count + " slide asset(s) not in this deck", true);

                if (GUILayout.Button(new GUIContent("Rescan",
                        "Re-reads the project. Not automatic — it scans every slide asset."),
                        GUILayout.Width(60)))
                    RescanOrphans();
            }

            if (!_showOrphans || _orphans.Count == 0) return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                foreach (SlideSO orphan in _orphans)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button(new GUIContent("Add",
                                "Appends this slide to the end of the deck."), GUILayout.Width(44)))
                        {
                            int at = _slides.arraySize;
                            _slides.InsertArrayElementAtIndex(at);
                            _slides.GetArrayElementAtIndex(at).objectReferenceValue = orphan;
                            serializedObject.ApplyModifiedProperties();
                            RescanOrphans();
                            return;   // the list changed under this layout pass
                        }

                        EditorGUILayout.ObjectField(orphan, typeof(SlideSO), false);
                    }
                }
            }
        }

        // ---- styles ---------------------------------------------------------------------------

        private static GUIStyle _numberStyle, _badgeStyle, _emptyStyle, _sectionStyle;

        private static GUIStyle NumberStyle => _numberStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleRight,
            normal = { textColor = new Color(0.47f, 0.89f, 0.91f) },
        };

        private static GUIStyle BadgeStyle => _badgeStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleRight,
            normal = { textColor = new Color(0.62f, 0.66f, 0.68f) },
        };

        private static GUIStyle EmptyStyle => _emptyStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleRight,
            normal = { textColor = new Color(1f, 0.61f, 0.54f) },
        };

        private static GUIStyle SectionStyle => _sectionStyle ??= new GUIStyle(EditorStyles.miniBoldLabel)
        {
            normal = { textColor = new Color(0.55f, 0.92f, 0.86f) },
        };
    }
}
#endif
