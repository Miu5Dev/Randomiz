using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Collision info returned after movement
/// </summary>
public struct CollisionInfo
{
    public bool hit;
    public Vector3 point;
    public Vector3 normal;
    public float angle;        // Angle from up vector
    public Collider collider;
    public GameObject gameObject;
    
    public bool IsGround(float maxAngle) => hit && angle <= maxAngle;
    public bool IsWall(float maxGroundAngle, float maxCeilingAngle = 135f) => hit && angle > maxGroundAngle && angle < maxCeilingAngle;
    public bool IsCeiling(float minAngle = 135f) => hit && angle >= minAngle;
}

/// <summary>
/// Ground state information
/// </summary>
public struct GroundInfo
{
    public bool isGrounded;
    public bool isOnSlope;
    public bool isSteepSlope;
    public Vector3 normal;
    public Vector3 point;
    public float angle;
    public float distance;
    public Collider collider;
    public GameObject gameObject;
    
    public Vector3 GetMoveDirection(Vector3 direction)
    {
        if (!isGrounded) return direction;
        return Vector3.ProjectOnPlane(direction, normal).normalized;
    }
    
    public Vector3 GetSlopeDirection()
    {
        if (!isOnSlope) return Vector3.zero;
        return Vector3.ProjectOnPlane(Vector3.down, normal).normalized;
    }
}

/// <summary>
/// Result of a Move operation
/// </summary>
public struct MoveResult
{
    public Vector3 startPosition;
    public Vector3 endPosition;
    public Vector3 attemptedMovement;
    public Vector3 actualMovement;
    public bool collided;
    public List<CollisionInfo> collisions;
    
    public bool HitGround(float maxAngle)
    {
        foreach (var c in collisions)
            if (c.IsGround(maxAngle)) return true;
        return false;
    }
    
    public bool HitWall(float maxGroundAngle)
    {
        foreach (var c in collisions)
            if (c.IsWall(maxGroundAngle)) return true;
        return false;
    }
    
    public bool HitCeiling(float minAngle = 135f)
    {
        foreach (var c in collisions)
            if (c.IsCeiling(minAngle)) return true;
        return false;
    }
    
    public CollisionInfo? GetGroundCollision(float maxAngle)
    {
        foreach (var c in collisions)
            if (c.IsGround(maxAngle)) return c;
        return null;
    }
    
    public CollisionInfo? GetWallCollision(float maxGroundAngle)
    {
        foreach (var c in collisions)
            if (c.IsWall(maxGroundAngle)) return c;
        return null;
    }
}

/// <summary>
/// Core physics controller - handles movement, collisions, and provides data.
/// Does NOT impose any movement logic. You build that on top.
/// </summary>
public class PhysicsController : MonoBehaviour
{
    [Header("Collision Settings")]
    public float skinWidth = 0.02f;
    public int maxMoveIterations = 3;
    public int maxOverlapIterations = 3;
    public LayerMask collisionMask = ~0;
    
    [Header("Ground Detection")]
    public float groundCheckDistance = 0.1f;
    public float maxGroundAngle = 45f;
    
    [Header("Ground Movement")]
    public bool autoSlopeHandling = true;
    public float groundSnapDistance = 0.3f;
    
    // Components
    private CapsuleCollider col;
    
    // Cached data
    private GroundInfo groundInfo;
    private List<CollisionInfo> frameCollisions = new List<CollisionInfo>();
    
    // Public access
    public CapsuleCollider Collider => col;
    public GroundInfo Ground => groundInfo;
    public IReadOnlyList<CollisionInfo> FrameCollisions => frameCollisions;
    
    void Awake()
    {
        col = GetComponent<CapsuleCollider>();
        if (col == null)
        {
            Debug.LogError("PhysicsController requires a CapsuleCollider!");
        }
    }
    
    void FixedUpdate()
    {
        // Clear frame data
        frameCollisions.Clear();
        
        // Update ground state
        groundInfo = CheckGround();
    }
    
    #region Ground Detection
    
    /// <summary>
    /// Check ground beneath the character
    /// </summary>
    public GroundInfo CheckGround()
    {
        return CheckGround(groundCheckDistance);
    }
    
