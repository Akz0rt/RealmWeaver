using System;
using System.Collections.Generic;
using System.Numerics;
using Newtonsoft.Json;

namespace WorldGen.Notes.Data
{
    /// <summary>Which representation a page uses. Board MUST stay the zero value: a page saved before
    /// document pages existed carries no "Kind" key at all, and Newtonsoft then leaves the field at
    /// default(PageKind) — so zero is what makes every pre-existing page load back as the board it is,
    /// with no migration code.</summary>
    public enum PageKind { Board = 0, Document = 1 }

    /// <summary>What one row of a document page is. Section MUST stay the zero value for the same reason
    /// Board does.
    ///
    /// CANVAS TAKES 5, NOT THE FREED 4. BoardRef = 4 is already written as a four in saved files, and an
    /// int-backed C# enum accepts any value of its underlying type without complaint — so an old BoardRef row
    /// would deserialize as a Canvas block that silently carries no board. BoardRef stays as a TOMBSTONE: the
    /// value is spent forever, no new code creates one, and NotesDocOps.Normalize degrades any it finds into
    /// the plain row it always looked like.</summary>
    public enum BlockKind { Section = 0, Item = 1, Prose = 2, Image = 3, BoardRef = 4, Canvas = 5 }

    /// <summary>Which kind of world object a NotesPage.Bound names. Poi covers a settlement/POI on the map;
    /// Building and Room narrow to a building interior and a room within it, mirroring the levels Р3's
    /// «Открыть город» and friends already open through.</summary>
    public enum WorldRefKind { Poi = 0, Building = 1, Room = 2 }

    /// <summary>What a page documents, if it documents a world object at all.
    ///
    /// IT NO LONGER DRIVES THE NAVIGATOR (Task 10e). Until then this was the only signal NavigatorTree.Build
    /// read to decide «Мир» membership; «Мир» is now the world's own contents and consults no page at all
    /// (see NavigatorTree's class doc for the ruling). What remains is the page-to-world link itself, whose
    /// real consumers are QuickOpen's W4 row-suppression, NotesDocOps.FindPageBoundTo/EnsurePageFor, and
    /// Р3's binding work — nothing derives a tree from it any more. Kept
    /// as a nullable reference rather than a bool flag beside an id, the same reason PageGroup.LinkedPoiId
    /// is null rather than paired with an "IsLinked" bool: the two could otherwise disagree.</summary>
    public class WorldRef
    {
        public WorldRefKind Kind;
        public string Id;
    }

    public class NotesDocument
    {
        public List<PageGroup> Groups = new List<PageGroup>();
    }

    public class PageGroup
    {
        public string Id = Guid.NewGuid().ToString();
        public string Title = "Новая группа";
        public string LinkedPoiId;    // null = not tied to a POI
        /// <summary>Marks the single «Справочник» group that promoted pages are filed into. A ROLE FLAG,
        /// deliberately not a title match: the user can rename any group, and can create their own group
        /// called «Справочник», either of which would silently break title-based lookup.</summary>
        public bool IsReference;
        public List<NotesPage> Pages = new List<NotesPage>();
    }

    public class NotesPage
    {
        public string Id = Guid.NewGuid().ToString();
        public string Name = "Новая страница";
        public PageKind Kind = PageKind.Board;
        /// <summary>Document rows, empty for a Board. A FLAT list — nesting is carried by DocBlock.Depth,
        /// which makes reorder, collapse and JSON round-trip trivial at the cost of nothing, since nothing
        /// here goes deeper than three levels.</summary>
        public List<DocBlock> Blocks = new List<DocBlock>();
        /// <summary>READ ON LOAD, NEVER WRITTEN. A page's own board is what format 13 stored; format 14 keeps
        /// boards inside DocBlock (see NotesDocOps.MigrateBoardPage), and these four fields exist only so
        /// there is something for an old file to deserialize INTO. Nulled by the migration, and kept off every
        /// save by the ShouldSerialize methods below — removing the fields outright would leave nothing to
        /// read a v13 file with.
        ///
        /// No `= new List&lt;&gt;()` initializer, deliberately: the migration nulls them, and a null is what the
        /// guard reads as "already migrated".</summary>
        public List<CanvasObjectData> Objects;
        public List<LinkData> Links;
        public Vector2 CameraPan;
        public float CameraZoom = 1f;

        public bool ShouldSerializeObjects() => false;
        public bool ShouldSerializeLinks() => false;
        public bool ShouldSerializeCameraPan() => false;
        public bool ShouldSerializeCameraZoom() => false;

        /// <summary>Which world object this page documents, if any — null for an ordinary authored page.
        /// See WorldRef: this is the single field NavigatorTree.Build reads for МИР membership. Additive,
        /// matching DocBlock.Detail/LinkedPageId: absent from a file saved before it existed, and
        /// deserializes to null there, so no migration code exists or is needed (ProjectSerializer.
        /// CurrentFormatVersion 13).</summary>
        [JsonProperty("Bound", NullValueHandling = NullValueHandling.Ignore)]
        public WorldRef Bound;
    }

