using System.Collections.Generic;

namespace WorldGen.Workspace.Data
{
    /// <summary>Одна заявка: панель `Pane` показывает поверхность `Kind`/`Id`. Обычный класс с
    /// публичными полями, а не запись, — .NET Standard 2.1 не компилирует `record`.</summary>
    public class SurfaceClaim
    {
        public int Pane;
        public SurfaceKind Kind;
        public string Id = "";
    }

    /// <summary>Два свойства вида поверхности, от которых зависит показ. ЕДИНСТВЕННОЕ место, где
    /// это решается: до этой арки то же знание было размазано между `ISurfaceHost.ShareGroup` и
    /// `ScreenSurfaceHosts.GroupFor`, причём смешивая два разных утверждения — «несколько видов
    /// над одним экраном» и «этот вид умеет жить только в одной панели». Второе снято со страницы
    /// и доски, первое осталось у интерьеров; смешанные, они не разъединялись.</summary>
    public static class SurfaceKindRules
    {
        /// <summary>Страница и доска рисуются по экземпляру на панель. Остальные завязаны на один
        /// физический объект — камеру карты или экран редактора — и потому живут в одной.</summary>
        public static bool AllowsMultiplePanes(SurfaceKind kind) =>
            kind == SurfaceKind.Page || kind == SurfaceKind.Canvas;

        /// <summary>Ключ ФИЗИЧЕСКОГО экрана. Город, здание и подземелье возвращают один и тот же:
        /// это три вида над одним `DungeonEditorScreen`, поэтому вторая панель, попросившая любой
        /// из них, просит уже занятый экран. Для остальных ключ свой у каждого вида.</summary>
        public static string ScreenKeyOf(SurfaceKind kind) =>
            kind == SurfaceKind.Settlement || kind == SurfaceKind.BuildingInterior || kind == SurfaceKind.Dungeon
                ? "interior"
                : kind.ToString();
    }

    /// <summary>Кто из панелей что показывает — целиком, без единой ссылки на UnityEngine, чтобы
    /// правило проверялось офлайн-харнессом. `WorkspaceController.SyncSurfaces` после этой арки
    /// только применяет ответ.</summary>
    public static class SurfaceClaims
    {
        /// <summary>Заявки в порядке ПРИОРИТЕТА: активная панель первой. Порядок не косметика —
        /// на нём держится разрешение спора за общий экран (первая заявка занимает ключ) и порядок
        /// показа в применителе.</summary>
        public static List<SurfaceClaim> Resolve(WorkspaceLayout layout)
        {
            var claims = new List<SurfaceClaim>();
            if (layout == null) return claims;

            var takenScreens = new HashSet<string>();
            int[] order = layout.FocusedPane == 1 ? new[] { 1, 0 } : new[] { 0, 1 };

            foreach (int pane in order)
            {
                var state = WorkspaceOps.PaneAt(layout, pane);
                if (state?.Tabs == null) continue;
                // ActiveIndex равен −1 ровно тогда, когда вкладок нет (контракт PaneState), но
                // читается как индекс, поэтому проверяется как индекс.
                if (state.ActiveIndex < 0 || state.ActiveIndex >= state.Tabs.Count) continue;

                var surface = state.Tabs[state.ActiveIndex].Surface;
                if (surface == null) continue;

                if (!SurfaceKindRules.AllowsMultiplePanes(surface.Kind)
                    && !takenScreens.Add(SurfaceKindRules.ScreenKeyOf(surface.Kind)))
                    continue;

                claims.Add(new SurfaceClaim { Pane = pane, Kind = surface.Kind, Id = surface.Id ?? "" });
            }

            return claims;
        }
    }
}
