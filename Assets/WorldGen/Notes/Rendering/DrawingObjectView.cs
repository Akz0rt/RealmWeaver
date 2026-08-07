using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WorldGen.Notes.Data;

namespace WorldGen.Notes.Rendering
{
    /// <summary>Рисунок на доске: перетаскиваемый прямоугольник, в котором ПЕЧАТАЕТСЯ растр из списка
    /// мазков. Пикселей этот класс больше не хранит и не отдаёт наружу — правда лежит в
    /// DrawingObjectData.Strokes, а текстура всего лишь её отпечаток, который можно выбросить и
    /// напечатать заново в любой момент (Rebake).
    ///
    /// Мазок ведёт CanvasInteractionController через три вызова: BeginStroke на нажатии, StampLive на
    /// каждом отрезке протаскивания, EndStroke на отпускании.</summary>
    [RequireComponent(typeof(RectTransform))]
    public class DrawingObjectView : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler, IPointerClickHandler
    {
        DrawingObjectData data;
        RectTransform rect;
        RawImage rawImage;
        Texture2D texture;
        Vector2 dragStartLocalPos;
        Vector2 pressScreenPos;
        bool dragging;

        /// <summary>Рабочий растр, живёт ТОЛЬКО пока идёт мазок — см. EndStroke о том, почему он не
        /// остаётся между мазками.</summary>
        byte[] rgba;
        /// <summary>Копия готового растра до начала мазка. Тоже только на время мазка.</summary>
        byte[] beforeStroke;
        Stroke liveStroke;

        public string ObjectId => data?.Id;
        public CanvasObjectData Data => data;
        public RectTransform RectTransform => rect;

        /// <summary>When set, self-move-drag is only allowed while its ActiveTool is Select — otherwise
        /// (e.g. Drawing tool) CanvasInteractionController handles the drag itself (painting).</summary>
        public CanvasInteractionController interactionController;

        public event System.Action<string, System.Numerics.Vector2, System.Numerics.Vector2> OnDragEnded;
        public event System.Action<string> OnClicked;

        /// <summary>The DM has begun moving this object. See NoteCardView.OnDragStarted for why the undo
        /// snapshot cannot wait for OnDragEnded.</summary>
        public event System.Action<string> OnDragStarted;

        public void Initialize(DrawingObjectData drawingData, RectTransform canvasContainer)
        {
            data = drawingData;
            rect = GetComponent<RectTransform>();
            transform.SetParent(canvasContainer, false);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rawImage = gameObject.AddComponent<RawImage>();
            Refresh();
            Rebake();
        }

        public void Refresh()
        {
            if (data == null) return;
            rect.anchoredPosition = new Vector2(data.Position.X, data.Position.Y);
            rect.sizeDelta = new Vector2(data.Size.X, data.Size.Y);
        }

        /// <summary>Печатает рисунок начисто: лист, унаследованный слой, затем все мазки по порядку.
        /// Нужна при загрузке, при отмене, при смене листа и при изменении размера — то есть нигде в
        /// горячем пути рисования.
        ///
        /// ОДИН ПРОХОД, А НЕ ДВА БУФЕРА СО СЛИЯНИЕМ. План предлагал напечатать мазки отдельным
        /// растром и наложить его на унаследованный слой по правилу «пиксель, отличный от цвета
        /// листа, берётся из мазков». Это правило неисполнимо: СТИРАЮЩИЙ мазок как раз и кладёт
        /// цвет листа, поэтому под ластиком всегда выигрывал бы унаследованный слой и ластик по
        /// старому рисунку не стирал бы ничего. Здесь мазки штампуются прямо в лист, на который уже
        /// лёг унаследованный слой, — «печатается первым, мазки поверх» получается буквально, и
        /// ластик возвращает к цвету листа что угодно, включая старую картинку.
        ///
        /// При пустом унаследованном слое этот проход побайтово совпадает с StrokeRaster.Bake — он и
        /// есть Bake, только по готовому листу: правило штамповки одно на всех (StampStroke), и
        /// второго растеризатора, способного молча разойтись с проверяемым, не заводится.</summary>
        public void Rebake()
        {
            if (data == null) return;
            StrokeRaster.ChooseSize(data.Size.X, data.Size.Y, out int w, out int h);
            EnsureTexture(w, h);

            var paper = PaperPalette.At(data.PaperIndex);
            var bytes = StrokeRaster.NewPaper(w, h, paper);

            if (data.PixelDataPng != null && data.PixelDataPng.Length > 0)
                BlitLegacyLayer(bytes, w, h);

            // По порядку и только по порядку, как в Bake: ластик — такой же мазок, и «стёр, потом
            // нарисовал» не равно «нарисовал, потом стёр».
            if (data.Strokes != null)
                foreach (var stroke in data.Strokes)
                    if (stroke != null) StrokeRaster.StampStroke(bytes, w, h, stroke, paper);

            texture.LoadRawTextureData(bytes);
            texture.Apply();
            // Перепечатка — это и есть законный конец любого мазка: растр теперь равен данным, и
            // держать при нём начатый мазок было бы враньём.
            ReleaseStroke();
        }