    /// <summary>
    /// Check ground with custom distance
    /// </summary>
    public GroundInfo CheckGround(float distance)
    {
        GroundInfo info = new GroundInfo();
        
        float radius = col.radius - skinWidth;
        Vector3 origin = GetFeetPosition() + Vector3.up * (radius + skinWidth);
        
        if (Physics.SphereCast(origin, radius, Vector3.down, out RaycastHit hit, distance + skinWidth, collisionMask, QueryTriggerInteraction.Ignore))
        {
            info.normal = hit.normal;
            info.point = hit.point;
            info.angle = Vector3.Angle(hit.normal, Vector3.up);
            info.distance = hit.distance - skinWidth;
            info.collider = hit.collider;
            info.gameObject = hit.collider.gameObject;
            info.isOnSlope = info.angle > 0.1f;
            info.isSteepSlope = info.angle > maxGroundAngle;
            info.isGrounded = info.distance <= groundCheckDistance && !info.isSteepSlope;
        }
        
        return info;
    }
    
    /// <summary>
    /// Check ground in a specific direction (for local gravity)
    /// </summary>
    public GroundInfo CheckGroundInDirection(Vector3 direction, float distance)
    {
        GroundInfo info = new GroundInfo();
        
        float radius = col.radius - skinWidth;
        Vector3 origin = transform.position + col.center - direction.normalized * (col.height * 0.5f - col.radius) + (-direction.normalized) * skinWidth;
        
        if (Physics.SphereCast(origin, radius, direction.normalized, out RaycastHit hit, distance + skinWidth, collisionMask, QueryTriggerInteraction.Ignore))
        {
            info.normal = hit.normal;
            info.point = hit.point;
            info.angle = Vector3.Angle(hit.normal, -direction);
            info.distance = hit.distance - skinWidth;
            info.collider = hit.collider;
            info.gameObject = hit.collider.gameObject;
            info.isOnSlope = info.angle > 0.1f;
            info.isSteepSlope = info.angle > maxGroundAngle;
            info.isGrounded = info.distance <= groundCheckDistance && !info.isSteepSlope;
        }
        
        return info;
    }
    
    #endregion
    
    #region Surface Detection
    
    /// <summary>
    /// Check for a surface in any direction
    /// </summary>
    public CollisionInfo CheckDirection(Vector3 direction, float distance)
    {
        CollisionInfo info = new CollisionInfo();
        
        float radius = col.radius - skinWidth;
        Vector3 bottom = GetCapsulePoint(false);
        Vector3 top = GetCapsulePoint(true);
        
        if (Physics.CapsuleCast(bottom, top, radius, direction.normalized, out RaycastHit hit, distance + skinWidth, collisionMask, QueryTriggerInteraction.Ignore))
        {
            info.hit = true;
            info.point = hit.point;
            info.normal = hit.normal;
            info.angle = Vector3.Angle(hit.normal, Vector3.up);
            info.collider = hit.collider;
            info.gameObject = hit.collider.gameObject;
        }
        
        return info;
    }
    
    /// <summary>
    /// Check for walls around the character
    /// </summary>
    public CollisionInfo CheckWall(Vector3 direction)
    {
        return CheckDirection(direction, skinWidth * 2f);
    }
    
    /// <summary>
    /// Check for ceiling above
    /// </summary>
    public CollisionInfo CheckCeiling(float distance = 0.1f)
    {
        return CheckDirection(Vector3.up, distance);
    }
    
    /// <summary>
    /// Get all surfaces currently touching the character
    /// </summary>
    public List<CollisionInfo> GetTouchingSurfaces()
    {
        List<CollisionInfo> surfaces = new List<CollisionInfo>();
        
        Collider[] overlaps = Physics.OverlapCapsule(
            GetCapsulePoint(false),
            GetCapsulePoint(true),
            col.radius + skinWidth,
            collisionMask,
            QueryTriggerInteraction.Ignore
        );
        
        foreach (Collider other in overlaps)
        {
            if (other == col) continue;
            
            if (Physics.ComputePenetration(
                col, transform.position, transform.rotation,
                other, other.transform.position, other.transform.rotation,
                out Vector3 dir, out float dist))
            {
                surfaces.Add(new CollisionInfo
                {
                    hit = true,
                    normal = dir,
                    angle = Vector3.Angle(dir, Vector3.up),
                    collider = other,
                    gameObject = other.gameObject
                });
            }
        }
        
        return surfaces;
    }
    
    #endregion
    
    #region Movement
    
