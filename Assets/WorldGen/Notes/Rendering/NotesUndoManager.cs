using System.Collections.Generic;
using UnityEngine;
using WorldGen.Notes.Data;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Command-stack undo for canvas object/link create, delete, and move actions.
    /// Delete goes through RequestDeleteObject, which shows a confirm dialog before
    /// mutating anything.
    /// </summary>
    public class NotesUndoManager : MonoBehaviour
    {
        abstract class Command
        {
            public abstract void Undo();
        }

        class CreateObjectCommand : Command
        {
            public NotesCanvasController Canvas;
            public string ObjectId;
            public override void Undo() => Canvas.RemoveObject(ObjectId);
        }

        class CreateLinkCommand : Command
        {
            public NotesCanvasController Canvas;
            public string LinkId;
            public override void Undo() => Canvas.RemoveLink(LinkId);
        }

        class MoveCommand : Command
        {
            public NotesCanvasController Canvas;
            public CanvasObjectData Data;
            public System.Numerics.Vector2 OldPosition;
            public override void Undo()
            {
                Data.Position = OldPosition;
                var view = Canvas.GetView(Data.Id);
                switch (view)
                {
                    case NoteCardView n: n.Refresh(); break;
                    case ImageObjectView i: i.Refresh(); break;
                    case DrawingObjectView d: d.Refresh(); break;
                }
                Canvas.RefreshLinksFor(Data.Id);
            }
        }

        class ResizeCommand : Command
        {
            public NotesCanvasController Canvas;
            public CanvasObjectData Data;
            public System.Numerics.Vector2 OldPosition;
            public System.Numerics.Vector2 OldSize;
            public override void Undo()
            {
                Data.Position = OldPosition;
                Data.Size = OldSize;
                Canvas.RefreshView(Data.Id);
                Canvas.RefreshLinksFor(Data.Id);
            }
        }

        class DeleteObjectCommand : Command
        {
            public NotesCanvasController Canvas;
            public CanvasObjectData Data;
            public override void Undo()
            {
                switch (Data)
                {
                    case NoteCardData c: Canvas.AddNoteCard(c.Position); break;
                    case ImageObjectData img: Canvas.AddImage(img.Position, img.ImageBytes); break;
                    case DrawingObjectData d: Canvas.AddDrawing(d.Position, d.PixelWidth, d.PixelHeight); break;
                }
                // Note: re-created object gets a new Id; any links the deleted object had
                // are not restored. This is an accepted v1 limitation (delete is confirmed
                // up front specifically because it isn't fully reversible for linked objects).
            }
        }

        class DeleteLinkCommand : Command
        {
            public NotesCanvasController Canvas;
            public string FromObjectId;
            public string ToObjectId;
            public override void Undo() => Canvas.AddLink(FromObjectId, ToObjectId);
        }

        [Header("Confirm dialog UI (built at runtime, not scene-assigned)")]
        public Font builtinFont;

        readonly Stack<Command> undoStack = new Stack<Command>();

        void Awake()
        {
            if (builtinFont == null) builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        public void PushCreateNoteCard(NotesCanvasController canvas, System.Numerics.Vector2 position)
        {
            var data = canvas.AddNoteCard(position);
            if (data == null) return;
            undoStack.Push(new CreateObjectCommand { Canvas = canvas, ObjectId = data.Id });
        }

        public void PushCreateImage(NotesCanvasController canvas, System.Numerics.Vector2 position, byte[] bytes)
        {
            var data = canvas.AddImage(position, bytes);
            if (data == null) return;
            undoStack.Push(new CreateObjectCommand { Canvas = canvas, ObjectId = data.Id });
        }

        public void PushCreateDrawing(NotesCanvasController canvas, System.Numerics.Vector2 position, int w, int h)
        {
            var data = canvas.AddDrawing(position, w, h);
            if (data == null) return;
            undoStack.Push(new CreateObjectCommand { Canvas = canvas, ObjectId = data.Id });
        }

        public void PushCreateLink(NotesCanvasController canvas, string fromId, string toId)
        {
            var data = canvas.AddLink(fromId, toId);
            if (data == null) return;
            undoStack.Push(new CreateLinkCommand { Canvas = canvas, LinkId = data.Id });
        }

        public void PushMove(NotesCanvasController canvas, CanvasObjectData data, System.Numerics.Vector2 oldPos, System.Numerics.Vector2 newPos)
        {
            undoStack.Push(new MoveCommand { Canvas = canvas, Data = data, OldPosition = oldPos });
        }

        public void PushResize(NotesCanvasController canvas, CanvasObjectData data, System.Numerics.Vector2 oldPosition, System.Numerics.Vector2 oldSize)
        {
            undoStack.Push(new ResizeCommand { Canvas = canvas, Data = data, OldPosition = oldPosition, OldSize = oldSize });
        }

        public void RequestDeleteObject(NotesCanvasController canvas, CanvasObjectData data, System.Action<bool> onConfirmed)
        {
            ConfirmDialog.Show(builtinFont, $"Удалить \"{DescribeObject(data)}\"?", confirmed =>
            {
                if (confirmed)
                {
                    canvas.RemoveObject(data.Id);
                    undoStack.Push(new DeleteObjectCommand { Canvas = canvas, Data = data });
                }
                onConfirmed?.Invoke(confirmed);
            });
        }

        public void RequestDeleteLink(NotesCanvasController canvas, LinkData data, System.Action<bool> onConfirmed)
        {
            ConfirmDialog.Show(builtinFont, "Удалить связь?", confirmed =>
            {
                if (confirmed)
                {
                    canvas.RemoveLink(data.Id);
                    undoStack.Push(new DeleteLinkCommand { Canvas = canvas, FromObjectId = data.FromObjectId, ToObjectId = data.ToObjectId });
                }
                onConfirmed?.Invoke(confirmed);
            });
        }

        public void Undo()
        {
            if (undoStack.Count == 0) return;
            var command = undoStack.Pop();
            command.Undo();
        }

        static string DescribeObject(CanvasObjectData data) => data switch
        {
            NoteCardData c => string.IsNullOrEmpty(c.Title) ? "заметку" : c.Title,
            ImageObjectData => "изображение",
            DrawingObjectData => "рисунок",
            _ => "объект"
        };

        // ── Self-tests ─────────────────────────────────────────────────────────

        [ContextMenu("Self-Test: Notes Undo — Create/Undo Card")]
        public void SelfTestCreateUndoCard()
        {
            var canvas = FindObjectOfType<NotesCanvasController>();
            var doc = canvas != null ? canvas.documentController : null;
            if (canvas == null || doc == null || doc.ActivePage == null)
            {
                Debug.Log("Self-Test Notes Undo — Create/Undo Card: FAIL (missing NotesCanvasController/active page in scene)");
                return;
            }

            int before = doc.ActivePage.Objects.Count;
            PushCreateNoteCard(canvas, new System.Numerics.Vector2(0f, 0f));
            bool createdOk = doc.ActivePage.Objects.Count == before + 1;

            Undo();
            bool undoneOk = doc.ActivePage.Objects.Count == before;

            bool ok = createdOk && undoneOk;
            Debug.Log(ok
                ? "Self-Test Notes Undo — Create/Undo Card: PASS"
                : $"Self-Test Notes Undo — Create/Undo Card: FAIL (createdOk={createdOk}, undoneOk={undoneOk})");
        }
    }
}
