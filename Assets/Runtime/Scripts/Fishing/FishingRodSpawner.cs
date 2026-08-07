using UnityEngine;

namespace DumyatRuntime
{
    public class FishingRodSpawner : MonoBehaviour
    {
        [SerializeField] private GameObject _fishingRodPrefab;
        [SerializeField] private Transform _spawnPoint;
        void Start()
        {

        }
        void OnTriggerEnter(Collider other)
        {

            if (other.CompareTag("Rod"))
            {
                // other.gameObject.SetActive(false);
                other.transform.position = _spawnPoint.position;
                other.transform.rotation = _spawnPoint.rotation;
            }
        }

        // Update is called once per frame
        void Update()
        {

        }
    }
}