    /// <summary>
    /// Move the character with collision detection and resolution.
    /// Returns detailed info about the movement and collisions.
    /// </summary>
    public MoveResult Move(Vector3 motion)
    {
        MoveResult result = new MoveResult
        {
            startPosition = transform.position,
            attemptedMovement = motion,
            collisions = new List<CollisionInfo>()
        };

        // --- AUTO SLOPE HANDLING ---
        if (autoSlopeHandling && groundInfo.isGrounded)
        {
            // Si motion.y es positivo, es un salto → no manipular
            if (motion.y > 0.001f)
            {
                // Salto: dejamos motion intacto, desactivamos snap este frame
                // No hacemos nada, motion pasa tal cual
            }
            else
            {
                Vector3 horizontal = new Vector3(motion.x, 0f, motion.z);

                if (horizontal.sqrMagnitude > 0.0001f && groundInfo.isOnSlope)
                {
                    Vector3 projected = Vector3.ProjectOnPlane(horizontal, groundInfo.normal);
                    motion = projected.normalized * horizontal.magnitude;
                }
                else if (horizontal.sqrMagnitude > 0.0001f)
                {
                    motion = horizontal;
                }
                else
                {
                    motion = Vector3.zero;
                }
            }
        }
        // --- END SLOPE HANDLING ---

        if (motion.sqrMagnitude < 0.00001f)
        {
            if (autoSlopeHandling && groundInfo.isGrounded)
            {
                SnapToGround(groundSnapDistance);
            }
            result.endPosition = transform.position;
            result.actualMovement = Vector3.zero;
            return result;
        }

        Vector3 remainingMotion = motion;

        for (int i = 0; i < maxMoveIterations && remainingMotion.sqrMagnitude > 0.0001f; i++)
        {
            float distance = remainingMotion.magnitude;
            Vector3 direction = remainingMotion.normalized;

            float radius = col.radius - skinWidth;
            Vector3 bottom = GetCapsulePoint(false);
            Vector3 top = GetCapsulePoint(true);

            if (Physics.CapsuleCast(bottom, top, radius, direction, out RaycastHit hit,
                distance + skinWidth, collisionMask, QueryTriggerInteraction.Ignore))
            {
                float safeDistance = Mathf.Max(0, hit.distance - skinWidth);
                transform.position += direction * safeDistance;

                CollisionInfo collision = new CollisionInfo
                {
                    hit = true,
                    point = hit.point,
                    normal = hit.normal,
                    angle = Vector3.Angle(hit.normal, Vector3.up),
                    collider = hit.collider,
                    gameObject = hit.collider.gameObject
                };
                result.collisions.Add(collision);
                frameCollisions.Add(collision);

                float usedDistance = safeDistance;
                float leftoverDistance = distance - usedDistance;

                if (leftoverDistance > 0.001f)
                {
                    Vector3 leftoverMotion = direction * leftoverDistance;
                    remainingMotion = Vector3.ProjectOnPlane(leftoverMotion, hit.normal);
                }
                else
                {
                    remainingMotion = Vector3.zero;
                }

                result.collided = true;
            }
            else
            {
                transform.position += remainingMotion;
                remainingMotion = Vector3.zero;
            }
        }

        ResolveOverlaps(result.collisions);

        // --- GROUND SNAP: solo si NO estamos saltando ---
        if (autoSlopeHandling && groundInfo.isGrounded && motion.y <= 0.001f)
        {
            SnapToGround(groundSnapDistance);
        }
        // --- END GROUND SNAP ---

        result.endPosition = transform.position;
        result.actualMovement = result.endPosition - result.startPosition;

        return result;
    }
    
    /// <summary>
    /// Move without collision (teleport)
    /// </summary>
    public void MoveRaw(Vector3 motion)
    {
        transform.position += motion;
    }
    
    /// <summary>
    /// Teleport to position
    /// </summary>
    public void SetPosition(Vector3 position)
    {
        transform.position = position;
    }
    
