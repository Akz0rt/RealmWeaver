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
    /// Board does.</summary>
    public enum BlockKind { Section = 0, Item = 1, Prose = 2, Image = 3, BoardRef = 4 }

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
        public List<CanvasObjectData> Objects = new List<CanvasObjectData>();
        public List<LinkData> Links = new List<LinkData>();
        public Vector2 CameraPan;
        public float CameraZoom = 1f;
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

        /// <summary>Image only — 0 means derive the height from the aspect ratio at the column width.</summary>
        public float DisplayHeight;
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
