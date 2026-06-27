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

                var itemContainer = new VisualElement();
                itemContainer.style.flexDirection = FlexDirection.Row;
                itemContainer.style.justifyContent = Justify.SpaceBetween;
                itemContainer.style.alignItems = Align.Center;
                itemContainer.style.paddingTop = 10;
                itemContainer.style.paddingBottom = 10;
                itemContainer.style.borderBottomWidth = 1;
                itemContainer.style.borderBottomColor = new Color(0.3f, 0.3f, 0.3f);

                var nameLabel = new Label(recipe.RecipeName);
                nameLabel.style.color = Color.white;
                nameLabel.style.fontSize = 18;
                itemContainer.Add(nameLabel);

                var actionButton = new Button();
                actionButton.style.width = 100;
                actionButton.style.height = 30;

                bool isUnlocked = CraftingManager.Singleton != null && CraftingManager.Singleton.IsBlueprintUnlocked(recipe.RecipeName);

                if (isUnlocked)
                {
                    actionButton.text = "Select";
                    actionButton.style.backgroundColor = new Color(0.2f, 0.6f, 0.2f);
                    actionButton.clicked += () => OnSelectClicked(recipe);
                }
                else
                {
                    actionButton.text = "Buy";
                    actionButton.style.backgroundColor = new Color(0.6f, 0.4f, 0.1f);
                    actionButton.clicked += () => OnBuyClicked(recipe, actionButton);
                }

                itemContainer.Add(actionButton);
                _blueprintContainer.Add(itemContainer);
            }
        }

        private void OnBuyClicked(CraftingRecipe recipe, Button button)
        {
            if (CraftingManager.Singleton != null)
            {
                CraftingManager.Singleton.UnlockBlueprint(recipe.RecipeName);
            }

            button.text = "Select";
            button.style.backgroundColor = new Color(0.2f, 0.6f, 0.2f);

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