        /// <summary>ЕДИНСТВЕННЫЙ выход из мазка. Раньше буферы и сам мазок отпускались тройками
        /// присваиваний в трёх местах, и каждое место успело разойтись с остальными: перепечатка
        /// забывала мазок, защитная ветка забывала буферы. Одна помощница на все выходы — чтобы
        /// «мазок кончился» означало одно и то же везде.</summary>
        void ReleaseStroke()
        {
            rgba = null;
            beforeStroke = null;
            liveStroke = null;
        }

        /// <summary>Кладёт унаследованный PNG на лист. Через Graphics.CopyTexture его не положить:
        /// разрешение старого растра почти никогда не совпадает с тем, что считает ChooseSize.
        /// Декодируем во ВРЕМЕННУЮ текстуру, переносим пиксели с растяжением и уничтожаем её тут же:
        /// Texture2D неуправляемая, и оставленная копия текла бы на каждую перепечатку.
        ///
        /// РАСТЯГИВАЕТСЯ НА ВЕСЬ ЛИСТ, БЕЗ ПОЛЕЙ. RawImage и раньше растягивал этот PNG на весь
        /// прямоугольник объекта, каким бы ни было соотношение сторон самого PNG, — значит перенос
        /// «доля в долю» даёт на экране РОВНО прежнюю картинку. Сохранение собственной пропорции
        /// PNG полями, наоборот, сдвинуло бы и сжало бы то, что ДМ уже нарисовал: это и было бы
        /// искажением, а не защитой от него.
        ///
        /// БЕЗ ПЕРЕВОРОТА ПО Y. GetPixels32 отдаёт строки снизу вверх, и строка 0 нашего массива —
        /// тоже низ (см. StrokeRaster). Обе стороны считают одинаково, поэтому y переносится в y.
        ///
        /// НАЛОЖЕНИЕ «ИСТОЧНИК ПОВЕРХ», А НЕ ПРОСТАЯ ЗАМЕНА. У старых рисунков стёртое —
        /// по-настоящему прозрачное (ластик писал 255,255,255,0), и замена пробивала бы в листе
        /// дыры. Наложение делает старые дыры цветом ЛИСТА — то самое правило, по которому работает
        /// сегодняшний ластик, одно на старое и на новое. Прежний вид от этого не страдает: у
        /// старого файла лист белый непрозрачный (PaperIndex = 0), а прозрачный лист (индекс 4)
        /// наложение сохраняет прозрачным.</summary>
        void BlitLegacyLayer(byte[] sheet, int w, int h)
        {
            var png = data.PixelDataPng;
            if (png == null || png.Length == 0) return;

            // 1×1: LoadImage всё равно переставит текстуру под настоящий размер PNG, а нулевую
            // сторону Texture2D не принимает вовсе.
            var decoded = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            try
            {
                // Битый или обрезанный PNG — не повод падать: рисунок покажется пустым листом со
                // своими мазками, и ДМ увидит потерю, а не исключение в Консоли на каждый кадр.
                if (!decoded.LoadImage(png)) return;

                // РАЗМЕР БЕРЁТСЯ У ДЕКОДИРОВАННОЙ ТЕКСТУРЫ, А НЕ ИЗ PixelWidth/PixelHeight. LoadImage
                // сам переставляет текстуру под размер картинки, и он тут единственный достоверный:
                // разойдись сохранённое число с настоящим — читали бы мимо строк.
                int sw = decoded.width, sh = decoded.height;
                if (sw <= 0 || sh <= 0) return;

                var src = decoded.GetPixels32();
                if (src == null || src.Length < sw * sh) return;

                for (int y = 0; y < h; y++)
                {
                    float srcY = (y + 0.5f) / h * sh - 0.5f;
                    int yLow = Mathf.FloorToInt(srcY);
                    float fy = srcY - yLow;
                    int y0 = Mathf.Clamp(yLow, 0, sh - 1);
                    int y1 = Mathf.Clamp(yLow + 1, 0, sh - 1);

                    for (int x = 0; x < w; x++)
                    {
                        float srcX = (x + 0.5f) / w * sw - 0.5f;
                        int xLow = Mathf.FloorToInt(srcX);
                        float fx = srcX - xLow;
                        int x0 = Mathf.Clamp(xLow, 0, sw - 1);
                        int x1 = Mathf.Clamp(xLow + 1, 0, sw - 1);

                        var c00 = src[y0 * sw + x0];
                        var c10 = src[y0 * sw + x1];
                        var c01 = src[y1 * sw + x0];
                        var c11 = src[y1 * sw + x1];

                        float sa = Bilerp(c00.a, c10.a, c01.a, c11.a, fx, fy) / 255f;
                        if (sa <= 0.0001f) continue;   // полностью прозрачно — лист остаётся как есть

                        float sr = Bilerp(c00.r, c10.r, c01.r, c11.r, fx, fy);
                        float sg = Bilerp(c00.g, c10.g, c01.g, c11.g, fx, fy);
                        float sb = Bilerp(c00.b, c10.b, c01.b, c11.b, fx, fy);

                        int i = (y * w + x) * 4;
                        float da = sheet[i + 3] / 255f;
                        float outA = sa + da * (1f - sa);
                        if (outA <= 0.0001f) continue;

                        // Прозрачность НЕ предумножена (её нет ни в PNG, ни в нашем массиве),
                        // поэтому цвет делится на итоговую — иначе полупрозрачное блёкло бы.
                        float k = da * (1f - sa);
                        sheet[i] = ToByte((sr * sa + sheet[i] * k) / outA);
                        sheet[i + 1] = ToByte((sg * sa + sheet[i + 1] * k) / outA);
                        sheet[i + 2] = ToByte((sb * sa + sheet[i + 2] * k) / outA);
                        sheet[i + 3] = ToByte(outA * 255f);
                    }
                }
            }
            finally
            {
                // ОБЯЗАТЕЛЬНО И БЕЗУСЛОВНО, включая ранние выходы выше: до 4 МБ утечки на каждую
                // перепечатку, а перепечатка случается на каждой отмене.
                Destroy(decoded);
            }
        }

