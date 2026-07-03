using System;
using System.Collections.Generic;
using System.Numerics;

namespace WorldGen.Notes.Data
{
    public class NotesDocument
    {
        public List<PageGroup> Groups = new List<PageGroup>();
    }

    public class PageGroup
    {
        public string Id = Guid.NewGuid().ToString();
        public string Title = "Новая группа";
        public string LinkedPoiId;    // null = not tied to a POI
        public List<NotesPage> Pages = new List<NotesPage>();
    }

    public class NotesPage
    {
        public string Id = Guid.NewGuid().ToString();
        public string Name = "Новая страница";
        public List<CanvasObjectData> Objects = new List<CanvasObjectData>();
        public List<LinkData> Links = new List<LinkData>();
        public Vector2 CameraPan;
        public float CameraZoom = 1f;
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
