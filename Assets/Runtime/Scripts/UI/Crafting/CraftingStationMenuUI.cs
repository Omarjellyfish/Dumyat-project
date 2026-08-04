using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using Reflex.Attributes;

namespace DumyatRuntime
{
    /// <summary>
    /// The station's blueprint panel, drawn as a single drafting sheet.
    ///
    /// An index strip lists every blueprint the station and the player between them can offer;
    /// picking one puts its cut list on the sheet. The sheet's one action button buys the drawing
    /// if it is still locked, or hands it to the station if it is not.
    ///
    /// Selecting in the index is a VIEW change only — nothing reaches
    /// <see cref="CraftingStation.SelectRecipe"/> until the action button is pressed. That keeps
    /// browsing the cut lists free, which matters when the numbers on them are what the player is
    /// about to cut wood against.
    /// </summary>
    public class CraftingStationMenuUI : MonoBehaviour
    {
        /// <summary>Graduations on the short ruler that precedes each dimension.</summary>
        private const int RulerTicks = 7;

        // The sheet's own chrome in both languages. Authored uppercase on the English side because
        // UI Toolkit has no text-transform; casing is a no-op on Arabic.
        private const string EyebrowEn = "WORKSHOP · CUT SHEETS";
        private const string EyebrowAr = "الورشة · ورق القص";
        private const string TitleEn = "BLUEPRINTS";
        private const string TitleAr = "الرسومات";
        private const string UnlockedEn = "UNLOCKED";
        private const string UnlockedAr = "المتاح";
        private const string CutListEn = "CUT LIST";
        private const string CutListAr = "قائمة القص";
        private const string DimensionsEn = "W X H X D (CM)";
        private const string DimensionsAr = "عرض × ارتفاع × عمق (سم)";
        private const string SelectEn = "SELECT";
        private const string SelectAr = "اختار";
        private const string BuyEn = "BUY";
        private const string BuyAr = "اشتري";
        private const string LockedEn = "locked";
        private const string LockedAr = "مقفولة";
        private const string DrawingEn = "DRAWING";
        private const string DrawingAr = "الرسمة";
        private const string PartsEn = "PARTS";
        private const string PartsAr = "القطع";
        private const string ScaleEn = "SCALE";
        private const string ScaleAr = "المقياس";
        private const string NoBlueprintsEn = "No blueprints available at this station.";
        private const string NoBlueprintsAr = "مفيش رسومات في المحطة دي.";
        private const string NoPartsEn = "This drawing lists no parts.";
        private const string NoPartsAr = "الرسمة دي مفيهاش قطع.";

        [SerializeField] private UIDocument _uiDocument;
        [SerializeField] private CraftingStation _station;

        private VisualElement _root;
        private VisualElement _blueprintIndex;
        private ScrollView _blueprintContainer;
        private VisualElement _titleBlock;
        private Button _sheetAction;
        private Label _unlockedCountLabel;
        private Label _eyebrowLabel;
        private Label _titleLabel;
        private Label _unlockedCaption;
        private Label _cutListHeading;
        private Label _dimensionHeading;

        private ICraftingService _crafting;
        private ISettingsService _settings;
        private IEventService _eventService;

        private readonly List<CraftingRecipe> _recipes = new List<CraftingRecipe>();
        private readonly List<VisualElement> _indexChips = new List<VisualElement>();
        private int _selectedIndex = -1;

        /// <summary>Whether the sheet is currently drawn in Arabic. One source of truth per frame.</summary>
        private bool Arabic => UiLanguage.IsArabic(_settings);

        [Inject]
        private void Inject(ICraftingService crafting, ISettingsService settings, IEventService eventService)
        {
            _crafting = crafting;
            _settings = settings;
            _eventService = eventService;
        }

