using UnityEngine;

namespace DumyatRuntime
{
    public class CharacterAudioManager : MonoBehaviour
    {
        [Header("Local Audio Sources")]
        [Tooltip("Used for quick, overlapping sounds like footsteps and impacts.")]
        [SerializeField] private AudioSource oneShotSource;

        [Tooltip("Used for dialogue or grunts so they don't talk over themselves.")]
        [SerializeField] private AudioSource voiceSource;

        [Tooltip("Used for continuous abilities, like a jetpack or charging an attack.")]
        [SerializeField] private AudioSource loopSource;

        [Tooltip("For extended dialouge")]
        [SerializeField] private AudioSource DialougeAudioSource;


        [Header("Audio Banks")]
        [SerializeField] private AudioClip[] footsteps;
        [SerializeField] private AudioClip[] jumpSounds;
        [SerializeField] private AudioClip[] attackGrunts;
        [SerializeField] private AudioClip[] hurtSounds;

        #region One-Shot SFX (Footsteps, Jumps)
        public void PlayFootstep()
        {
            if (footsteps.Length == 0) return;
            AudioClip clip = footsteps[Random.Range(0, footsteps.Length)];

            oneShotSource.PlayOneShot(clip);
        }

        public void PlayJump()
        {
            if (jumpSounds.Length == 0) return;
            AudioClip clip = jumpSounds[Random.Range(0, jumpSounds.Length)];
            oneShotSource.PlayOneShot(clip);
        }
        #endregion

        #region Voice Lines (Grunts, Hurt)

        public void PlayDialougeSound(AudioClip dialogueSound)
        {
            if (dialogueSound == null) return;
            DialougeAudioSource.clip = dialogueSound;
            DialougeAudioSource.Play();
        }
        public void PlayAttackGrunt()
        {
            if (attackGrunts.Length == 0) return;
            AudioClip clip = attackGrunts[Random.Range(0, attackGrunts.Length)];

            // Assign to clip and Play() so it interrupts any current voice line
            voiceSource.clip = clip;
            voiceSource.Play();
        }

        public void PlayHurtSound()
        {
            if (hurtSounds.Length == 0) return;
            AudioClip clip = hurtSounds[Random.Range(0, hurtSounds.Length)];
            voiceSource.clip = clip;
            voiceSource.Play();
        }
        #endregion

        #region Looping Abilities
        public void StartLoopingSound(AudioClip clip)
        {
            if (clip == null) return;
            loopSource.clip = clip;
            loopSource.loop = true;
            loopSource.Play();
        }


        public void StopLoopingSound()
        {
            loopSource.Stop();
        }
        #endregion
    }
}