    /// <summary>
    /// Snap to ground if close enough
    /// </summary>
    public bool SnapToGround(float maxDistance)
    {
        GroundInfo ground = CheckGround(maxDistance);
        
        if (ground.distance > 0 && ground.distance <= maxDistance && !ground.isSteepSlope)
        {
            transform.position -= Vector3.up * ground.distance;
            groundInfo = CheckGround();
            return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Snap to any surface in direction
    /// </summary>
    public bool SnapToSurface(Vector3 direction, float maxDistance)
    {
        CollisionInfo surface = CheckDirection(direction, maxDistance);
        
        if (surface.hit)
        {
            float radius = col.radius;
            transform.position += direction.normalized * (maxDistance - radius);
            ResolveOverlaps();
            return true;
        }
        
        return false;
    }
    
    #endregion
    
    #region Overlap Resolution
    
    /// <summary>
    /// Push out of any overlapping colliders
    /// </summary>
    public void ResolveOverlaps()
    {
        ResolveOverlaps(null);
    }
    
    private void ResolveOverlaps(List<CollisionInfo> collisionList)
    {
        for (int i = 0; i < maxOverlapIterations; i++)
        {
            bool resolved = true;
            
            Collider[] overlaps = Physics.OverlapCapsule(
                GetCapsulePoint(false),
                GetCapsulePoint(true),
                col.radius,
                collisionMask,
                QueryTriggerInteraction.Ignore
            );
            
            foreach (Collider other in overlaps)
            {
                if (other == col) continue;
                
                if (Physics.ComputePenetration(
                    col, transform.position, transform.rotation,
                    other, other.transform.position, other.transform.rotation,
                    out Vector3 dir, out float dist))
                {
                    transform.position += dir * (dist + skinWidth);
                    
                    if (collisionList != null)
                    {
                        CollisionInfo collision = new CollisionInfo
                        {
                            hit = true,
                            normal = dir,
                            angle = Vector3.Angle(dir, Vector3.up),
                            collider = other,
                            gameObject = other.gameObject
                        };
                        collisionList.Add(collision);
                        frameCollisions.Add(collision);
                    }
                    
                    resolved = false;
                }
            }
            
            if (resolved) break;
        }
    }
    
    #endregion
    
    #region Utility
    
    public Vector3 GetFeetPosition()
    {
        return transform.position + col.center - Vector3.up * (col.height * 0.5f);
    }
    
    public Vector3 GetHeadPosition()
    {
        return transform.position + col.center + Vector3.up * (col.height * 0.5f);
    }
    
    public Vector3 GetCenterPosition()
    {
        return transform.position + col.center;
    }
    
    public Vector3 GetCapsulePoint(bool top)
    {
        float offset = col.height * 0.5f - col.radius;
        Vector3 dir = top ? Vector3.up : Vector3.down;
        return transform.position + col.center + dir * offset;
    }
    
    /// <summary>
    /// Project a direction onto the current ground plane
    /// </summary>
    public Vector3 ProjectOnGround(Vector3 direction)
    {
        if (!groundInfo.isGrounded) return direction;
        return Vector3.ProjectOnPlane(direction, groundInfo.normal).normalized * direction.magnitude;
    }
    
    /// <summary>
    /// Get the downhill direction on current slope
    /// </summary>
    public Vector3 GetSlopeDownDirection()
    {
        if (!groundInfo.isOnSlope) return Vector3.zero;
        return Vector3.ProjectOnPlane(Vector3.down, groundInfo.normal).normalized;
    }
    
    /// <summary>
    /// Check if a movement would hit something
    /// </summary>
    public bool WouldCollide(Vector3 motion, out CollisionInfo collision)
    {
        collision = CheckDirection(motion.normalized, motion.magnitude);
        return collision.hit;
    }
    
    #endregion
    
    #region Debug
    
    void OnDrawGizmosSelected()
    {
        if (col == null) col = GetComponent<CapsuleCollider>();
        if (col == null) return;
        
        // Draw capsule
        Gizmos.color = Color.green;
        DrawCapsuleGizmo(GetCapsulePoint(false), GetCapsulePoint(true), col.radius);
        
        // Draw ground check
        Gizmos.color = groundInfo.isGrounded ? Color.green : (groundInfo.isSteepSlope ? Color.yellow : Color.red);
        Vector3 feetPos = GetFeetPosition();
        Gizmos.DrawLine(feetPos, feetPos + Vector3.down * groundCheckDistance);
        
        // Draw ground normal
        if (groundInfo.isGrounded || groundInfo.isSteepSlope)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawRay(groundInfo.point, groundInfo.normal * 0.5f);
            
            // Draw slope direction
            Gizmos.color = Color.red;
            Gizmos.DrawRay(groundInfo.point, GetSlopeDownDirection() * 0.5f);
        }
    }
    
    private void DrawCapsuleGizmo(Vector3 bottom, Vector3 top, float radius)
    {
        Gizmos.DrawWireSphere(bottom, radius);
        Gizmos.DrawWireSphere(top, radius);
        Gizmos.DrawLine(bottom + Vector3.forward * radius, top + Vector3.forward * radius);
        Gizmos.DrawLine(bottom - Vector3.forward * radius, top - Vector3.forward * radius);
        Gizmos.DrawLine(bottom + Vector3.right * radius, top + Vector3.right * radius);
        Gizmos.DrawLine(bottom - Vector3.right * radius, top - Vector3.right * radius);
    }
    
    #endregion
}