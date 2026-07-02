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

        [Header("Confirm dialog UI (built at runtime, not scene-assigned)")]
        public Font builtinFont;

        readonly Stack<Command> undoStack = new Stack<Command>();
        GameObject confirmDialogGO;

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

        public void RequestDeleteObject(NotesCanvasController canvas, CanvasObjectData data, System.Action<bool> onConfirmed)
        {
            ShowConfirmDialog($"Удалить \"{DescribeObject(data)}\"?", confirmed =>
            {
                if (confirmed)
                {
                    canvas.RemoveObject(data.Id);
                    undoStack.Push(new DeleteObjectCommand { Canvas = canvas, Data = data });
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

        void ShowConfirmDialog(string message, System.Action<bool> onResult)
        {
            if (confirmDialogGO != null) Destroy(confirmDialogGO);

            var canvasGO = new GameObject("ConfirmDialogCanvas");
            var canvas = canvasGO.AddComponent<UnityEngine.Canvas>();
            canvas.renderMode = UnityEngine.RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            canvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
            canvasGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            confirmDialogGO = canvasGO;

            var panelGO = new GameObject("Panel");
            panelGO.transform.SetParent(canvasGO.transform, false);
            var panelImg = panelGO.AddComponent<UnityEngine.UI.Image>();
            panelImg.color = new Color(0.1f, 0.1f, 0.1f, 0.95f);
            var panelRect = panelGO.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(280f, 120f);
            panelRect.anchoredPosition = Vector2.zero;

            var msgGO = new GameObject("Message");
            msgGO.transform.SetParent(panelGO.transform, false);
            var msgText = msgGO.AddComponent<UnityEngine.UI.Text>();
            msgText.text = message;
            msgText.font = builtinFont;
            msgText.fontSize = 13;
            msgText.color = Color.white;
            msgText.alignment = TextAnchor.MiddleCenter;
            var msgRect = msgGO.GetComponent<RectTransform>();
            msgRect.anchorMin = new Vector2(0f, 0.4f);
            msgRect.anchorMax = new Vector2(1f, 1f);
            msgRect.sizeDelta = Vector2.zero;

            AddDialogButton(panelGO.transform, "Отмена", new Vector2(0.05f, 0.1f), new Vector2(0.48f, 0.35f), () =>
            {
                Destroy(confirmDialogGO);
                onResult(false);
            });
            AddDialogButton(panelGO.transform, "Удалить", new Vector2(0.52f, 0.1f), new Vector2(0.95f, 0.35f), () =>
            {
                Destroy(confirmDialogGO);
                onResult(true);
            });
        }

        void AddDialogButton(Transform parent, string label, Vector2 anchorMin, Vector2 anchorMax, System.Action onClick)
        {
            var go = new GameObject($"Btn_{label}");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<UnityEngine.UI.Image>();
            img.color = new Color(0.35f, 0.35f, 0.35f, 0.9f);
            var btn = go.AddComponent<UnityEngine.UI.Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.sizeDelta = Vector2.zero;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<UnityEngine.UI.Text>();
            text.text = label;
            text.font = builtinFont;
            text.fontSize = 12;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
        }

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