        private void OnEnable()
        {
            if (_uiDocument == null)
            {
                _uiDocument = GetComponent<UIDocument>();
            }

            if (_uiDocument != null && _uiDocument.rootVisualElement != null)
            {
                _root = _uiDocument.rootVisualElement;
                _blueprintIndex = _root.Q<VisualElement>("BlueprintIndex");
                _blueprintContainer = _root.Q<ScrollView>("BlueprintContainer");
                _titleBlock = _root.Q<VisualElement>("SheetTitleBlock");
                _unlockedCountLabel = _root.Q<Label>("UnlockedCount");

                _eyebrowLabel = _root.Q<Label>("CraftingEyebrow");
                _titleLabel = _root.Q<Label>("CraftingTitle");
                _unlockedCaption = _root.Q<Label>("UnlockedLabel");
                _cutListHeading = _root.Q<Label>("CutListHeading");
                _dimensionHeading = _root.Q<Label>("DimensionHeading");

                _sheetAction = _root.Q<Button>("SheetAction");
                if (_sheetAction != null) _sheetAction.clicked += OnSheetActionClicked;
            }

            _eventService?.Subscribe<LanguageChangedEvent>(HandleLanguageChanged);
        }

        private void OnDisable()
        {
            if (_sheetAction != null) _sheetAction.clicked -= OnSheetActionClicked;
            _eventService?.Unsubscribe<LanguageChangedEvent>(HandleLanguageChanged);
        }

        /// <summary>
        /// The player changed language while the sheet was open. Rebuilding rather than patching
        /// strings in place: the index chips and the cut list are both built in C#, so a full
        /// redraw is the only way to be sure nothing keeps the old language.
        /// </summary>
        private void HandleLanguageChanged(LanguageChangedEvent evt)
        {
            ApplyStaticLanguage();
            BuildIndex();
            SelectSheet(_selectedIndex);
        }

        /// <summary>Fills the strings authored in the UXML from the current language.</summary>
        private void ApplyStaticLanguage()
        {
            bool ar = Arabic;

            if (_eyebrowLabel != null) _eyebrowLabel.text = ar ? EyebrowAr : EyebrowEn;
            if (_titleLabel != null) _titleLabel.text = ar ? TitleAr : TitleEn;
            if (_unlockedCaption != null) _unlockedCaption.text = ar ? UnlockedAr : UnlockedEn;
            if (_cutListHeading != null) _cutListHeading.text = ar ? CutListAr : CutListEn;
            if (_dimensionHeading != null) _dimensionHeading.text = ar ? DimensionsAr : DimensionsEn;
        }

        private void Start()
        {
            PopulateBlueprints();
        }

        private void PopulateBlueprints()
        {
            _recipes.Clear();

            if (_station != null && _station.AvailableRecipes != null)
            {
                _recipes.AddRange(_station.AvailableRecipes);
            }

            if (_crafting != null)
            {
                var unlocked = _crafting.GetUnlockedRecipes();
                foreach (var recipe in unlocked)
                {
                    if (recipe != null && !_recipes.Contains(recipe))
                    {
                        _recipes.Add(recipe);
                    }
                }
            }

            // Drop nulls so an index position always maps to a real recipe.
            _recipes.RemoveAll(r => r == null);

            ApplyStaticLanguage();
            BuildIndex();
            UpdateUnlockedCount();

            SelectSheet(_recipes.Count > 0 ? 0 : -1);
        }

        // ─────────────────────────────────────────────────────────── Index strip ──

        private void BuildIndex()
        {
            if (_blueprintIndex == null) return;

            _blueprintIndex.Clear();
            _indexChips.Clear();

            for (int i = 0; i < _recipes.Count; i++)
            {
                int index = i;
                var recipe = _recipes[i];

                var chip = new VisualElement();
                chip.AddToClassList("blueprint-chip");

                var nameLabel = new Label(recipe.RecipeName);
                nameLabel.AddToClassList("blueprint-chip-name");
                nameLabel.pickingMode = PickingMode.Ignore;
                chip.Add(nameLabel);

                int partCount = recipe.RequiredParts != null ? recipe.RequiredParts.Length : 0;
                bool unlocked = _crafting != null && _crafting.IsBlueprintUnlocked(recipe.RecipeName);

                // "N قطعة" reads correctly for any count — Arabic's plural rules would otherwise
                // need three forms, and the counted noun stays singular after a numeral here.
                string meta = unlocked
                    ? (Arabic ? $"{partCount} قطعة" : $"{partCount} parts")
                    : (Arabic ? LockedAr : LockedEn);

                var metaLabel = new Label(meta);
                metaLabel.AddToClassList("blueprint-chip-meta");
                if (!unlocked) metaLabel.AddToClassList("blueprint-chip-meta--locked");
                metaLabel.pickingMode = PickingMode.Ignore;
                chip.Add(metaLabel);

                if (!unlocked) chip.AddToClassList("blueprint-chip--locked");

                chip.RegisterCallback<ClickEvent>(_ => SelectSheet(index));

                _blueprintIndex.Add(chip);
                _indexChips.Add(chip);
            }
        }

