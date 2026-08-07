using System.Collections.Generic;
using UnityEngine;

namespace WorldGen.Workspace.Data
{
    /// <summary>Самопроверки правила заявок. Каждая названа вместе с мутантом, который она
    /// обязана завалить, — иначе тест проверяет не то, что думает (см. арку отрисовки поселений,
    /// где так утекли семь пустых проверок).</summary>
    public class SurfaceClaimsSelfTests : MonoBehaviour
    {
        static WorkspaceLayout TwoPanes(SurfaceKind a, string aId, SurfaceKind b, string bId, int focused)
        {
            var l = new WorkspaceLayout { FocusedPane = focused };
            l.Primary = OnePane(a, aId);
            l.Secondary = OnePane(b, bId);
            return l;
        }

        static PaneState OnePane(SurfaceKind k, string id)
        {
            var p = new PaneState();
            p.Tabs.Add(new TabState { Surface = new SurfaceRef { Kind = k, Id = id }, Title = id });
            p.ActiveIndex = 0;
            return p;
        }

        /// <summary>МУТАНТ: обход панелей всегда с нулевой, а не с активной. Обе панели просят
        /// карту — односоставную, — поэтому выигрывает ровно одна, и это должна быть активная.</summary>
        [ContextMenu("Self-Test: Claims Focused Pane Wins")]
        public void SelfTestClaimsFocusedPaneWins()
        {
            var l = TwoPanes(SurfaceKind.WorldMap, "", SurfaceKind.WorldMap, "", focused: 1);
            var claims = SurfaceClaims.Resolve(l);
            bool ok = claims.Count == 1 && claims[0].Pane == 1;
            if (!ok) Debug.LogError($"FAIL: Claims Focused Pane Wins — {Describe(claims)}");
        }

        /// <summary>МУТАНТ: снят запрет на общий экран. Город, здание и подземелье — три вида над
        /// ОДНИМ экраном, поэтому вторая панель не получает заявки, хотя вид у неё другой.</summary>
        [ContextMenu("Self-Test: Claims Interior Screen Shared")]
        public void SelfTestClaimsInteriorScreenShared()
        {
            var l = TwoPanes(SurfaceKind.Settlement, "town", SurfaceKind.Dungeon, "cave", focused: 0);
            var claims = SurfaceClaims.Resolve(l);
            bool ok = claims.Count == 1 && claims[0].Pane == 0 && claims[0].Kind == SurfaceKind.Settlement;
            if (!ok) Debug.LogError($"FAIL: Claims Interior Screen Shared — {Describe(claims)}");
        }

        /// <summary>МУТАНТ: страница объявлена односоставной — ровно нынешнее поведение, ради
        /// отмены которого вся арка. Две РАЗНЫЕ страницы обязаны дать две заявки, и вторая не
        /// вытесняет первую.</summary>
        [ContextMenu("Self-Test: Claims Two Pages Both Claim")]
        public void SelfTestClaimsTwoPagesBothClaim()
        {
            var l = TwoPanes(SurfaceKind.Page, "pageA", SurfaceKind.Page, "pageB", focused: 0);
            var claims = SurfaceClaims.Resolve(l);
            bool two = claims.Count == 2;
            bool first = two && claims[0].Pane == 0 && claims[0].Id == "pageA";
            bool second = two && claims[1].Pane == 1 && claims[1].Id == "pageB";
            if (!(first && second)) Debug.LogError($"FAIL: Claims Two Pages Both Claim — {Describe(claims)}");
        }

        /// <summary>МУТАНТ: доску забыли внести в многопанельные — ДМ заказал доску наравне со
        /// страницей, и забыть её легко именно потому, что она добавляется последней.</summary>
        [ContextMenu("Self-Test: Claims Two Boards Both Claim")]
        public void SelfTestClaimsTwoBoardsBothClaim()
        {
            var l = TwoPanes(SurfaceKind.Canvas, "blockA", SurfaceKind.Canvas, "blockB", focused: 0);
            var claims = SurfaceClaims.Resolve(l);
            bool ok = claims.Count == 2 && claims[0].Id == "blockA" && claims[1].Id == "blockB";
            if (!ok) Debug.LogError($"FAIL: Claims Two Boards Both Claim — {Describe(claims)}");
        }

        /// <summary>МУТАНТ: нет проверки пустой панели — `ActiveIndex` там равен −1, и заявка
        /// уходит с индексом вкладки, которой нет. Панель без вкладок существует всегда: так
        /// выглядит только что закрытая последняя вкладка до нормализации.</summary>
        [ContextMenu("Self-Test: Claims Empty Pane Silent")]
        public void SelfTestClaimsEmptyPaneSilent()
        {
            var l = TwoPanes(SurfaceKind.Page, "pageA", SurfaceKind.Page, "pageB", focused: 0);
            l.Secondary.Tabs.Clear();
            l.Secondary.ActiveIndex = -1;
            var claims = SurfaceClaims.Resolve(l);
            bool ok = claims.Count == 1 && claims[0].Pane == 0;
            if (!ok) Debug.LogError($"FAIL: Claims Empty Pane Silent — {Describe(claims)}");
        }

        /// <summary>МУТАНТ: запрет общего экрана применён и к разным экранам тоже. Страница рядом
        /// с картой работает СЕГОДНЯ — это защита от регресса, а не новая возможность.</summary>
        [ContextMenu("Self-Test: Claims Page Beside Map")]
        public void SelfTestClaimsPageBesideMap()
        {
            var l = TwoPanes(SurfaceKind.Page, "pageA", SurfaceKind.WorldMap, "", focused: 0);
            var claims = SurfaceClaims.Resolve(l);
            bool ok = claims.Count == 2;
            if (!ok) Debug.LogError($"FAIL: Claims Page Beside Map — {Describe(claims)}");
        }

        /// <summary>Нет разделения — есть только Primary. Вырожденный случай, на котором
        /// приложение проводит большую часть времени.</summary>
        [ContextMenu("Self-Test: Claims Single Pane")]
        public void SelfTestClaimsSinglePane()
        {
            var l = new WorkspaceLayout { FocusedPane = 0 };
            l.Primary = OnePane(SurfaceKind.Page, "pageA");
            l.Secondary = null;
            var claims = SurfaceClaims.Resolve(l);
            bool ok = claims.Count == 1 && claims[0].Pane == 0 && claims[0].Id == "pageA";
            if (!ok) Debug.LogError($"FAIL: Claims Single Pane — {Describe(claims)}");
        }

        static string Describe(List<SurfaceClaim> claims)
        {
            if (claims == null) return "claims=null";
            var parts = new List<string>();
            foreach (var c in claims) parts.Add($"[pane={c.Pane} kind={c.Kind} id={c.Id}]");
            return parts.Count == 0 ? "claims=<пусто>" : string.Join(" ", parts);
        }
    }
}
