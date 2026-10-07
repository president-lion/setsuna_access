using Setsuna;
using UnityEngine.UI;

namespace SetsunaAccess
{
    /// <summary>
    /// Speaks each page of text as the game starts typing it out, with the speaker's name
    /// when the balloon shows one.
    /// </summary>
    internal static class Dialogue
    {
        public static void OnPage(DisplayOneByOneText typer, string fullText)
        {
            var text = TextClean.Clean(fullText);
            if (text.Length == 0) return;

            string speaker = null;
            var interrupt = true;
            var balloon = typer.GetComponentInParent<UiMessageBalloon>();
            if (balloon != null)
            {
                // txtName is filled just before the page starts; disabled when nobody is named.
                var name = Reflect.Get<Text>(balloon, "txtName");
                if (name != null && name.enabled) speaker = TextClean.Clean(name.text);
                // Auto-advancing screen text shouldn't cut off its own previous page.
                interrupt = !balloon.IsAuto;
            }
            Speech.Say(Strings.Line(speaker, text), interrupt);
        }
    }
}
