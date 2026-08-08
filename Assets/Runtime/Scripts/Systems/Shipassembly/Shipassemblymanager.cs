using System;
using System.Collections.Generic;
using UnityEngine;
using Reflex.Attributes;

namespace DumyatRuntime
{
    /// <summary>
    /// Tracks every <see cref="ShipPartSocket"/> in the Ship Workshop scene and fires
    /// <see cref="OnShipFullyAssembled"/> the moment the last required socket is filled.
    ///
    /// Plain scene-referenced MonoBehaviour singleton -- intentionally NOT wired into Reflex,
    /// per the task brief. It doesn't touch inventory state at all, so there's nothing for DI
    /// to give it; if a future feature needs that, add an [Inject] method for IInventoryService
    /// rather than reworking this class.
    ///
    /// Other systems (cutscenes, dialogue, quest triggers, etc.) can subscribe to
    /// <see cref="OnShipFullyAssembled"/> without ShipAssemblyManager knowing anything about them —
    /// or, for anything that shouldn't hold a reference to this manager at all, to
    /// <see cref="ShipPartSocketedEvent"/> / <see cref="ShipAssembledEvent"/> on the EventBus.
    /// </summary>
    public class ShipAssemblyManager : MonoBehaviour
    {
        public static ShipAssemblyManager Instance { get; private set; }

        private IEventService _eventService;

        // The class note above says a future need for DI should arrive as an [Inject] method rather
        // than a rework, so here it is: the sockets are a scene fact the tutorial and the quest system
        // both want, and neither should have to find this manager to hear about them.
        [Inject]
        private void Inject(IEventService eventService) => _eventService = eventService;

        [Header("Sockets")]
        [Tooltip("All sockets that must be filled for the ship to count as assembled. Leave empty to auto-discover every ShipPartSocket in the scene on Awake.")]
        [SerializeField] private ShipPartSocket[] _requiredSockets;

        private readonly HashSet<ShipPartSocket> _filledSockets = new();
        private bool _hasFiredCompletion;

        /// <summary>Fired once, the moment the last required socket is filled.</summary>
        public event Action OnShipFullyAssembled;

        public bool IsFullyAssembled =>
            _requiredSockets != null && _requiredSockets.Length > 0 && _filledSockets.Count >= _requiredSockets.Length;

        public int FilledSocketCount => _filledSockets.Count;
        public int RequiredSocketCount => _requiredSockets?.Length ?? 0;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("ShipAssemblyManager: an instance already exists in this scene; destroying duplicate.");
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (_requiredSockets == null || _requiredSockets.Length == 0)
                _requiredSockets = FindObjectsByType<ShipPartSocket>(FindObjectsSortMode.None);
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>Called by a <see cref="ShipPartSocket"/> when a part is successfully socketed.</summary>
        public void NotifySocketFilled(ShipPartSocket socket)
        {
            if (socket == null || Array.IndexOf(_requiredSockets, socket) < 0)
                return;

            _filledSockets.Add(socket);

            _eventService?.Publish(new ShipPartSocketedEvent(
                socket.SocketID, _filledSockets.Count, RequiredSocketCount));

            CheckCompletion();
        }

        /// <summary>Called by a <see cref="ShipPartSocket"/> if it's ever cleared (e.g. a reset flow).</summary>
        public void NotifySocketEmptied(ShipPartSocket socket)
        {
            if (socket == null) return;

            if (_filledSockets.Remove(socket))
                _hasFiredCompletion = false; // ship is no longer complete; allow the event to re-fire later
        }

        private void CheckCompletion()
        {
            if (_hasFiredCompletion || !IsFullyAssembled) return;

            _hasFiredCompletion = true;
            Debug.Log("ShipAssemblyManager: ship fully assembled.");
            OnShipFullyAssembled?.Invoke();
            _eventService?.Publish(new ShipAssembledEvent(_filledSockets.Count));
        }
    }
}