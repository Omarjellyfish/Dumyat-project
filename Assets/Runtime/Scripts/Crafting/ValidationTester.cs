using UnityEngine;
using DumyatRuntime;

namespace DumyatRuntime.Testing
{
    public class ValidationTester : MonoBehaviour
    {
        [SerializeField] private CraftingStation _station;

        private void OnEnable()
        {
            CraftingValidator.OnValidationComplete += HandleValidationResult;
        }

        private void OnDisable()
        {
            CraftingValidator.OnValidationComplete -= HandleValidationResult;
        }

        private void Update()
        {
            // Press Spacebar to trigger a check
            if (Input.GetKeyDown(KeyCode.Space))
            {
                Debug.Log("--- STARTING VALIDATION CHECK ---");
                _station.CheckBuild();
            }
        }

        private void HandleValidationResult(ValidationResult result)
        {
            if (result.IsSuccess)
            {
                Debug.Log($"<color=green>SUCCESS!</color> Score: {result.QualityScore * 100:F1}%\n{result.Message}");
            }
            else
            {
                Debug.Log($"<color=red>FAILED!</color> {result.Message}");

                foreach (var failure in result.FailedParts)
                {
                    string partName = failure.PartObject != null ? failure.PartObject.name : "System";
                    Debug.Log($"<color=orange>[{failure.Reason}]</color> {partName} - {failure.Details}");
                }
            }
        }
    }
}