        static float Bilerp(byte v00, byte v10, byte v01, byte v11, float fx, float fy)
            => Mathf.Lerp(Mathf.Lerp(v00, v10, fx), Mathf.Lerp(v01, v11, fx), fy);

        static byte ToByte(float value) => (byte)Mathf.Clamp(Mathf.RoundToInt(value), 0, 255);

        /// <summary>Texture2D неуправляемая: смена разрешения без Destroy оставляет до 4 МБ на
        /// каждое перетаскивание угла.</summary>
        void EnsureTexture(int w, int h)
        {
            if (texture != null && texture.width == w && texture.height == h) return;
            if (texture != null) Destroy(texture);
            texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Bilinear;
            rawImage.texture = texture;
        }

        void OnDestroy()
        {
            if (texture != null) Destroy(texture);
        }

        /// <summary>Начало мазка. Копия готового растра запоминается ЗДЕСЬ — при отпускании она
        /// восстанавливается, и поверх печатается только чистовой мазок. Без этого каждое отпускание
        /// требовало бы перепечатать весь список, и стоимость росла бы с числом мазков.</summary>
        public void BeginStroke(Stroke stroke)
        {
            if (texture == null || data == null) return;
            liveStroke = stroke;
            rgba = texture.GetRawTextureData<byte>().ToArray();
            beforeStroke = (byte[])rgba.Clone();
        }

        /// <summary>Один отрезок сырого мазка. Идёт через тот же массив, что и Bake: два
        /// растеризатора (SetPixel для живого пути и массив для проверяемого) означали бы два набора
        /// правил, расходящихся молча.</summary>
        public void StampLive(StrokePoint from, StrokePoint to)
        {
            if (rgba == null || liveStroke == null) return;
            var segment = new Stroke
            {
                IsEraser = liveStroke.IsEraser,
                InkIndex = liveStroke.InkIndex,
                Points = { from, to },
            };
            StrokeRaster.StampStroke(rgba, texture.width, texture.height, segment,
                                     PaperPalette.At(data.PaperIndex));
            texture.LoadRawTextureData(rgba);
            texture.Apply();
        }

