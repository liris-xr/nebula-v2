using System.Collections;
using UnityEngine;

/// <summary>
/// Attach to an odor-emitting object. Diffuses while the player's head (a NebulaPlayerHead)
/// is within `triggerDistance` metres of this object. The check is a plain distance test
/// polled every frame: no collider, Rigidbody or layer setup is involved, so it cannot
/// silently fail the way trigger overlap can (missing Rigidbody, layer matrix, XR rig
/// reparenting, etc.).
///   Binary : fixed duty cycle while in range.
///   Linear : duty scales with the head-to-object distance.
///
/// This is the original nebula-v2 distance-triggered odor script, ported against the
/// BleBridge-backed NebulaManager API (NebulaManager.Instance.Configure / StartDiffusion /
/// StopDiffusion are identical), with an IsConnected guard so diffusion waits for the
/// bridge to connect and resumes by itself once it has.
/// </summary>
public class NebulaOdorZone : MonoBehaviour
{
    public enum DiffusionMode { Binary, Linear }

    [Header("Atomizer")]
    public NebulaAtomizer atomizer = NebulaAtomizer.L1;

    [Header("Trigger")]
    [Tooltip("Distance (m) between the head and this object below which diffusion runs.")]
    public float triggerDistance = 0.45f;

    [Header("Diffusion")]
    public DiffusionMode mode = DiffusionMode.Binary;
    [Tooltip("Square signal period sent to the firmware (ms).")]
    public int periodMs = 100;
    [Tooltip("Duty cycle used in Binary mode.")]
    [Range(0, 100)] public int binaryDutyCycle = 50;

    [Header("Linear mode")]
    [Range(0, 100)] public int minimumDutyCycle = 1;
    [Range(0, 100)] public int maximumDutyCycle = 30;
    [Tooltip("Distance (m) at which the duty is maximal.")]
    public float distanceAtMaxDuty = 0.1f;
    [Tooltip("Distance (m) at which the duty is minimal. Keep <= triggerDistance so the duty reaches its minimum at the edge of the zone.")]
    public float distanceAtMinDuty = 0.45f;
    [Tooltip("Duty update interval (s). Avoids saturating the link.")]
    public float updateInterval = 0.1f;

    public bool IsDiffusing { get; private set; }

    private bool _inRange;
    private int _lastSentDuty = -1;
    private Coroutine _routine;

    private void Update()
    {
        Transform head = NebulaPlayerHead.Head;
        bool inRange = head != null &&
            (head.position - transform.position).sqrMagnitude <= triggerDistance * triggerDistance;

        if (!inRange)
        {
            if (_inRange)
            {
                _inRange = false;
                StopDiffusion();
            }
            return;
        }

        _inRange = true;
        // StartDiffusion is a no-op while already diffusing, so calling it every frame also
        // picks up a bridge that finishes connecting after the head entered range.
        if (NebulaManager.Instance != null && NebulaManager.Instance.IsConnected) StartDiffusion();
    }

    public void StartDiffusion()
    {
        if (IsDiffusing || NebulaManager.Instance == null) return;
        // Don't begin diffusion until the bridge has connected the Nebula device; otherwise
        // Configure/StartDiffusion would warn that the transport isn't connected.
        if (!NebulaManager.Instance.IsConnected) return;
        IsDiffusing = true;
        int duty = mode == DiffusionMode.Linear ? ComputeDuty() : binaryDutyCycle;
        NebulaManager.Instance.Configure(atomizer, periodMs, duty);
        NebulaManager.Instance.StartDiffusion(atomizer);
        _lastSentDuty = duty;
        if (mode == DiffusionMode.Linear) _routine = StartCoroutine(TrackDistance());
    }

    public void StopDiffusion()
    {
        if (!IsDiffusing) return;
        IsDiffusing = false;
        if (_routine != null) { StopCoroutine(_routine); _routine = null; }
        NebulaManager.Instance?.StopDiffusion(atomizer);
        _lastSentDuty = -1;
    }

    // Updates the duty from the head distance, sending only when the value changes.
    private IEnumerator TrackDistance()
    {
        var wait = new WaitForSeconds(updateInterval);
        while (IsDiffusing)
        {
            yield return wait;
            int duty = ComputeDuty();
            if (duty != _lastSentDuty)
            {
                NebulaManager.Instance?.Configure(atomizer, periodMs, duty);
                _lastSentDuty = duty;
            }
        }
    }

    private int ComputeDuty()
    {
        Transform head = NebulaPlayerHead.Head;
        if (head == null) return minimumDutyCycle;
        float distance = Vector3.Distance(head.position, transform.position);
        float t = Mathf.InverseLerp(distanceAtMinDuty, distanceAtMaxDuty, distance); // 0 = far, 1 = near
        return Mathf.RoundToInt(Mathf.Lerp(minimumDutyCycle, maximumDutyCycle, t));
    }

    private void OnDisable()
    {
        _inRange = false;
        StopDiffusion();
    }
}