    /// <summary>One row of a document page. ONE class with a Kind enum rather than a subclass hierarchy:
    /// the board's polymorphic CanvasObjectData already forced a hand-written CanvasObjectDataConverter,
    /// and a single concrete type means a second one is never needed.</summary>
    public class DocBlock
    {
        /// <summary>Stable for the block's whole life — never regenerated on edit, reorder, or undo, and
        /// unique across the WHOLE document, not just its page. Session state (DM vs players) will
        /// reference blocks by this id from outside the document, so churn here would silently break it.</summary>
        public string Id = Guid.NewGuid().ToString();
        public BlockKind Kind;
        /// <summary>0 = section, 1 = row, 2 = sub-row.</summary>
        public int Depth;
        public string Text = "";

        /// <summary>⊕ in-place body. Item only; null when absent.</summary>
        [JsonProperty("Detail", NullValueHandling = NullValueHandling.Ignore)]
        public string Detail;

        /// <summary>📄 target page. Optional on Item, REQUIRED on BoardRef (where it must point at a page
        /// whose Kind is Board). Never dangles in a saved file.</summary>
        [JsonProperty("LinkedPageId", NullValueHandling = NullValueHandling.Ignore)]
        public string LinkedPageId;

        public bool Collapsed;

        /// <summary>Image only — raw png/jpg bytes, embedded exactly as ImageObjectData already does.</summary>
        [JsonProperty("ImageBytes", NullValueHandling = NullValueHandling.Ignore)]
        public byte[] ImageBytes;

        /// <summary>Image and Canvas. For an image, 0 means derive the height from the aspect ratio at the
        /// column width; for a canvas, 0 means the default block height. Not stored for any other kind — see
        /// I7 and Normalize, which clears it.</summary>
        public float DisplayHeight;

        /// <summary>Canvas only — how wide the block is drawn. 0 means the text column's own width
        /// (NotesTypography.MeasureWidthPx). A canvas is the ONE row allowed to be wider than the column, so
        /// it is the one row that stores a width; an image is always exactly the column wide and stores
        /// none.</summary>
        public float DisplayWidth;

        /// <summary>Canvas only — the board's objects, null on every other kind (I7). Held by the BLOCK rather
        /// than by the page, which is the whole of Р4: a page snapshot then already contains the board, so
        /// DocHistory is the page's single undo stack and the canvas's own command stack could be deleted.</summary>
        [JsonProperty("CanvasObjects", NullValueHandling = NullValueHandling.Ignore)]
        public List<CanvasObjectData> CanvasObjects;

        /// <summary>Canvas only — the links between those objects, null on every other kind (I7).</summary>
        [JsonProperty("CanvasLinks", NullValueHandling = NullValueHandling.Ignore)]
        public List<LinkData> CanvasLinks;

        /// <summary>Canvas only — where the board is scrolled to. System.Numerics.Vector2, like every vector
        /// in this file: UnityEngine's same-named type would tie the whole pure layer to an Editor.</summary>
        public Vector2 CanvasPan;

        /// <summary>Canvas only. 1 is the NEUTRAL value, not 0 — so Normalize resets a non-Canvas block to 1
        /// rather than to zero, and "the field is at its default" is what I7 checks.</summary>
        public float CanvasZoom = 1f;

        // Keeps three floats' worth of neutral canvas geometry off the wire for every ordinary row. Plain
        // ShouldSerializeX() methods rather than [DefaultValue] + DefaultValueHandling: Newtonsoft finds these
        // by convention, and the offline harness compiles against stubs/NewtonsoftStub.cs, which would
        // otherwise need widening for an attribute it does not have.
        public bool ShouldSerializeCanvasPan() => Kind == BlockKind.Canvas;
        public bool ShouldSerializeCanvasZoom() => Kind == BlockKind.Canvas;
        public bool ShouldSerializeDisplayWidth() => Kind == BlockKind.Canvas;
    }

    /// <summary>One "this page is mentioned here" entry, computed by NotesDocOps.FindBacklinks and never
    /// stored. A plain class, NOT a positional record: those need init accessors, which need
    /// System.Runtime.CompilerServices.IsExternalInit, which .NET Standard 2.1 does not have — so they do
    /// not compile under Unity 2022.3.</summary>
    public class Backlink
    {
        public string SourcePageId;
        public string SourcePageName;
        public string SectionTitle;
        public string BlockId;
    }

    public abstract class CanvasObjectData
    {
        public string Id = Guid.NewGuid().ToString();
        public Vector2 Position;
        public Vector2 Size;
    }

    public class NoteCardData : CanvasObjectData
    {
        public string Title = "";
        public string Body = "";

        public NoteCardData()
        {
            Size = new Vector2(220f, 140f);
        }
    }

    public class ImageObjectData : CanvasObjectData
    {
        public byte[] ImageBytes;   // raw file bytes (png/jpg/gif), embedded directly
    }

    public class DrawingObjectData : CanvasObjectData
    {
        public byte[] PixelDataPng;  // PNG-encoded raster content, null until first stroke
        public int PixelWidth;
        public int PixelHeight;

        public DrawingObjectData(int pixelWidth, int pixelHeight)
        {
            PixelWidth = pixelWidth;
            PixelHeight = pixelHeight;
            Size = new Vector2(pixelWidth, pixelHeight);
        }
    }

    public class LinkData
    {
        public string Id = Guid.NewGuid().ToString();
        public string FromObjectId;
        public string ToObjectId;
        public bool Directed = true;
        /// <summary>Offset from the straight-line midpoint between the two connected objects'
        /// anchor points, in canvas units. Null = an automatic bend is computed instead.</summary>
        public Vector2? ControlPointOffset;
    }
}
