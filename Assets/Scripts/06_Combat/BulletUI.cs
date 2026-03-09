using UnityEngine;

public class BulletUI : MonoBehaviour
{
    [Header("Movement")]
    public RectTransform movementArea;   // the BoxMask
    public RectTransform soulRect;       // the soul rect
    public Vector2 direction = Vector2.down;
    public float speed = 200f;

    [Header("Damage")]
    public int damage = 5;

    [Header("Refs")]
    public PlayerSoul soul;              // explicit reference

    [Header("Behaviour")]
    public bool bounceInsideArea = false;    // TV-logo style when true

    [Header("Hitbox Scales (0–1 = smaller than sprite, >1 = larger)")]
    [Range(0.1f, 1.2f)]
    public float soulBoundsScale = 0.5f;         // how tight the soul hitbox is
    [Range(0.1f, 1.2f)]
    public float normalBulletBoundsScale = 0.6f; // default for small/medium bullets
    [Range(0.1f, 1.5f)]
    public float tvBulletBoundsScale = 0.9f;     // for big bouncing TV bullets

    private RectTransform _rect;

    private void Awake()
    {
        _rect = GetComponent<RectTransform>();
    }

    private void Update()
    {
        if (_rect == null || movementArea == null) return;

        Rect area = movementArea.rect;

        // ---------- MOVE ----------
        Vector2 pos = _rect.anchoredPosition;
        pos += direction.normalized * speed * Time.deltaTime;

        if (bounceInsideArea)
        {
            // Bouncing bullet (TV logo style)
            bool bouncedX = false;
            bool bouncedY = false;

            // left / right walls
            if (pos.x < area.xMin)
            {
                pos.x = area.xMin;
                direction = Vector2.Reflect(direction, Vector2.right);
                bouncedX = true;
            }
            else if (pos.x > area.xMax)
            {
                pos.x = area.xMax;
                direction = Vector2.Reflect(direction, Vector2.left);
                bouncedX = true;
            }

            // bottom / top walls
            if (pos.y < area.yMin)
            {
                pos.y = area.yMin;
                direction = Vector2.Reflect(direction, Vector2.up);
                bouncedY = true;
            }
            else if (pos.y > area.yMax)
            {
                pos.y = area.yMax;
                direction = Vector2.Reflect(direction, Vector2.down);
                bouncedY = true;
            }

            if (bouncedX || bouncedY)
            {
                // add some randomness to the reflected direction
                float angleOffset = Random.Range(-40f, 40f); // degrees
                float rad = angleOffset * Mathf.Deg2Rad;
                float cos = Mathf.Cos(rad);
                float sin = Mathf.Sin(rad);

                Vector2 d = direction.normalized;
                Vector2 rotated = new Vector2(
                    d.x * cos - d.y * sin,
                    d.x * sin + d.y * cos
                );

                direction = rotated.normalized;

                // tiny nudge so we don't get stuck on corners
                pos += direction * (speed * Time.deltaTime * 0.1f);
            }
        }
        else
        {
            // normal bullets: despawn when leaving area
            if (pos.x < area.xMin - 50f || pos.x > area.xMax + 50f ||
                pos.y < area.yMin - 50f || pos.y > area.yMax + 50f)
            {
                Destroy(gameObject);
                return;
            }
        }

        _rect.anchoredPosition = pos;

        // ---------- COLLISION WITH SOUL ----------
        if (soulRect != null)
        {
            // Build world-space rects and scale them
            Rect soulBounds = GetWorldRectScaled(soulRect, soulBoundsScale);
            float bulletScale = bounceInsideArea ? tvBulletBoundsScale : normalBulletBoundsScale;
            Rect bulletBounds = GetWorldRectScaled(_rect, bulletScale);

            if (soulBounds.Overlaps(bulletBounds))
            {
                // deal damage
                if (soul != null)
                {
                    soul.TakeHit(damage);
                }
                else
                {
                    // Fallback: try to get PlayerSoul from soulRect
                    var ps = soulRect.GetComponent<PlayerSoul>();
                    if (ps != null)
                        ps.TakeHit(damage);
                }

                // Normal bullets disappear, bouncing/TV bullets stay
                if (!bounceInsideArea)
                {
                    Destroy(gameObject);
                }
            }
        }
    }

    /// <summary>
    /// Returns a world-space Rect for the RectTransform, scaled around its center.
    /// scale = 1 => full sprite, 0.5 => half-size hitbox centered, etc.
    /// </summary>
    private Rect GetWorldRectScaled(RectTransform rt, float scale)
    {
        Vector3[] corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        // corners: 0=bottom-left, 2=top-right in screen space for default canvas
        Vector2 min = corners[0];
        Vector2 max = corners[2];

        Vector2 size = max - min;
        Vector2 center = (min + max) * 0.5f;

        size *= Mathf.Clamp(scale, 0.01f, 2f);
        Vector2 newMin = center - size * 0.5f;

        return new Rect(newMin, size);
    }
}

