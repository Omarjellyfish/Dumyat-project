using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;

namespace DumyatRuntime
{
    public class CraftingStationMenuUI : MonoBehaviour
    {
        [SerializeField] private UIDocument _uiDocument;
        [SerializeField] private CraftingStation _station;

        private ScrollView _blueprintContainer;

        private void OnEnable()
        {
            if (_uiDocument == null)
            {
                _uiDocument = GetComponent<UIDocument>();
            }

            if (_uiDocument != null && _uiDocument.rootVisualElement != null)
            {
                var root = _uiDocument.rootVisualElement;
                _blueprintContainer = root.Q<ScrollView>("BlueprintContainer");
            }
        }

        private void Start()
        {
            PopulateBlueprints();
        }

        private void PopulateBlueprints()
        {
            if (_blueprintContainer == null) return;

            _blueprintContainer.Clear();

            List<CraftingRecipe> recipesToShow = new List<CraftingRecipe>();

            if (_station != null && _station.AvailableRecipes != null)
            {
                recipesToShow.AddRange(_station.AvailableRecipes);
            }

            if (CraftingManager.Singleton != null)
            {
                var unlocked = CraftingManager.Singleton.GetUnlockedRecipes();
                foreach (var recipe in unlocked)
                {
                    if (recipe != null && !recipesToShow.Contains(recipe))
                    {
                        recipesToShow.Add(recipe);
                    }
                }
            }

            if (recipesToShow.Count == 0) return;

            foreach (var recipe in recipesToShow)
            {
                if (recipe == null) continue;
                _blueprintContainer.Add(CreateBlueprintCard(recipe));
            }
        }

        private VisualElement CreateBlueprintCard(CraftingRecipe recipe)
        {
            var card = new VisualElement();
            card.AddToClassList("blueprint-card");

            // ── Top row: name + button ──
            var topRow = new VisualElement();
            topRow.AddToClassList("blueprint-card-top");

            var nameLabel = new Label(recipe.RecipeName);
            nameLabel.AddToClassList("blueprint-name");
            topRow.Add(nameLabel);

            var actionButton = new Button();
            actionButton.AddToClassList("bp-action-btn");

            bool isUnlocked = CraftingManager.Singleton != null
                && CraftingManager.Singleton.IsBlueprintUnlocked(recipe.RecipeName);

            if (isUnlocked)
            {
                actionButton.text = "Select";
                actionButton.AddToClassList("bp-btn-select");
                actionButton.clicked += () => OnSelectClicked(recipe);
            }
            else
            {
                actionButton.text = "Buy";
                actionButton.AddToClassList("bp-btn-buy");
                actionButton.clicked += () => OnBuyClicked(recipe, actionButton);
            }

            topRow.Add(actionButton);
            card.Add(topRow);

            // ── Details section: blueprint specifics ──
            var detailsContainer = new VisualElement();
            detailsContainer.AddToClassList("blueprint-details");

            int partCount = recipe.RequiredParts != null ? recipe.RequiredParts.Length : 0;
            AddDetailLabel(detailsContainer, $"Parts needed: {partCount}");

            if (recipe.RequiredParts != null)
            {
                foreach (var part in recipe.RequiredParts)
                {
                    if (!string.IsNullOrEmpty(part.SlotName))
                    {
                        // Convert to centimeters (* 100) and format cleanly (e.g. 4 x 31 x 4 cm)
                        float sizeX = part.ExpectedSize.x * 100f;
                        float sizeY = part.ExpectedSize.y * 100f;
                        float sizeZ = part.ExpectedSize.z * 100f;
                        string sizeText = $"{sizeX:F0} x {sizeY:F0} x {sizeZ:F0} cm";
                        AddDetailLabel(detailsContainer, $"• {part.SlotName} (Size: {sizeText})");
                    }
                }
            }

            card.Add(detailsContainer);

            return card;
        }

        private void AddDetailLabel(VisualElement parent, string text)
        {
            var label = new Label(text);
            label.AddToClassList("blueprint-detail-label");
            parent.Add(label);
        }

        private void OnBuyClicked(CraftingRecipe recipe, Button button)
        {
            if (CraftingManager.Singleton != null)
            {
                CraftingManager.Singleton.UnlockBlueprint(recipe.RecipeName);
            }

            button.text = "Select";
            button.RemoveFromClassList("bp-btn-buy");
            button.AddToClassList("bp-btn-select");

            button.clicked += () => OnSelectClicked(recipe);

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
