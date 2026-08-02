using System.Collections.Generic;
using System.Numerics;

namespace WorldGen.Notes.Data
{
    /// <summary>
    /// Where to point a board's camera so that EVERYTHING on it is on screen at once — the view a board opens
    /// with until the DM aims it themselves (DocBlock.CanvasViewSet / CanvasInlineViewSet).
    ///
    /// PURE, AND HERE RATHER THAN IN THE VIEW, for the reason the whole Data half of this layer exists: this
    /// is arithmetic with exactly one right answer, and arithmetic with one right answer belongs where it can
    /// be tested offline. The view's job is to hand over a viewport size and apply the two numbers back.
    ///
    /// THE COORDINATE SYSTEM IS THE CONTAINER'S, and the view is what makes that true. A board's objects live
    /// in a container whose anchor and pivot are both the viewport's CENTRE and whose own size is zero, so an
    /// object's Position is its CENTRE measured from that centre point, and the container's localScale is the
    /// zoom while its anchoredPosition is the pan. That is why `pan = -centre * zoom` below rather than
    /// `-centre`: the pan is applied to the already-scaled content, so it has to be expressed in scaled units.
    /// Getting this backwards is invisible at zoom 1 and wrong everywhere else, which is what SelfTestFit's
    /// fixture is built to expose.
    /// </summary>
    public static class CanvasFit
    {
        /// <summary>Never smaller than this, even when the content genuinely cannot fit. A board zoomed to
        /// nothing is not "everything visible", it is a grey rectangle — better to show part of a readable
        /// board than all of an unreadable one. Matches the floor the manual zoom already clamps to.</summary>
        public const float MinZoom = 0.25f;

        /// <summary>Never LARGER than 1, which is the half of "fit" that is easy to get wrong. Fitting is
        /// about making sure nothing is off screen; blowing one small card up to 3× to fill a pane is a
        /// different thing that nobody asked for, and it would make a board with a single note look broken.</summary>
        public const float MaxZoom = 1f;

        /// <summary>Breathing room on every side, in viewport pixels, so the outermost object does not sit flush
        /// against the frame's edge (or half under the inline frame's resize grip).</summary>
        public const float Margin = 24f;

        /// <param name="objects">The board's contents. Null or empty is an ORDINARY input, not an error — a
        /// board the DM just inserted has nothing on it, and the answer for that one is the neutral camera.</param>
        /// <param name="viewport">The visible area's size in pixels.</param>
        public static void Compute(IReadOnlyList<CanvasObjectData> objects, Vector2 viewport,
                                   out Vector2 pan, out float zoom)
        {
            pan = Vector2.Zero;
            zoom = 1f;
            if (objects == null || objects.Count == 0) return;

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            int counted = 0;
            foreach (var obj in objects)
            {
                if (obj == null) continue;
                // HALF THE SIZE ON EACH SIDE, because Position is the object's CENTRE (see the class doc).
                // Reading Position alone as the bounds is the mistake this measures against: it agrees with
                // the truth only when every object happens to be the same size, which two note cards are and
                // a note card beside a drawing is not.
                float halfW = obj.Size.X * 0.5f;
                float halfH = obj.Size.Y * 0.5f;
                if (obj.Position.X - halfW < minX) minX = obj.Position.X - halfW;
                if (obj.Position.Y - halfH < minY) minY = obj.Position.Y - halfH;
                if (obj.Position.X + halfW > maxX) maxX = obj.Position.X + halfW;
                if (obj.Position.Y + halfH > maxY) maxY = obj.Position.Y + halfH;
                counted++;
            }
            if (counted == 0) return;

            // Floors of 1 rather than 0: a board holding one zero-sized object would otherwise divide by zero
            // and hand the view a NaN scale, which uGUI accepts silently and then draws nothing at all.
            float contentW = System.Math.Max(1f, maxX - minX);
            float contentH = System.Math.Max(1f, maxY - minY);
            float availW = System.Math.Max(1f, viewport.X - Margin * 2f);
            float availH = System.Math.Max(1f, viewport.Y - Margin * 2f);

            // The SMALLER of the two ratios — the axis that runs out of room first is the one that decides.
            // Taking the larger fits the generous axis and pushes the other one off screen, which is the exact
            // opposite of what this method is for.
            zoom = System.Math.Min(availW / contentW, availH / contentH);
            if (zoom < MinZoom) zoom = MinZoom;
            if (zoom > MaxZoom) zoom = MaxZoom;

            var centre = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
            pan = -centre * zoom;
        }
    }
}
