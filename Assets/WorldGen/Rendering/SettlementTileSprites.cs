using UnityEngine;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    /// <summary>
    /// The ART SLOTS for the 2.5D settlement render (arc C.1, Task 7) — a plain serializable holder, NOT a
    /// MonoBehaviour, so it shows up as one inline block on <see cref="SettlementVolumeRenderer"/>'s inspector
    /// and can be swapped wholesale later without touching the renderer.
    ///
    /// The design's "art = sprite slots" rule: the user will DRAW the pixel-art tiles later, so art is never a
    /// blocker. Until a slot is filled the renderer draws its PROCEDURAL placeholder instead — a flat-ish top
    /// face plus darker side faces and a soft drop shadow, which reads as volume without any texture at all.
    /// A null slot is therefore the normal, expected state, not a misconfiguration: it must never throw and
    /// never blank a tile.
    ///
    /// TWO layers of fallback, deliberately separate:
    ///   - <see cref="useProcedural"/> is a MASTER override. While true (the default — there is no art yet)
    ///     every slot is ignored and every tile draws procedurally, so a half-finished tileset can sit in the
    ///     inspector without changing what the checkpoint sees. The user flips it off once the set is ready.
    ///   - With it false, each slot falls back INDIVIDUALLY: a null slot still draws its placeholder while its
    ///     filled neighbours draw art. That is what lets the tileset land one tile type at a time.
    ///
    /// Procedural drawing is not a separate code path in the renderer: a UnityEngine.UI.Image with a null
    /// sprite renders as a solid quad tinted by Image.color, so "placeholder" is literally "resolve the slot to
    /// null and set the colour". <see cref="Resolve"/> is the one place that decision is made.
    /// </summary>
    [System.Serializable]
    public class SettlementTileSprites
    {
        /// <summary>Master override: while true, EVERY slot below is ignored and every tile draws its
        /// procedural placeholder. Defaults true because no art exists yet (the beauty pass draws it later) —
        /// the DM checkpoint for this task is explicitly a placeholder checkpoint.</summary>
        public bool useProcedural = true;

        // ── Ground: one slot per TileType ────────────────────────────────────────────────────────────────
        // The tile's own footprint quad. For a FLAT tile (Road/Void/None) this is the whole visual; for an
        // EXTRUDED tile (Building/Wall/Gate) the renderer prefers roofSprite for the raised top face and falls
        // back to the matching ground slot here, so a tileset that only supplies ground art still renders.

        public Sprite noneGround;
        public Sprite buildingGround;
        public Sprite roadGround;
        public Sprite voidGround;
        public Sprite wallGround;
        public Sprite gateGround;

        // ── Volume ──────────────────────────────────────────────────────────────────────────────────────

        /// <summary>Top face of any extruded tile (the roof of a house, the walkway on top of the town wall).
        /// Falls back to the per-type ground slot above when null.</summary>
        public Sprite roofSprite;

        /// <summary>The VERTICAL side face of an extruded tile — "wall" as in the wall of a box, NOT
        /// <see cref="TileType.Wall"/>'s ground tile (that is <see cref="wallGround"/>). Used for both the
        /// south (front) face and the darker east strip; the renderer tints them differently.</summary>
        public Sprite wallSprite;

        /// <summary>Soft drop shadow under an extruded tile. Always tinted by the renderer's shadow colour
        /// even when supplied, so a plain white soft-edged blob is the right thing to author here.</summary>
        public Sprite shadowSprite;

        /// <summary>The ground slot for one tile type. TOTAL by construction — an unknown/future TileType
        /// member returns null (→ placeholder) rather than throwing, so adding an enum member can never crash
        /// the renderer mid-frame.</summary>
        public Sprite GroundFor(TileType t)
        {
            switch (t)
            {
                case TileType.Building: return buildingGround;
                case TileType.Road:     return roadGround;
                case TileType.Void:     return voidGround;
                case TileType.Wall:     return wallGround;
                case TileType.Gate:     return gateGround;
                case TileType.None:     return noneGround;
                default:                return null;
            }
        }

        /// <summary>Apply the master override to one slot: null (→ procedural placeholder) while
        /// <see cref="useProcedural"/> is set, otherwise the slot itself (which may still be null, giving the
        /// per-slot fallback). Every sprite the renderer assigns goes through here.</summary>
        public Sprite Resolve(Sprite slot) => useProcedural ? null : slot;

        /// <summary>Top-face sprite for an EXTRUDED tile: the shared roof art if supplied, else this type's own
        /// ground art, else null (placeholder). Already master-overridden.</summary>
        public Sprite RoofFor(TileType t)
        {
            if (useProcedural) return null;
            return roofSprite != null ? roofSprite : GroundFor(t);
        }
    }
}