        /// <summary>Конец мазка: откат к состоянию до мазка и печать ЧИСТОВОЙ цепочки. Именно здесь
        /// линия видимо «дёргается» в новую форму — это решение ДМ, а не недоделка.
        ///
        /// Буферы отпускаются: снимок плюс рабочий массив это 8 МБ на рисунок при 1024×1024, и доска
        /// с двумя десятками больших рисунков носила бы сотню мегабайт, которыми не пользуется.
        ///
        /// НЕТ БУФЕРОВ — НЕ ТИХИЙ ВЫХОД, А ЧЕСТНАЯ ПЕРЕПЕЧАТКА, и это защита от рассинхронизации
        /// данных с экраном. Ctrl+Z посреди зажатой кнопки уничтожает вид и создаёт новый С ТЕМ ЖЕ
        /// id: контроллер продолжает копить точки в уже мёртвый мазок, доводит его до конца и
        /// дописывает в данные — а BeginStroke на новом виде никто не звал, буферов нет, и тихий
        /// выход оставил бы мазок В ФАЙЛЕ, НО НЕ НА ЭКРАНЕ до следующего повода перепечатать.
        /// Перепечатка из данных делает такой исход невозможным по построению, а не по
        /// внимательности.</summary>
        public void EndStroke()
        {
            if (rgba == null || beforeStroke == null || liveStroke == null) { Rebake(); return; }
            StrokeRaster.StampStroke(beforeStroke, texture.width, texture.height, liveStroke,
                                     PaperPalette.At(data.PaperIndex));
            texture.LoadRawTextureData(beforeStroke);
            texture.Apply();
            ReleaseStroke();
        }

        /// <summary>Локальная точка прямоугольника → доли, как их хранит StrokePoint.</summary>
        public Vector2 LocalToFraction(Vector2 local)
            => new Vector2(local.x / rect.rect.width + 0.5f, local.y / rect.rect.height + 0.5f);

        /// <summary>Длина в единицах доски → доля ширины рисунка. Живёт ЗДЕСЬ, рядом с
        /// LocalToFraction, ради одного: обе величины делятся на ОДНУ И ТУ ЖЕ ширину.
        ///
        /// Знаменатель — rect.rect.width, а не data.Size.X, потому что координаты точки приходят из
        /// ScreenPointToLocalPointInRectangle и меряются именно прямоугольником. Сегодня эти два
        /// числа равны (Refresh ставит sizeDelta из Size), но связь неявная: поменяй рисунку якоря —
        /// и толщина разошлась бы с координатами, а выглядело бы это как «линия рисуется не там, где
        /// толщина», то есть неузнаваемо. Один источник убирает вопрос.</summary>
        public float LengthToFraction(float lengthCanvasUnits)
            => rect.rect.width > 0.0001f ? lengthCanvasUnits / rect.rect.width : 0f;

        bool CanSelfMove => interactionController == null || interactionController.ActiveTool == NotesTool.Select;

        public void OnPointerDown(PointerEventData eventData)
        {
            dragStartLocalPos = rect.anchoredPosition;
            pressScreenPos = eventData.position;
            dragging = false;
        }

        /// <summary>Двойной клик открывает правку рисунка — ровно так же, как у карточки открывает текст.
        /// Одиночный клик и протаскивание при этом остаются за выделением и переносом.
        ///
        /// Само переключение делает контроллер: «правка рисунка» — это активный инструмент «Рисунок»,
        /// привязанный к этому объекту, а инструментом здесь не распоряжаются.</summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.clickCount != 2 || interactionController == null) return;
            interactionController.EnterDrawingEditMode(ObjectId);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!CanSelfMove) return;
            if (!dragging) OnDragStarted?.Invoke(data.Id);
            dragging = true;
            rect.anchoredPosition = dragStartLocalPos + eventData.position - pressScreenPos;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (dragging)
            {
                var oldPos = data.Position;
                data.Position = new System.Numerics.Vector2(rect.anchoredPosition.x, rect.anchoredPosition.y);
                OnDragEnded?.Invoke(data.Id, oldPos, data.Position);
            }
            else
            {
                OnClicked?.Invoke(data.Id);
            }
            dragging = false;
        }
    }
}
