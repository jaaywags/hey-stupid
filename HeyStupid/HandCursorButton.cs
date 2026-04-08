namespace HeyStupid
{
    using Microsoft.UI.Input;
    using Microsoft.UI.Xaml.Controls;

    public class HandCursorButton : Button
    {
        public HandCursorButton()
        {
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
        }
    }
}