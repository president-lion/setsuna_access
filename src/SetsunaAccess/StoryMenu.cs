using Setsuna;
using UnityEngine;

namespace SetsunaAccess
{
    /// <summary>
    /// Snow Chronicles (UiCampStoryWindow). The list rows are read by Focus; this adds what the side
    /// panels show: an entry's details (UiCampStoryAbout, filled by OnSelectSmallContent), a location's
    /// panel while browsing locations, and the History page (records and completion rates).
    /// </summary>
    internal static class StoryMenu
    {
        public static void OnSelectEntry(UiCampStoryWindow win)
        {
            Focus.Append(Panel(win, "storyAbout"));
        }

        public static void OnSelectLarge(UiCampStoryWindow win)
        {
            // Only locations show a panel while the large list is focused.
            if ((UiStoryCategory)Reflect.Int(win, "currentStoryCategory") != UiStoryCategory.Location) return;
            if ((UiStoryState)Reflect.Int(win, "currentStoryState") != UiStoryState.LargeContent) return;
            Focus.Append(Panel(win, "storyAbout"));
        }

        public static void OnState(UiCampStoryWindow win)
        {
            if ((UiStoryState)Reflect.Int(win, "currentStoryState") == UiStoryState.History)
                Focus.Append(Panel(win, "storyHistory"));
        }

        private static string Panel(UiCampStoryWindow win, string field)
        {
            var panel = Reflect.Get<Component>(win, field);
            return panel != null && panel.gameObject.activeInHierarchy ? Ui.ReadAll(panel.transform) : null;
        }
    }
}
