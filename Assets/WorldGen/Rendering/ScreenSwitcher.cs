using System.Collections.Generic;
using UnityEngine;

namespace WorldGen.Rendering
{
    /// <summary>The mutually-exclusive top-level screens. Named AppScreen (NOT Screen) on purpose:
    /// UnityEngine.Screen (Screen.width/height) is used throughout this namespace, and a same-namespace
    /// type named Screen would shadow it and break every Screen.width reference.</summary>
    public enum AppScreen { Generation, Progress, MapEditor, PoiEditor, Dungeon }

    /// <summary>
    /// Single source of truth for which top-level screen is visible. Show(target) deactivates the
    /// members of EVERY screen except the target, then activates the target's — so a panel can never
    /// leak onto the wrong screen by omission. "Smart" members with internal sub-state (the toolbar
    /// and its docked tab panels) are handled by the after-show hook, not the member list.
    /// </summary>
    public class ScreenSwitcher
    {
        readonly Dictionary<AppScreen, GameObject[]> members;
        readonly System.Action<AppScreen> onAfterShow;

        public AppScreen Current { get; private set; }

        /// <param name="members">Per-screen "dumb" GameObjects that are simply toggled on/off.</param>
        /// <param name="onAfterShow">Called after each Show with the now-active screen — for members
        /// that manage their own sub-visibility (e.g. MapToolbarUI.SetChromeVisible). May be null.</param>
        public ScreenSwitcher(Dictionary<AppScreen, GameObject[]> members, System.Action<AppScreen> onAfterShow = null)
        {
            this.members = members;
            this.onAfterShow = onAfterShow;
        }

        public void Show(AppScreen target)
        {
            foreach (var kv in members)
            {
                bool active = kv.Key == target;
                var gos = kv.Value;
                for (int i = 0; i < gos.Length; i++)
                    if (gos[i] != null) gos[i].SetActive(active);
            }
            Current = target;
            onAfterShow?.Invoke(target);
        }
    }
}
