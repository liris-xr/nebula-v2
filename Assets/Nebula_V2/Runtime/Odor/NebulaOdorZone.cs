using System.Collections;
using UnityEngine;

/// <summary>
/// Attach to an odor-emitting object. Diffuses while the player's head (a NebulaPlayerHead)
/// is inside this object's trigger collider.
///   Binary : fixed duty cycle while inside.
///   Linear : duty scales with the head-to-object distance.
/// </summary>
[RequireComponent(typeof(Collider))]
public class NebulaOdorZone : MonoBehaviour
{
    public enum DiffusionMode { Binary, Linear }

    [Header("Atomizer")]
    public NebulaAtomizer atomizer = NebulaAtomizer.L1;

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
    [Tooltip("Distance (m) at which the duty is minimal.")]
    public float distanceAtMinDuty = 0.45f;
    [Tooltip("Duty update interval (s). Avoids saturating the link.")]
    public float updateInterval = 0.1f;

    public bool IsDiffusing { get; private set; }

    private Transform _head;
    private int _lastSentDuty = -1;
    private Coroutine _routine;

    private void OnTriggerEnter(Collider other)
    {
        if (other.GetComponent<NebulaPlayerHead>() == null) return;
        _head = other.transform;
        StartDiffusion();
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.GetComponent<NebulaPlayerHead>() == null) return;
        StopDiffusion();
    }

    public void StartDiffusion()
    {
        if (IsDiffusing || NebulaManager.Instance == null) return;
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
        if (_head == null) return minimumDutyCycle;
        float distance = Vector3.Distance(_head.position, transform.position);
        float t = Mathf.InverseLerp(distanceAtMinDuty, distanceAtMaxDuty, distance); // 0 = far, 1 = near
        return Mathf.RoundToInt(Mathf.Lerp(minimumDutyCycle, maximumDutyCycle, t));
    }

    private void OnDisable() => StopDiffusion();
}
