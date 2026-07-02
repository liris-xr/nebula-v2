using UnityEngine;

/// <summary>
/// Put this on the player's head (VR camera). It marks the head as the odor detector:
/// NebulaOdorZone finds it via GetComponent&lt;NebulaPlayerHead&gt;() on trigger overlap.
///
/// It also sets up a trigger SphereCollider and a kinematic Rigidbody, which Unity
/// requires for trigger callbacks to fire between the head and the odor zones.
/// </summary>
[RequireComponent(typeof(SphereCollider))]
public class NebulaPlayerHead : MonoBehaviour
{
    [Tooltip("Radius of the head trigger sphere (m).")]
    public float radius = 0.3f;

    private void Awake()
    {
        var sphere = GetComponent<SphereCollider>();
        sphere.isTrigger = true;
        sphere.radius = radius;

        // Trigger callbacks require a Rigidbody on one of the two colliders.
        var rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
    }
}