        /// <summary>Puts a blueprint on the sheet. View only — nothing reaches the station here.</summary>
        private void SelectSheet(int index)
        {
            _selectedIndex = index;

            for (int i = 0; i < _indexChips.Count; i++)
            {
                _indexChips[i].EnableInClassList("blueprint-chip--active", i == index);
            }

            RenderSheet(index >= 0 && index < _recipes.Count ? _recipes[index] : null);
        }

        // ────────────────────────────────────────────────────────────── The sheet ──

        private void RenderSheet(CraftingRecipe recipe)
        {
            if (_blueprintContainer != null) _blueprintContainer.Clear();
            if (_titleBlock != null) _titleBlock.Clear();

            if (recipe == null)
            {
                if (_blueprintContainer != null)
                {
                    var empty = new Label(Arabic ? NoBlueprintsAr : NoBlueprintsEn);
                    empty.AddToClassList("blueprint-detail-label");
                    _blueprintContainer.Add(empty);
                }

                if (_sheetAction != null) _sheetAction.style.display = DisplayStyle.None;
                UiLanguage.ApplyScript(_root, Arabic);
                return;
            }

            if (_sheetAction != null) _sheetAction.style.display = DisplayStyle.Flex;

            // Cut list — one row per part, dimensions right-aligned in their own column.
            if (recipe.RequiredParts != null && recipe.RequiredParts.Length > 0)
            {
                foreach (var part in recipe.RequiredParts)
                {
                    if (string.IsNullOrEmpty(part.SlotName)) continue;

                    // Convert to centimeters (* 100) and format cleanly (e.g. 4 x 31 x 4)
                    float sizeX = part.ExpectedSize.x * 100f;
                    float sizeY = part.ExpectedSize.y * 100f;
                    float sizeZ = part.ExpectedSize.z * 100f;
                    string sizeText = $"{sizeX:F0} x {sizeY:F0} x {sizeZ:F0}";

                    AddPartRow(_blueprintContainer, part.SlotName, sizeText);
                }
            }
            else if (_blueprintContainer != null)
            {
                var none = new Label(Arabic ? NoPartsAr : NoPartsEn);
                none.AddToClassList("blueprint-detail-label");
                _blueprintContainer.Add(none);
            }

            BuildTitleBlock(recipe);
            UpdateSheetAction(recipe);

            // Last, so the rows, chips and title-block cells built above are all covered. Elements
            // created after this call would start without the shaping class.
            UiLanguage.ApplyScript(_root, Arabic);
        }

        private void BuildTitleBlock(CraftingRecipe recipe)
        {
            if (_titleBlock == null) return;

            int partCount = recipe.RequiredParts != null ? recipe.RequiredParts.Length : 0;
            bool ar = Arabic;

            // The recipe name itself is content, not chrome — it stays as authored in the asset.
            AddTitleBlockCell(_titleBlock, ar ? DrawingAr : DrawingEn, recipe.RecipeName);
            AddTitleBlockCell(_titleBlock, ar ? PartsAr : PartsEn, partCount.ToString("00"));

            // UI Toolkit has no :last-child, so the final cell is tagged explicitly to drop its
            // trailing divider.
            AddTitleBlockCell(_titleBlock, ar ? ScaleAr : ScaleEn, "1:1", isLast: true);
        }

