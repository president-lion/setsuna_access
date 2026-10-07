using System;
using Setsuna;
using UnityEngine;

namespace SetsunaAccess
{
    /// <summary>
    /// Generic menu focus. The camp, shop, save and result screens all move their cursor by
    /// calling a choice's SelectLogic / OnSelect(true). Those hooks only record the choice; the
    /// line is built in LateUpdate, after the game has filled in the row and its help text:
    /// "row text, 3 of 7. description".
    /// </summary>
    internal static class Focus
    {
        private static UiChoices _pending;
        private static bool _queueNext;
        private static string _messageBar;
        private static string _lastBar;

        public static void Select(UiChoices choice)
        {
            // List/tab controllers are choices too, but only hand focus on to a row or tab.
            if (choice == null || choice is UiCampContentController || choice is UiCampTabController) return;
            _pending = choice;
        }

        /// <summary>A screen just announced its title: queue the next focus line instead of cutting it off.</summary>
        public static void QueueNext() { _queueNext = true; }

        /// <summary>The camp's bottom help line; spoken after the focused item.</summary>
        public static void MessageBar(string text)
        {
            _messageBar = TextClean.Clean(text);
        }

        public static void LateTick()
        {
            var choice = _pending;
            var bar = _messageBar;
            _pending = null;
            _messageBar = null;

            if (choice != null && choice.gameObject.activeInHierarchy)
            {
                var line = Describe(choice);
                if (!string.IsNullOrEmpty(bar)) { line = Join(line, bar); _lastBar = bar; }
                Speech.Say(line, !_queueNext);
                _queueNext = false;
            }
            else if (!string.IsNullOrEmpty(bar) && bar != _lastBar)
            {
                _lastBar = bar;
                Speech.Say(bar, false);
            }
        }

        public static string Describe(UiChoices choice)
        {
            var label = Ui.ReadAll(choice.transform);
            if (choice.IsLock) label = Join(label, Strings.Locked);

            int index, count;
            Position(choice, out index, out count);
            var line = count > 0 ? Strings.Item(label, index, count) : label;

            var content = choice as UiCampContent;
            if (content != null) line = Join(line, RowDescription(content));
            return line;
        }

        private static void Position(UiChoices choice, out int index, out int count)
        {
            index = -1; count = 0;
            var content = choice as UiCampContent;
            var ctrl = content != null ? content.MainContentController : null;
            if (ctrl != null)
            {
                index = ctrl.CurrentContentNo;
                count = Reflect.Int(ctrl, "contentMaxNum");
                return;
            }
            // Otherwise: active siblings of the same type, in hierarchy order.
            var parent = choice.transform.parent;
            if (parent == null) return;
            var type = choice.GetType();
            for (var i = 0; i < parent.childCount; i++)
            {
                var c = parent.GetChild(i).GetComponent(type) as UiChoices;
                if (c == null || !c.gameObject.activeSelf) continue;
                if (c == choice) index = count;
                count++;
            }
            if (index < 0) count = 0;
        }

        /// <summary>Description from the row's own data: an item, a skill.</summary>
        private static string RowDescription(UiCampContent content)
        {
            var item = Reflect.Get<ItemData>(content, "currentItemData");
            if (item != null) return TextClean.Clean(item.param.description);
            var skill = Reflect.Get<object>(content, "currentSkillData");
            if (skill != null)
            {
                var d = Reflect.Get<string>(skill, "description");
                if (!string.IsNullOrEmpty(d)) return TextClean.Clean(d);
            }
            return null;
        }

        private static string Join(string a, string b)
        {
            if (string.IsNullOrEmpty(b)) return a;
            if (string.IsNullOrEmpty(a)) return b;
            return a + ". " + b;
        }
    }
}
