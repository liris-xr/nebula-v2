using UnityEngine;

/// <summary>
/// Put this on the player's head (VR camera). It registers the head transform as the odor
/// detector: NebulaOdorZone reads NebulaPlayerHead.Head and triggers on distance, so no
/// collider, Rigidbody or physics layer setup is needed anywhere.
/// </summary>
public class NebulaPlayerHead : MonoBehaviour
{
    /// <summary>Transform of the player's head, set by the instance in the scene.</summary>
    public static Transform Head { get; private set; }

    private void OnEnable() => Head = transform;
    private void OnDisable() { if (Head == transform) Head = null; }
    private void OnDestroy() { if (Head == transform) Head = null; }
}
