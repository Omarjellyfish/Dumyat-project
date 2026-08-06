using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// The striking face of the hammer. Turns a physics contact into one blow on the nail it lands on.
    ///
    /// A hammer held in VR does not touch a nail once and stop. The tracked hand keeps driving the
    /// rigidbody into a collider it cannot pass through, so the solver separates and re-contacts many
    /// times a second, and a mesh collider reports each of its contact pairs separately. Taken at face
    /// value that reads as dozens of blows for one swing — enough to drive a nail home instantly and
    /// then re-open the menu the last blow just closed.
    ///
    /// So a nail is struck only on the transition from "not touching" to "touching", and cannot be
    /// struck again until this head has physically left it. <see cref="NailAction"/> holds a short
    /// cooldown on top, which catches the case this cannot see: a real bounce, where the head leaves
    /// and returns inside a couple of frames.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class HammerHead : MonoBehaviour
    {
        [SerializeField] private LayerMask _nailLayer;

        // Contact COUNT per nail, not a flag. One nail can answer a single swing with several collider
        // pairs, each sending its own enter and exit; counting them means the nail re-arms on the last
        // exit rather than the first, which is what stops one swing registering twice.
        private readonly Dictionary<NailAction, int> _contacts = new Dictionary<NailAction, int>();

        // Reused so pruning does not allocate on a path that runs on every blow.
        private readonly List<NailAction> _lostContacts = new List<NailAction>();

        private void OnCollisionEnter(Collision collision)
        {
            NailAction nail = ResolveNail(collision);
            if (nail == null) return;

            DropLostContacts();

            if (_contacts.TryGetValue(nail, out int count))
            {
                // Already resting against this nail — this is another collider pair or a solver
                // re-contact, not a new swing.
                _contacts[nail] = count + 1;
                return;
            }

            _contacts[nail] = 1;
            nail.Hit(collision.relativeVelocity.magnitude);
        }

        private void OnCollisionExit(Collision collision)
        {
            NailAction nail = ResolveNail(collision);
            if (nail == null) return;

            if (!_contacts.TryGetValue(nail, out int count)) return;

            if (count <= 1) _contacts.Remove(nail);
            else _contacts[nail] = count - 1;
        }

        // Dropping the hammer, or the menu switching it off, ends every contact it was holding.
        private void OnDisable() => _contacts.Clear();

        private NailAction ResolveNail(Collision collision)
        {
            if (((1 << collision.gameObject.layer) & _nailLayer) == 0) return null;

            return collision.gameObject.GetComponentInParent<NailAction>();
        }

        /// <summary>
        /// Forgets nails that can no longer send an exit.
        ///
        /// <see cref="NailGroupController"/> hides a whole group of nails the moment one of them opens
        /// a panel, and a nail switched off while the head is inside it would otherwise stay counted as
        /// "in contact" forever — leaving it permanently un-hittable once it comes back.
        /// </summary>
        private void DropLostContacts()
        {
            if (_contacts.Count == 0) return;

            _lostContacts.Clear();

            foreach (KeyValuePair<NailAction, int> contact in _contacts)
            {
                if (contact.Key == null || !contact.Key.isActiveAndEnabled)
                    _lostContacts.Add(contact.Key);
            }

            foreach (NailAction lost in _lostContacts)
                _contacts.Remove(lost);
        }
    }
}
