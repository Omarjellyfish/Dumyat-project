using UnityEngine;

namespace DumyatEditor
{
    [CreateAssetMenu(fileName = "PlayerAttackStats", menuName = "Dumyat/Custom Inspector Test/Player Attack Stats", order = 0)]
    public class PlayerAttackStats : ScriptableObject
    {
        [SerializeField] private float _playerMovementSpeed = 10f;
        [SerializeField] private float _playerDashSpeed = 5f;

        [SerializeField] private bool _showValues;
        [SerializeField] private float _attackDamage = 10f;
        [SerializeField] private float _attackRange = 5f;

        public enum AttackType
        {
            Melee,
            Ranged
        }

        public AttackType PlayerAttackType;


       public const float DEFAULT_MOVEMENT_SPEED= 10f;
       public const float DEFAULT_DASH_SPEED = 5f;


        public void AssignRandomSpeed()
        {
            _playerMovementSpeed = Random.Range(0, 101);
        }

        public void SetToDefaultValues()
        {
            _playerMovementSpeed = DEFAULT_MOVEMENT_SPEED;
            _playerDashSpeed = DEFAULT_DASH_SPEED;

            Debug.Log("Movement Speed and Dash Speed have been set to default values.");
        }


    }
}
