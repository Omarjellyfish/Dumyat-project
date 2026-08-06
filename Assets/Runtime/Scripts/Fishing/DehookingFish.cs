using Reflex.Attributes;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace DumyatRuntime
{
    public class DehookingFish : MonoBehaviour
    {
        private XRGrabInteractable grabInteractable;
        [SerializeField] private AudioClip CatchFishVoice;
        private ISoundService _soundService;
        private IPlayerPersistenceService _playerPersistence;

        private const string CatchVoicePlayedKey = "catch_fish_voice_played";

        [Inject]
        private void Inject(ISoundService soundService, IPlayerPersistenceService playerPersistence) =>
            (_soundService, _playerPersistence) = (soundService, playerPersistence);

        private void Awake()
        {
            grabInteractable = GetComponent<XRGrabInteractable>();
        }

        private void OnEnable()
        {
            grabInteractable.selectEntered.AddListener(OnGrabbed);
        }

        private void OnDisable()
        {
            grabInteractable.selectEntered.RemoveListener(OnGrabbed);
        }

        private void OnGrabbed(SelectEnterEventArgs args)
        {
            FixedJoint joint = GetComponent<FixedJoint>();

            if (!_playerPersistence.HasKey(CatchVoicePlayedKey))
            {
                _soundService.PlaySoundFxAtPosition(CatchFishVoice, transform, 1f);
                _playerPersistence.AddKey(CatchVoicePlayedKey);
            }

            if (joint != null)
            {
                Destroy(joint);
            }
        }
    }
}