        /// <summary>
        /// The sheet's one action: buy the drawing, or hand it to the station. Buy is jade — the same
        /// colour the wallet uses, because it is a spend.
        /// </summary>
        private void UpdateSheetAction(CraftingRecipe recipe)
        {
            if (_sheetAction == null) return;

            bool unlocked = _crafting != null && _crafting.IsBlueprintUnlocked(recipe.RecipeName);

            // Authored uppercase — UI Toolkit has no text-transform.
            _sheetAction.text = Arabic
                ? (unlocked ? SelectAr : BuyAr)
                : (unlocked ? SelectEn : BuyEn);
            _sheetAction.EnableInClassList("bp-btn-select", unlocked);
            _sheetAction.EnableInClassList("bp-btn-buy", !unlocked);
        }

        private void OnSheetActionClicked()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _recipes.Count) return;

            var recipe = _recipes[_selectedIndex];
            bool unlocked = _crafting != null && _crafting.IsBlueprintUnlocked(recipe.RecipeName);

            if (unlocked)
            {
                OnSelectClicked(recipe);
                return;
            }

            OnBuyClicked(recipe);
        }

        // ────────────────────────────────────────────────────────────── Sheet parts ──

        private void AddPartRow(VisualElement parent, string slotName, string sizeText)
        {
            if (parent == null) return;

            var row = new VisualElement();
            row.AddToClassList("blueprint-part-row");

            var nameLabel = new Label(slotName);
            nameLabel.AddToClassList("blueprint-part-name");
            row.Add(nameLabel);

            row.Add(CreateRuler());

            var sizeLabel = new Label(sizeText);
            sizeLabel.AddToClassList("blueprint-part-size");
            row.Add(sizeLabel);

            parent.Add(row);
        }

        /// <summary>
        /// The short ruler that precedes each dimension — the drawing convention for a dimensioned
        /// edge. Decorative, so it is ray-transparent.
        /// </summary>
        private VisualElement CreateRuler()
        {
            var ruler = new VisualElement();
            ruler.AddToClassList("blueprint-part-ruler");
            ruler.pickingMode = PickingMode.Ignore;

            for (int i = 0; i < RulerTicks; i++)
            {
                var tick = new VisualElement();
                tick.AddToClassList("blueprint-part-tick");

                // Every third tick is a major graduation, like a real rule.
                if (i % 3 == 0) tick.AddToClassList("blueprint-part-tick--major");

                ruler.Add(tick);
            }

            return ruler;
        }

        private void AddTitleBlockCell(VisualElement parent, string key, string value, bool isLast = false)
        {
            var cell = new VisualElement();
            cell.AddToClassList("blueprint-title-cell");
            if (isLast) cell.AddToClassList("blueprint-title-cell--last");

            var keyLabel = new Label(key);
            keyLabel.AddToClassList("blueprint-title-key");
            cell.Add(keyLabel);

            var valueLabel = new Label(value);
            valueLabel.AddToClassList("blueprint-title-value");
            cell.Add(valueLabel);

            parent.Add(cell);
        }

        /// <summary>
        /// Fills the header readout from the currently listed recipes. Display only — null-guarded so
        /// the menu still works against a UXML without the UnlockedCount label.
        /// </summary>
        private void UpdateUnlockedCount()
        {
            if (_unlockedCountLabel == null) return;

            int unlocked = 0;
            foreach (var recipe in _recipes)
            {
                if (_crafting != null && _crafting.IsBlueprintUnlocked(recipe.RecipeName))
                    unlocked++;
            }

            _unlockedCountLabel.text = $"{unlocked}/{_recipes.Count}";
        }

        // ───────────────────────────────────────────────────────────────── Actions ──

        private void OnBuyClicked(CraftingRecipe recipe)
        {
            if (_crafting != null)
            {
                _crafting.UnlockBlueprint(recipe.RecipeName);
            }

            // Rebuilding the index and the action from current state avoids the old bug where the
            // Buy handler stayed subscribed alongside Select and every later press ran both.
            BuildIndex();
            SelectSheet(_selectedIndex);
            UpdateUnlockedCount();

            Debug.Log($"[CraftingStationMenu] Bought blueprint: {recipe.RecipeName}");
        }

        private void OnSelectClicked(CraftingRecipe recipe)
        {
            if (_station != null)
            {
                _station.SelectRecipe(recipe);
                Debug.Log($"[CraftingStationMenu] Selected blueprint: {recipe.RecipeName}");
            }
        }
    }
}
