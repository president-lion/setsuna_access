using Setsuna;
using UnityEngine;

namespace SetsunaAccess
{
    /// <summary>
    /// Battle results (UiResultWindow). Its state machine waits for Confirm at ExpEnd and
    /// ShowGetItem; each panel is read once when the state reaches it. Game over too.
    /// </summary>
    internal static class Results
    {
        private static UiResultWindow _win;
        private static int _lastState = -1;
        private static bool _gameOver;

        public static void OnOpen(UiResultWindow win)
        {
            _win = win;
            _lastState = -1;
            _gameOver = false;
        }

        public static void Tick()
        {
            if (_win == null) return;
            if (!_win.gameObject.activeInHierarchy) { _win = null; return; }

            if (_win.IsGameOver && !_gameOver)
            {
                _gameOver = true;
                var go = Reflect.Get<Component>(_win, "gameOverObject");
                Speech.Say(go != null ? Ui.ReadAll(go.transform) : Strings.GameOver);
            }

            var state = (int)_win.state;
            if (state == _lastState) return;
            _lastState = state;
            switch (_win.state)
            {
                case UiResultWindow.ResultState.ExpEnd:
                {
                    var line = Ui.ReadAll(Reflect.Get<Component>(_win, "expObject")?.transform);
                    var charas = Reflect.Arr(_win, "charaObjects");
                    if (charas != null)
                        foreach (var c in charas)
                        {
                            var comp = c as Component;
                            if (comp != null && comp.gameObject.activeInHierarchy)
                                line += ". " + Ui.ReadAll(comp.transform);
                        }
                    Speech.Say(line);
                    break;
                }
                case UiResultWindow.ResultState.ShowGetItem:
                    Speech.Say(Ui.ReadAll(Reflect.Get<Component>(_win, "itemObject")?.transform));
                    break;
                case UiResultWindow.ResultState.ChackSublimation:
                {
                    var bar = Reflect.Get<GameObject>(_win, "sub_messageBar");
                    if (bar != null) Speech.Say(Ui.ReadAll(bar.transform));
                    Focus.QueueNext();
                    break;
                }
            }
        }
    }
}
