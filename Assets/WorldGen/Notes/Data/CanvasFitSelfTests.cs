using System.Collections.Generic;
using System.Numerics;
using UnityEngine;

namespace WorldGen.Notes.Data
{
    /// <summary>
    /// Self-tests for "open a board showing everything on it". Runs two ways, as every suite here does:
    /// right-click this component in the Editor, or offline via Tools/notes-harness.
    ///
    /// THE FIXTURE IS BUILT SO THE WRONG RULES DISAGREE WITH THE RIGHT ONE, which is the only thing that
    /// makes these assertions worth writing. Four specific mistakes are in scope, and each one is a plausible
    /// edit rather than an invented one:
    ///   1. bounds from Position alone, forgetting that Position is a CENTRE and objects have width;
    ///   2. the LARGER of the two axis ratios instead of the smaller;
    ///   3. pan = -centre, forgetting that the pan is applied to already-scaled content;
    ///   4. no upper clamp, so one small card is blown up to fill a pane.
    /// The two objects therefore have DIFFERENT sizes (so 1 moves the centre AND the width), the content is
    /// wide and short against a viewport that is neither (so 2 changes the zoom), and the content's centre is
    /// away from the origin at a zoom that is not 1 (so 3 changes the pan). A fixture of two identical cards
    /// symmetric about the origin would pass all four rules and prove nothing.
    /// </summary>
    public class CanvasFitSelfTests : MonoBehaviour
    {
        static NoteCardData Card(float x, float y, float w, float h)
            => new NoteCardData { Position = new Vector2(x, y), Size = new Vector2(w, h) };

        static bool Near(float a, float b) => System.Math.Abs(a - b) < 0.01f;

        [ContextMenu("Self-Test: Canvas Fit")]
        public void SelfTestCanvasFit()
        {
            bool ok = true;

            // X: card A spans [-150, -50], card B spans [150, 450]  -> content 600 wide, centred on x = 150.
            // Y: card A spans [-25, 25],   card B spans [75, 125]   -> content 150 tall, centred on y = 50.
            // Reading Position alone would give x-bounds [-100, 300]: centre 100, width 400. Both wrong.
            var objects = new List<CanvasObjectData>
            {
                Card(-100f, 0f, 100f, 50f),
                Card(300f, 100f, 300f, 50f),
            };

            // 400x300 viewport, 24px margin each side -> 352 x 252 usable.
            //   x ratio = 352/600 = 0.58667   <- the binding one
            //   y ratio = 252/150 = 1.68      <- would be clamped to 1 if the larger were taken
            CanvasFit.Compute(objects, new Vector2(400f, 300f), out var pan, out float zoom);

            if (!Near(zoom, 0.58667f))
            {
                Debug.LogError($"FAIL fit zoom: {zoom}, want 0.58667 — the WIDTH is what runs out first. " +
                               "1.68 (or its clamp, 1) means the larger ratio was taken; 0.88 means the " +
                               "bounds were read from Position alone, ignoring that objects have width.");
                ok = false;
            }

            // pan = -centre * zoom = -(150, 50) * 0.58667 = (-88, -29.33).
            // Forgetting the * zoom gives (-150, -50) — off by 62 pixels, and invisible at zoom 1.
            if (!Near(pan.X, -88f) || !Near(pan.Y, -29.333f))
            {
                Debug.LogError($"FAIL fit pan: {pan}, want (-88, -29.33) — the pan is applied to content that " +
                               "is ALREADY scaled, so it is the centre times the zoom, not the centre. " +
                               "(-150, -50) means the zoom was left out; (-58.7, -29.3) means the centre was " +
                               "taken from Position alone.");
                ok = false;
            }

            // A single small card in a big pane: fitting must not MAGNIFY it. Both ratios are far above 1
            // here (752/100 and 552/50), so an unclamped rule would hand back 5.52 after the min.
            CanvasFit.Compute(new List<CanvasObjectData> { Card(0f, 0f, 100f, 50f) },
                              new Vector2(800f, 600f), out var smallPan, out float smallZoom);
            if (!Near(smallZoom, 1f))
            {
                Debug.LogError($"FAIL fit of one small card: zoom {smallZoom}, want 1 — fitting means nothing " +
                               "is off screen, not that the content is stretched to fill the pane.");
                ok = false;
            }
            if (!Near(smallPan.X, 0f) || !Near(smallPan.Y, 0f))
            { Debug.LogError($"FAIL fit of one small card: pan {smallPan}, want (0, 0)"); ok = false; }

            // Content far too big for the frame: the floor holds, and the content is still CENTRED — the DM
            // gets a readable middle rather than an unreadable whole.
            CanvasFit.Compute(new List<CanvasObjectData> { Card(1000f, 0f, 10000f, 100f) },
                              new Vector2(400f, 300f), out var hugePan, out float hugeZoom);
            if (!Near(hugeZoom, CanvasFit.MinZoom))
            { Debug.LogError($"FAIL fit of oversized content: zoom {hugeZoom}, want the {CanvasFit.MinZoom} floor"); ok = false; }
            if (!Near(hugePan.X, -250f))
            { Debug.LogError($"FAIL fit of oversized content: pan.X {hugePan.X}, want -250 (centre 1000 * 0.25)"); ok = false; }

            // An empty board is an ordinary input — a board the DM just inserted. Neutral camera, no NaN.
            CanvasFit.Compute(new List<CanvasObjectData>(), new Vector2(400f, 300f), out var emptyPan, out float emptyZoom);
            if (!Near(emptyZoom, 1f) || !Near(emptyPan.X, 0f) || !Near(emptyPan.Y, 0f))
            { Debug.LogError($"FAIL fit of an empty board: {emptyPan}/{emptyZoom}, want (0, 0)/1"); ok = false; }

            CanvasFit.Compute(null, new Vector2(400f, 300f), out _, out float nullZoom);
            if (!Near(nullZoom, 1f))
            { Debug.LogError($"FAIL fit of a null list: zoom {nullZoom}, want 1"); ok = false; }

            // A zero-sized object must not divide by zero — uGUI accepts a NaN scale in silence and then
            // draws an empty board, which reads as data loss rather than as a bad number.
            CanvasFit.Compute(new List<CanvasObjectData> { Card(0f, 0f, 0f, 0f) },
                              new Vector2(400f, 300f), out var degenPan, out float degenZoom);
            if (float.IsNaN(degenZoom) || float.IsNaN(degenPan.X) || float.IsNaN(degenPan.Y))
            { Debug.LogError($"FAIL fit of a zero-sized object: {degenPan}/{degenZoom} — NaN reaches uGUI silently"); ok = false; }

            if (ok) Debug.Log("PASS CanvasFit self-tests");
        }
    }
}
