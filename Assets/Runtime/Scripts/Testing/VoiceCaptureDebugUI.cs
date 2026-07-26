using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace DumyatRuntime
{
    /// <summary>
    /// TEMPORARY developer overlay for diagnosing microphone / voice-capture problems (built for the
    /// "mic not working in VR" investigation). Drives a <see cref="VoiceCaptureController"/> directly so
    /// you can, at runtime and ENTIRELY FROM INSIDE VR: switch capture mode (push-to-talk / voice-activity),
    /// pick the microphone device, request OS mic permission, open/close the STT session, arm/stop capture,
    /// and WATCH the outcome — the OS mic permission, the live device list, whether a session is open, and
    /// (the key VR tell) the live input LEVEL while capturing.
    ///
    /// Read the level meter first: if it stays 0.0000 while "Capturing" is true, the mic is not delivering
    /// audio (missing OS RECORD_AUDIO grant, no device, or a routing issue) — not an STT/socket problem.
    ///
    /// Primary surface: a world-space panel that floats in the upper-left of the player's view (like the
    /// consent panel). Every control on it is a ray-clickable uGUI button (the canvas has a
    /// TrackedDeviceGraphicRaycaster), so the whole tool is operable with a VR controller — no keyboard,
    /// no mouse. The panel reuses the scene's existing XR UI input module + ray interactors.
    ///
    /// A flat OnGUI overlay (F9) still draws on the Editor Game view / PCVR Link mirror as a desktop
    /// convenience; it is NOT needed for VR and is never visible inside a standalone headset.
    ///
    /// Drop this on any GameObject in a scene that has a <see cref="VoiceCaptureController"/>. Delete it
    /// when done — it is a debug tool, not shipping UI.
    /// </summary>
    public class VoiceCaptureDebugUI : MonoBehaviour
    {
        [Header("Target (auto-found if empty)")]
        [SerializeField] private VoiceCaptureController _controller;

        [Header("Surfaces")]
        [Tooltip("Float the interactive VR panel in the player's view. This is the surface you use in-headset.")]
        [SerializeField] private bool _showWorldPanel = true;

        [Tooltip("Also draw the flat OnGUI overlay (Editor / PCVR Link mirror, needs a mouse). Desktop extra; " +
                 "not required for VR. F9 toggles it.")]
        [SerializeField] private bool _showOverlay = true;

        [Tooltip("Scale of the OnGUI overlay (raise it for readability on a high-res mirror).")]
        [SerializeField, Min(0.5f)] private float _overlayScale = 1.6f;

        [Header("VR summon")]
        [Tooltip("Optional controller button that shows/hides the whole VR panel from inside the headset " +
                 "(so you don't need the Inspector in a standalone build). Leave empty to use the built-in " +
                 "default: left-hand Menu or Primary (X) button, or F10 on a keyboard.")]
        [SerializeField] private InputActionReference _summonButton;

        [Header("World panel placement (floats top-left of view)")]
        [Tooltip("Distance (m) the world-space panel floats in front of the camera.")]
        [SerializeField, Min(0.3f)] private float _worldDistance = 1.2f;

        [Tooltip("How far LEFT of view-centre the panel sits (m) — pushes it out of the middle.")]
        [SerializeField] private float _worldLeftOffset = 0.5f;

        [Tooltip("How far UP from view-centre the panel's top edge sits (m).")]
        [SerializeField] private float _worldUpOffset = 0.36f;

        [Header("Convenience")]
        [Tooltip("Open the STT session automatically on Start so the meter is live without pressing Open.")]
        [SerializeField] private bool _openSessionOnStart;

        private IConsentService _consent;
        private readonly List<string> _events = new List<string>();
        private const int MaxEvents = 8;
        private Vector2 _scroll;

        // World-space panel (built lazily).
        private Transform _worldRoot;
        private GameObject _worldBody;      // collapsed by the Hide/Show button
        private TextMeshProUGUI _worldText;
        private TextMeshProUGUI _toggleLabel;
        private TextMeshProUGUI _permLabel;
        private TextMeshProUGUI _modeLabel;
        private TextMeshProUGUI _autoLabel;
        private TextMeshProUGUI _deviceLabel;
        private TextMeshProUGUI _vadLabel;
        private TextMeshProUGUI _monitorLabel;
        private bool _bodyVisible = true;
        private bool _panelShown = true;
        private Camera _camera;

        // Built-in summon binding, used only when no _summonButton is assigned.
        private InputAction _fallbackSummon;

        // Direct mic monitor — opens the OS microphone straight (bypassing consent / STT / socket / VAD)
        // so we can tell definitively whether Unity's Microphone delivers ANY audio on this device.
        private AudioClip _monitorClip;
        private string _monitorDevice;
        private float[] _monitorBuffer;
        private int _monitorLastPos;
        private int _monitorPos;
        private float _monitorLevel;
        private bool _monitorActive;

        private void Awake()
        {
            // Prefer a controller on THIS GameObject (the controller adds this component to itself when
            // its Show Debug UI checkbox is on); fall back to a scene search for manual placement.
            if (_controller == null) _controller = GetComponent<VoiceCaptureController>();
            if (_controller == null) _controller = FindFirstObjectByType<VoiceCaptureController>();
            _consent = FindFirstObjectByType<ConsentManager>();
            _camera = Camera.main;
        }

        private void OnEnable()
        {
            if (_controller != null)
            {
                _controller.OnTranscription += HandleTranscript;
                _controller.OnCaptureFailed += HandleFailed;
            }
            if (_showWorldPanel && _worldRoot == null) BuildWorldPanel();

            // Wire the in-VR summon button (assigned action, else the built-in default).
            if (_summonButton != null && _summonButton.action != null)
            {
                _summonButton.action.performed += OnSummon;
                _summonButton.action.Enable();
            }
            else
            {
                _fallbackSummon = new InputAction("VoiceDebugSummon", InputActionType.Button);
                _fallbackSummon.AddBinding("<XRController>{LeftHand}/menuButton");
                _fallbackSummon.AddBinding("<XRController>{LeftHand}/primaryButton");
                _fallbackSummon.AddBinding("<Keyboard>/f10");
                _fallbackSummon.performed += OnSummon;
                _fallbackSummon.Enable();
            }
        }

        private void OnDisable()
        {
            if (_controller != null)
            {
                _controller.OnTranscription -= HandleTranscript;
                _controller.OnCaptureFailed -= HandleFailed;
            }
            if (_summonButton != null && _summonButton.action != null)
                _summonButton.action.performed -= OnSummon;
            if (_fallbackSummon != null)
            {
                _fallbackSummon.performed -= OnSummon;
                _fallbackSummon.Dispose();
                _fallbackSummon = null;
            }

            if (_monitorActive) StopMicMonitor();   // release the OS mic if a direct test was running
            DestroyWorldPanel();   // clean up so toggling the checkbox off leaves nothing behind
        }

        // Show/hide the whole VR panel from a controller button (standalone-headset summon).
        private void OnSummon(InputAction.CallbackContext _)
        {
            _panelShown = !_panelShown;
            if (_worldRoot != null) _worldRoot.gameObject.SetActive(_panelShown);
        }

        private void Start()
        {
            if (_openSessionOnStart && _controller != null) _controller.OpenSession();
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.f9Key.wasPressedThisFrame) _showOverlay = !_showOverlay;
            UpdateMicMonitor();
        }

        private void LateUpdate()
        {
            if (_worldRoot == null) return;
            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return;

            // Float in the UPPER-LEFT of the player's view (not dead centre), upright and facing the player.
            Transform cam = _camera.transform;
            Vector3 centre = cam.position + cam.forward * _worldDistance;
            _worldRoot.position = centre + cam.up * _worldUpOffset - cam.right * _worldLeftOffset;
            _worldRoot.rotation = Quaternion.LookRotation(_worldRoot.position - cam.position, Vector3.up);

            if (_bodyVisible) RefreshWorldLabels();
        }

        // --- Event capture ---------------------------------------------------------

        private void HandleTranscript(string t) =>
            PushEvent("TRANSCRIPT: " + (string.IsNullOrEmpty(t) ? "<empty>" : t));

        private void HandleFailed(string reason) => PushEvent("FAILED: " + reason);

        private void PushEvent(string s)
        {
            _events.Add($"{Time.time:0.0}s  {s}");
            if (_events.Count > MaxEvents) _events.RemoveAt(0);
        }

        // --- Live label refresh (world panel) --------------------------------------

        private void RefreshWorldLabels()
        {
            if (_worldText != null) _worldText.text = BuildReadoutText(includeHint: true);
            if (_controller == null) return;

            if (_permLabel != null)
                _permLabel.text = MicrophonePermission.IsGranted ? "Mic Perm: GRANTED" : "Request Mic Perm";
            if (_modeLabel != null) _modeLabel.text = "Mode: " + _controller.Mode;
            if (_autoLabel != null) _autoLabel.text = "AutoReArm: " + (_controller.AutoReArm ? "On" : "Off");
            if (_deviceLabel != null)
            {
                string dev = _controller.MicrophoneDevice;
                _deviceLabel.text = "Mic: " + (string.IsNullOrEmpty(dev) ? "default" : dev);
            }
            if (_vadLabel != null) _vadLabel.text = "VAD gate: " + _controller.VadAmplitudeThreshold.ToString("0.000");
            if (_monitorLabel != null) _monitorLabel.text = _monitorActive ? "Mic Test: ON (stop)" : "Mic Test (direct)";
        }

        // --- Readout text (shared by world panel + OnGUI) --------------------------

        private string BuildReadoutText(bool includeHint)
        {
            var sb = new StringBuilder();
            string[] devices = Microphone.devices;

            sb.Append("OS mic permission: ").Append(MicrophonePermission.IsGranted ? "GRANTED" : "DENIED").Append('\n');
            if (_consent != null)
                sb.Append("Voice mode available: ").Append(_consent.IsVoiceModeAvailable()).Append('\n');
            sb.Append("Microphone.devices: ").Append(devices.Length);
            if (devices.Length > 0) sb.Append("  [").Append(string.Join(", ", devices)).Append(']');
            sb.Append('\n');

            // Direct mic test — the definitive read: does Unity's Microphone deliver ANY audio here,
            // independent of consent / socket / VAD / STT.
            if (_monitorActive)
            {
                int hz = _monitorClip != null ? _monitorClip.frequency : 0;
                sb.Append("── DIRECT MIC TEST @ ").Append(hz).Append("Hz  pos=").Append(_monitorPos)
                  .Append("  level ").Append(_monitorLevel.ToString("0.0000")).Append("  ")
                  .Append(LevelBar(_monitorLevel)).Append('\n');
                if (includeHint && _monitorPos == 0)
                    sb.Append(">> pos NOT advancing — Microphone.Start isn't recording (permission/device).\n");
                else if (includeHint && _monitorLevel <= 0.0001f)
                    sb.Append(">> recording (pos advances) but SILENT — the device returns no signal.\n");
                else if (includeHint)
                    sb.Append(">> MIC WORKS — audio is arriving. The failure is downstream (VAD/socket/STT).\n");
            }

            if (_controller == null) { sb.Append("No VoiceCaptureController in scene."); return sb.ToString(); }

            sb.Append("Session open: ").Append(_controller.IsSessionOpen)
              .Append("   Capturing: ").Append(_controller.IsCapturing).Append('\n');

            float lvl = _controller.CurrentInputLevel;
            sb.Append("Input level: ").Append(lvl.ToString("0.0000")).Append("  ").Append(LevelBar(lvl)).Append('\n');

            // In voice-activity mode only audio ABOVE this gate is treated as speech and streamed — show
            // it next to the level so a too-high gate silently eating normal speech is obvious.
            if (_controller.Mode == VoiceCaptureController.CaptureMode.VoiceActivity)
            {
                float gate = _controller.VadAmplitudeThreshold;
                sb.Append("VAD gate: ≥ ").Append(gate.ToString("0.000"))
                  .Append(lvl >= gate ? "   VOICED" : "   (below gate — silent)").Append('\n');
            }

            sb.Append("Status: ").Append(_controller.StatusText).Append('\n');
            if (!string.IsNullOrEmpty(_controller.LastError)) sb.Append("Last error: ").Append(_controller.LastError).Append('\n');
            if (!string.IsNullOrEmpty(_controller.LastTranscript)) sb.Append("Heard: ").Append(_controller.LastTranscript).Append('\n');

            if (includeHint && _controller.IsCapturing && lvl <= 0.0001f)
                sb.Append(">> Capturing but level 0 — mic not delivering audio (permission/device).\n");
            else if (includeHint && _controller.IsCapturing && lvl > 0.0001f
                     && _controller.Mode == VoiceCaptureController.CaptureMode.VoiceActivity
                     && lvl < _controller.VadAmplitudeThreshold)
                sb.Append(">> Mic works but level is BELOW the VAD gate — nothing streams. Lower VAD " +
                          "threshold or switch to Push-To-Talk.\n");
            else if (includeHint && _controller.IsSessionOpen && !_controller.IsCapturing)
                sb.Append(">> Session open but NOT capturing — press Capture (or AutoReArm On) to stream, " +
                          "else the server closes it idle (1000 NormalClosure).\n");

            if (_events.Count > 0) sb.Append("· ").Append(_events[_events.Count - 1]);

            return sb.ToString();
        }

        // A 16-cell bar; the meter is scaled so the VAD threshold region is easy to see.
        private static string LevelBar(float level)
        {
            const int cells = 16;
            int filled = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(level / 0.25f) * cells), 0, cells);
            return "[" + new string('#', filled) + new string('-', cells - filled) + "]";
        }

        // --- Control handlers (wired to VR buttons) --------------------------------

        private void OnTogglePermission() => MicrophonePermission.Request(_ => { });

        private void OnToggleMode()
        {
            if (_controller == null) return;
            _controller.Mode = _controller.Mode == VoiceCaptureController.CaptureMode.PushToTalk
                ? VoiceCaptureController.CaptureMode.VoiceActivity
                : VoiceCaptureController.CaptureMode.PushToTalk;
        }

        private void OnToggleAutoReArm()
        {
            if (_controller != null) _controller.AutoReArm = !_controller.AutoReArm;
        }

        private void OnOpenSession() => _controller?.OpenSession();
        private void OnCloseSession() => _controller?.CloseSession();
        private void OnBeginCapture() => _controller?.BeginCapture();
        private void OnEndCapture() => _controller?.EndCapture();

        // Tune the voice-activity gate live (0..0.5) so a build tester with no Inspector can lower it
        // until their spoken level clears it. Setter clamps the range.
        private void OnVadDown() { if (_controller != null) _controller.VadAmplitudeThreshold -= 0.01f; }
        private void OnVadUp() { if (_controller != null) _controller.VadAmplitudeThreshold += 0.01f; }

        // --- Direct mic monitor (bypasses the entire STT stack) --------------------

        private void OnToggleMicTest()
        {
            if (_monitorActive) StopMicMonitor();
            else StartMicMonitor();
        }

        private void StartMicMonitor()
        {
            string[] devices = Microphone.devices;
            if (devices == null || devices.Length == 0) { PushEvent("MIC TEST: no device."); return; }

            // Only one recording per device is allowed — free the STT path first so they don't fight.
            if (_controller != null) _controller.CloseSession();

            string dev = _controller != null && !string.IsNullOrEmpty(_controller.MicrophoneDevice)
                         && System.Array.IndexOf(devices, _controller.MicrophoneDevice) >= 0
                         ? _controller.MicrophoneDevice : devices[0];

            // Rate 0 = the device's DEFAULT sample rate (max compatibility on Android/Quest, where a
            // forced 16 kHz request can be rejected). We only care whether ANY samples arrive.
            _monitorClip = Microphone.Start(dev, true, 1, 0);
            if (_monitorClip == null) { PushEvent($"MIC TEST: Microphone.Start returned null on '{dev}'."); return; }

            _monitorDevice = dev;
            _monitorBuffer = new float[Mathf.Max(1, _monitorClip.samples)];
            _monitorLastPos = 0;
            _monitorPos = 0;
            _monitorLevel = 0f;
            _monitorActive = true;
            PushEvent($"MIC TEST started on '{dev}' @ {_monitorClip.frequency} Hz.");
        }

        private void StopMicMonitor()
        {
            if (!string.IsNullOrEmpty(_monitorDevice) && Microphone.IsRecording(_monitorDevice))
                Microphone.End(_monitorDevice);
            _monitorActive = false;
            _monitorClip = null;
            _monitorBuffer = null;
            _monitorLevel = 0f;
            PushEvent("MIC TEST stopped.");
        }

        private void UpdateMicMonitor()
        {
            if (!_monitorActive || _monitorClip == null) return;
            _monitorPos = Microphone.GetPosition(_monitorDevice);
            if (_monitorPos < 0 || _monitorPos == _monitorLastPos) return;

            _monitorClip.GetData(_monitorBuffer, 0);
            int len = _monitorBuffer.Length;
            int count = _monitorPos >= _monitorLastPos
                ? _monitorPos - _monitorLastPos
                : (len - _monitorLastPos) + _monitorPos;

            float peak = 0f;
            for (int i = 0; i < count; i++)
            {
                float a = Mathf.Abs(_monitorBuffer[(_monitorLastPos + i) % len]);
                if (a > peak) peak = a;
            }
            _monitorLastPos = _monitorPos;
            _monitorLevel = peak;
        }

        // Cycle the selected device through [system default, dev0, dev1, …] — a single button is far
        // friendlier to a VR ray than a variable-length list of device buttons.
        private void OnCycleDevice()
        {
            if (_controller == null) return;
            var list = new List<string> { "" };
            list.AddRange(Microphone.devices);
            int idx = list.IndexOf(_controller.MicrophoneDevice ?? "");
            if (idx < 0) idx = 0;
            _controller.MicrophoneDevice = list[(idx + 1) % list.Count];
        }

        // --- World-space VR panel ---------------------------------------------------

        private void BuildWorldPanel()
        {
            // Root canvas. Pivot top-left so the whole panel hangs down-and-right from its anchor,
            // which is placed in the upper-left of the view — nothing lands in the middle of the screen.
            var go = new GameObject("VoiceDebug VR Panel", typeof(RectTransform), typeof(Canvas),
                                    typeof(TrackedDeviceGraphicRaycaster));
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var crt = (RectTransform)go.transform;
            crt.pivot = new Vector2(0f, 1f);
            crt.sizeDelta = new Vector2(720f, 660f);
            go.transform.localScale = Vector3.one * 0.00085f;   // ~0.61 m wide, legible in-headset

            AddImage(go.transform, new Color(0f, 0f, 0f, 0.78f), stretch: true);

            // Everything stacks vertically with padding, so the layout is self-arranging.
            var content = AddVerticalGroup(go.transform, padding: 14, spacing: 8, stretch: true);

            // Header: title + Hide/Show toggle.
            var header = AddHorizontalGroup(content, spacing: 8, minHeight: 54);
            MakeLabel(header, "VOICE DEBUG", 30f, TextAlignmentOptions.MidlineLeft, flexibleWidth: 1f);
            _toggleLabel = MakeButton(header, "Hide", ToggleBody, minWidth: 150f).label;

            // Body: all controls + readout (this is what Hide/Show collapses).
            _worldBody = AddVerticalGroup(content, padding: 0, spacing: 8, stretch: false).gameObject;
            SetFlexible(_worldBody, flexibleHeight: 1f);

            var bodyT = _worldBody.transform;

            var row1 = AddHorizontalGroup(bodyT, spacing: 8, minHeight: 56);
            _permLabel = MakeButton(row1, "Request Mic Perm", OnTogglePermission, flexibleWidth: 1f).label;
            _modeLabel = MakeButton(row1, "Mode", OnToggleMode, flexibleWidth: 1f).label;
            _autoLabel = MakeButton(row1, "AutoReArm", OnToggleAutoReArm, flexibleWidth: 1f).label;

            var row2 = AddHorizontalGroup(bodyT, spacing: 8, minHeight: 56);
            MakeButton(row2, "Open", OnOpenSession, flexibleWidth: 1f);
            MakeButton(row2, "Close", OnCloseSession, flexibleWidth: 1f);
            MakeButton(row2, "Capture", OnBeginCapture, flexibleWidth: 1f);
            MakeButton(row2, "Stop", OnEndCapture, flexibleWidth: 1f);

            var row3 = AddHorizontalGroup(bodyT, spacing: 8, minHeight: 56);
            _deviceLabel = MakeButton(row3, "Mic: default", OnCycleDevice, flexibleWidth: 1f).label;

            // VAD gate tuning — lets an in-headset tester lower the voice-activity threshold live.
            var row4 = AddHorizontalGroup(bodyT, spacing: 8, minHeight: 56);
            MakeButton(row4, "VAD -", OnVadDown, minWidth: 130f);
            _vadLabel = MakeLabel(row4, "VAD gate: 0.000", 24f, TextAlignmentOptions.Center, flexibleWidth: 1f);
            MakeButton(row4, "VAD +", OnVadUp, minWidth: 130f);

            // Direct mic test — reads the OS mic straight, bypassing consent/STT/socket/VAD.
            var row5 = AddHorizontalGroup(bodyT, spacing: 8, minHeight: 56);
            _monitorLabel = MakeButton(row5, "Mic Test (direct)", OnToggleMicTest, flexibleWidth: 1f).label;

            // Readout fills whatever height remains.
            _worldText = MakeLabel(bodyT, "", 28f, TextAlignmentOptions.TopLeft, flexibleWidth: 1f);
            SetFlexible(_worldText.gameObject, flexibleHeight: 1f);

            _bodyVisible = true;
            _worldRoot = go.transform;
            RefreshWorldLabels();
        }

        private void ToggleBody()
        {
            _bodyVisible = !_bodyVisible;
            if (_worldBody != null) _worldBody.SetActive(_bodyVisible);
            if (_toggleLabel != null) _toggleLabel.text = _bodyVisible ? "Hide" : "Show";
        }

        // --- Tiny uGUI construction helpers ----------------------------------------

        private static Image AddImage(Transform parent, Color color, bool stretch)
        {
            var go = new GameObject("Image", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            if (stretch)
            {
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                rt.offsetMin = rt.offsetMax = Vector2.zero;
            }
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private static Transform AddVerticalGroup(Transform parent, float padding, float spacing, bool stretch)
        {
            var go = new GameObject("VGroup", typeof(RectTransform), typeof(VerticalLayoutGroup));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            if (stretch)
            {
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                rt.offsetMin = rt.offsetMax = Vector2.zero;
            }
            var g = go.GetComponent<VerticalLayoutGroup>();
            g.padding = new RectOffset((int)padding, (int)padding, (int)padding, (int)padding);
            g.spacing = spacing;
            g.childControlWidth = g.childControlHeight = true;
            g.childForceExpandWidth = true;
            g.childForceExpandHeight = false;
            return go.transform;
        }

        private static Transform AddHorizontalGroup(Transform parent, float spacing, float minHeight)
        {
            var go = new GameObject("HGroup", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            go.transform.SetParent(parent, false);
            var g = go.GetComponent<HorizontalLayoutGroup>();
            g.spacing = spacing;
            g.childControlWidth = g.childControlHeight = true;
            g.childForceExpandWidth = false;
            g.childForceExpandHeight = true;
            SetLayout(go, minHeight: minHeight, flexibleHeight: 0f);
            return go.transform;
        }

        private static TextMeshProUGUI MakeLabel(Transform parent, string text, float size,
                                                 TextAlignmentOptions align, float flexibleWidth = 0f)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = Color.white;
            tmp.alignment = align;
            tmp.raycastTarget = false;
            if (flexibleWidth > 0f) SetLayout(go, flexibleWidth: flexibleWidth);
            return tmp;
        }

        private struct ButtonRef { public Button button; public TextMeshProUGUI label; }

        // A world-space uGUI button; its click runs through the canvas TrackedDeviceGraphicRaycaster, so a
        // VR ray interactor can press it. Sized by its parent layout group (minWidth / flexibleWidth hints).
        private static ButtonRef MakeButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick,
                                            float minWidth = 0f, float flexibleWidth = 0f)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            var img = go.GetComponent<Image>();
            img.color = new Color(0.16f, 0.35f, 0.62f, 0.95f);   // matches the sale-panel button blue

            var button = go.GetComponent<Button>();
            button.targetGraphic = img;
            button.onClick.AddListener(onClick);

            var text = MakeLabel(go.transform, label, 24f, TextAlignmentOptions.Center);
            var trt = (RectTransform)text.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;

            if (minWidth > 0f || flexibleWidth > 0f) SetLayout(go, minWidth: minWidth, flexibleWidth: flexibleWidth);
            return new ButtonRef { button = button, label = text };
        }

        private static void SetLayout(GameObject go, float minWidth = -1f, float minHeight = -1f,
                                      float flexibleWidth = -1f, float flexibleHeight = -1f)
        {
            var le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            if (minWidth >= 0f) le.minWidth = minWidth;
            if (minHeight >= 0f) le.minHeight = minHeight;
            if (flexibleWidth >= 0f) le.flexibleWidth = flexibleWidth;
            if (flexibleHeight >= 0f) le.flexibleHeight = flexibleHeight;
        }

        private static void SetFlexible(GameObject go, float flexibleHeight) =>
            SetLayout(go, flexibleHeight: flexibleHeight);

        private void DestroyWorldPanel()
        {
            if (_worldRoot != null) Destroy(_worldRoot.gameObject);
            _worldRoot = null;
            _worldBody = null;
            _worldText = _toggleLabel = _permLabel = _modeLabel = _autoLabel = _deviceLabel = _vadLabel =
                _monitorLabel = null;
        }

        // --- OnGUI overlay (desktop extra: Editor / PCVR Link mirror) --------------

        private void OnGUI()
        {
            if (!_showOverlay) return;

            Matrix4x4 old = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(_overlayScale, _overlayScale, 1f));

            GUILayout.BeginArea(new Rect(10, 10, 460, Screen.height / _overlayScale - 20), GUI.skin.box);
            _scroll = GUILayout.BeginScrollView(_scroll);

            GUILayout.Label("<b>VOICE CAPTURE DEBUG</b>  (desktop mirror — use the VR panel in-headset; F9 toggles this)");

            if (_controller == null)
            {
                GUILayout.Label("No VoiceCaptureController found in the scene.");
                GUILayout.EndScrollView();
                GUILayout.EndArea();
                GUI.matrix = old;
                return;
            }

            if (!MicrophonePermission.IsGranted && GUILayout.Button("Request OS Mic Permission"))
                MicrophonePermission.Request(_ => { });

            GUILayout.Space(4);
            GUILayout.Label("<b>Microphone device</b>");
            if (GUILayout.Button(DevicePrefix(string.IsNullOrEmpty(_controller.MicrophoneDevice)) + "System default"))
                _controller.MicrophoneDevice = "";
            foreach (string d in Microphone.devices)
                if (GUILayout.Button(DevicePrefix(_controller.MicrophoneDevice == d) + d))
                    _controller.MicrophoneDevice = d;

            GUILayout.Space(4);
            GUILayout.Label("<b>Capture</b>");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Mode: " + _controller.Mode)) OnToggleMode();
            if (GUILayout.Button("AutoReArm: " + _controller.AutoReArm)) OnToggleAutoReArm();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Open Session")) _controller.OpenSession();
            if (GUILayout.Button("Close Session")) _controller.CloseSession();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Begin Capture")) _controller.BeginCapture();
            if (GUILayout.Button("End Capture")) _controller.EndCapture();
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.Label("<b>Outcome</b>");
            GUILayout.Label(BuildReadoutText(includeHint: true));

            if (_events.Count > 0)
            {
                GUILayout.Label("<b>Recent events</b>");
                for (int i = _events.Count - 1; i >= 0; i--) GUILayout.Label("  " + _events[i]);
            }

            if (!string.IsNullOrEmpty(_controller.LogFilePath))
                GUILayout.Label("Log file: " + _controller.LogFilePath);

            GUILayout.EndScrollView();
            GUILayout.EndArea();
            GUI.matrix = old;
        }

        private static string DevicePrefix(bool selected) => selected ? "▶ " : "   ";
    }
}
