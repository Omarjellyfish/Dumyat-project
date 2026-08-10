using System;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// The bit of folk wisdom a good-natured merchant opens with before he gets down to business.
    ///
    /// Two things keep it from becoming a tic. It is only offered by the SOFT merchants — a proverb
    /// from Hagg Mahrous, who lowballs seven times in ten, would read as sarcasm rather than warmth —
    /// and even they only say one <see cref="Chance"/> of the time. A line every single approach stops
    /// being charm by the third stall; the same reasoning is written up on
    /// <see cref="HoopAnnouncerSO"/>, which solved this for the basketball hoop.
    ///
    /// "Softness" is read off the dials the merchants already have. There is no aggression field on
    /// <see cref="MerchantConfig"/> — what exists is <see cref="MerchantConfig.DeceptionRate"/> (how
    /// often he lowballs) and <see cref="MerchantConfig.PatienceLevel"/> (how many counter-offers he
    /// tolerates), and between them they describe temperament well enough. Am Farouk sits at 0.35/6 and
    /// Hagg Mahrous at 0.75/3.
    /// </summary>
    [CreateAssetMenu(fileName = "MerchantIdioms", menuName = "Dumyat/Merchant/Merchant Idioms")]
    public class MerchantIdiomsSO : ScriptableObject
    {
        /// <summary>One idiom, in the same EN/AR shape as <see cref="ScriptedMerchantLinesSO.Line"/>.</summary>
        [Serializable]
        public struct Line
        {
            [Tooltip("Subtitle text.")]
            [TextArea] public string english;

            [Tooltip("Egyptian Arabic — this is what is actually spoken, so keep it colloquial and " +
                     "without diacritics.")]
            [TextArea] public string arabic;
        }

        [Header("Lines")]
        [Tooltip("Picked at random. Leave empty to switch idioms off without unassigning the asset.")]
        [SerializeField] private Line[] _idioms = Array.Empty<Line>();

        [Header("When")]
        [Tooltip("How often a qualifying merchant actually says one. A line every approach turns a " +
                 "joke into nagging.")]
        [Range(0f, 1f)]
        [SerializeField] private float _chance = 0.35f;

        [Tooltip("Only merchants at or BELOW this deception rate may speak. The dial runs 0 (honest) " +
                 "to 1 (lowballs constantly), so a lower number here means only the gentler traders.")]
        [Range(0f, 1f)]
        [SerializeField] private float _maxDeceptionRate = 0.55f;

        [Tooltip("Only merchants at or ABOVE this patience may speak. Patience is how many " +
                 "counter-offers he will sit through before walking away, so higher is gentler.")]
        [Min(0)]
        [SerializeField] private int _minPatienceLevel = 4;

        public float Chance => _chance;

        /// <summary>Whether this merchant is mild enough to be the sort who quotes proverbs.</summary>
        public bool QualifiesByTemperament(MerchantConfig config) =>
            config != null
            && config.DeceptionRate <= _maxDeceptionRate
            && config.PatienceLevel >= _minPatienceLevel;

        /// <summary>
        /// An idiom for this merchant, if he is the type and the roll goes his way. False most of the
        /// time by design — the caller should simply carry on with the greeting.
        /// </summary>
        public bool TryPick(MerchantConfig config, out Line line)
        {
            line = default;

            if (_idioms == null || _idioms.Length == 0) return false;
            if (!QualifiesByTemperament(config)) return false;
            if (UnityEngine.Random.value > _chance) return false;

            line = _idioms[UnityEngine.Random.Range(0, _idioms.Length)];
            return !string.IsNullOrWhiteSpace(line.english) || !string.IsNullOrWhiteSpace(line.arabic);
        }
    }
}
