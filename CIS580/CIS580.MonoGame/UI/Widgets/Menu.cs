using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using CIS580.Input;

namespace CIS580.UI.Widgets;

/// <summary>
/// A vertical list of items that the player moves through and chooses from.
/// </summary>
/// <remarks>
/// The menu is a widget, not a layer: a layer owns it, passes it input, and draws it.
/// See <see cref="MenuLayer"/> for a layer that shows a titled menu.
/// </remarks>
public sealed class Menu
{
    /// <summary>
    /// The items, top to bottom
    /// </summary>
    public List<MenuItem> Items { get; } = new();

    /// <summary>
    /// The index of the selected item
    /// </summary>
    public int SelectedIndex { get; set; }

    /// <summary>
    /// The selected item, or null if the menu is empty
    /// </summary>
    public MenuItem Selected => SelectedIndex >= 0 && SelectedIndex < Items.Count ? Items[SelectedIndex] : null;

    /// <summary>
    /// The most items shown at once (zero shows them all). When there are more, the menu
    /// scrolls to keep the selected item in view, with markers showing there is more.
    /// </summary>
    public int MaxVisibleItems { get; set; }

    /// <summary>
    /// The index of the first item shown (more than zero when the menu has scrolled)
    /// </summary>
    public int FirstVisibleIndex { get; private set; }

    // Where each item was last drawn on screen, so the mouse can point at and click items
    private readonly Dictionary<MenuItem, Rectangle> _itemBounds = new();

    /// <summary>
    /// Adds an item, returning this menu so calls can be chained
    /// </summary>
    public Menu Add(MenuItem item)
    {
        Items.Add(item);
        return this;
    }

    /// <summary>
    /// Responds to navigation: up/down move the selection, left/right adjust the selected item,
    /// and confirm activates it. (Cancel is left to the layer that owns the menu.)
    /// </summary>
    public void HandleInput(UiInput ui, UiTheme theme)
    {
        if (Items.Count == 0)
            return;

        if (ui.WasPressed(UiControl.Up))
            MoveSelection(-1, theme);
        if (ui.WasPressed(UiControl.Down))
            MoveSelection(1, theme);

        var selected = Selected;
        if (selected == null || !selected.IsEnabled)
            return;

        if (ui.WasPressed(UiControl.Left, out var leftSource) && selected.Adjust(-1, leftSource))
            theme.Sounds.Play(UiSound.Move);
        if (ui.WasPressed(UiControl.Right, out var rightSource) && selected.Adjust(1, rightSource))
            theme.Sounds.Play(UiSound.Move);

        if (ui.WasPressed(UiControl.Confirm, out var confirmSource))
        {
            theme.Sounds.Play(UiSound.Confirm);
            selected.Activate(confirmSource);
        }
    }

    /// <summary>
    /// Responds to the mouse: pointing at an item selects it, left-clicking chooses it (like
    /// Confirm), right-clicking adjusts it backwards (like Left), and the scroll wheel moves the
    /// selection. Items are found where they were last drawn (see <see cref="SetItemBounds"/>).
    /// </summary>
    /// <param name="input">The state of the input devices</param>
    /// <param name="theme">The UI theme (for sounds)</param>
    /// <param name="source">Who the mouse counts as; if null, the mouse is ignored</param>
    /// <returns>True if an item was clicked</returns>
    public bool HandleMouse(InputState input, UiTheme theme, InputSource source)
    {
        if (Items.Count == 0 || source == null)
            return false;

        int wheel = input.ScrollWheelDelta;
        if (wheel != 0)
            MoveSelection(wheel > 0 ? -1 : 1, theme);

        int hovered = IndexAt(input.MousePosition);
        if (hovered < 0 || !Items[hovered].IsEnabled)
            return false;

        if (input.MouseMoved && hovered != SelectedIndex)
        {
            SelectedIndex = hovered;
            theme.Sounds.Play(UiSound.Move);
        }

        var item = Items[hovered];
        if (input.WasLeftClicked)
        {
            SelectedIndex = hovered;
            theme.Sounds.Play(UiSound.Confirm);
            item.Activate(source);
            return true;
        }
        if (input.WasRightClicked)
        {
            SelectedIndex = hovered;
            if (item.Adjust(-1, source))
                theme.Sounds.Play(UiSound.Move);
            return true;
        }
        return false;
    }

    /// <summary>
    /// The index of the (visible) item at a point on screen, or -1 if there isn't one
    /// </summary>
    public int IndexAt(Point point)
    {
        for (int i = 0; i < Items.Count; i++)
        {
            if (_itemBounds.TryGetValue(Items[i], out var bounds) && bounds.Contains(point))
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Determines if the mouse is over an item that can be clicked
    /// </summary>
    public bool IsClickableAt(Point point)
    {
        int index = IndexAt(point);
        return index >= 0 && Items[index].IsEnabled;
    }

    /// <summary>
    /// Records where an item is on screen, for the mouse. <see cref="Draw"/> does this itself;
    /// call it when drawing the items some other way (after <see cref="ClearItemBounds"/>).
    /// </summary>
    public void SetItemBounds(MenuItem item, Rectangle bounds) => _itemBounds[item] = bounds;

    /// <summary>
    /// Forgets where the items are (e.g. before drawing them again)
    /// </summary>
    public void ClearItemBounds() => _itemBounds.Clear();

    /// <summary>
    /// Animates the selection highlight
    /// </summary>
    /// <param name="gameTime">The game time</param>
    /// <param name="showSelection">Whether the selection should be shown (e.g. only when the menu has focus)</param>
    public void Update(GameTime gameTime, bool showSelection)
    {
        float fade = (float)gameTime.ElapsedGameTime.TotalSeconds * 4;
        for (int i = 0; i < Items.Count; i++)
        {
            var item = Items[i];
            bool isSelected = showSelection && i == SelectedIndex;
            item.SelectionFade = Math.Clamp(item.SelectionFade + (isSelected ? fade : -fade), 0f, 1f);
        }
    }

    /// <summary>
    /// Draws the menu with its items centered below a point, sliding in from the left as it
    /// transitions on and out to the right as it transitions off.
    /// </summary>
    /// <param name="spriteBatch">A SpriteBatch that has been begun</param>
    /// <param name="theme">The UI theme</param>
    /// <param name="topCenter">Where the center of the first item's top goes</param>
    /// <param name="transition">The owning layer's transition</param>
    /// <param name="gameTime">The game time</param>
    public void Draw(SpriteBatch spriteBatch, UiTheme theme, Vector2 topCenter, Transition transition, GameTime gameTime)
    {
        var font = theme.TextFont;
        float slide = transition.State == TransitionState.Exiting ? transition.Offset * 512 : -transition.Offset * 256;
        float pulse = (float)Math.Sin(gameTime.TotalGameTime.TotalSeconds * 6) + 1;

        int visible = ScrollToSelection();
        ClearItemBounds();

        var position = new Vector2(topCenter.X + slide, topCenter.Y + font.LineSpacing / 2f);
        var markerColor = theme.DisabledColor * transition.Alpha;
        if (FirstVisibleIndex > 0)
            DrawMarker(spriteBatch, font, "^", position - new Vector2(0, font.LineSpacing * 0.8f), markerColor);

        for (int i = FirstVisibleIndex; i < FirstVisibleIndex + visible; i++)
        {
            var item = Items[i];
            string text = item.Text;

            var color = !item.IsEnabled ? theme.DisabledColor
                : Color.Lerp(theme.TextColor, theme.SelectedColor, item.SelectionFade);
            float scale = 1 + pulse * 0.05f * item.SelectionFade;
            var origin = new Vector2(font.MeasureString(text).X / 2, font.LineSpacing / 2f);

            spriteBatch.DrawString(font, text, position, color * transition.Alpha, 0, origin, scale,
                SpriteEffects.None, 0);

            // the clickable area: the item's whole row, a little wider than its text
            float clickWidth = Math.Max(origin.X * 2, 120) + 40;
            SetItemBounds(item, new Rectangle((int)(position.X - clickWidth / 2), (int)(position.Y - font.LineSpacing / 2f),
                (int)clickWidth, font.LineSpacing));

            position.Y += font.LineSpacing;
        }

        if (FirstVisibleIndex + visible < Items.Count)
            DrawMarker(spriteBatch, font, "v", position - new Vector2(0, font.LineSpacing * 0.2f), markerColor);
    }

    /// <summary>
    /// Scrolls so the selected item is visible
    /// </summary>
    /// <returns>The number of items visible</returns>
    internal int ScrollToSelection()
    {
        int visible = MaxVisibleItems > 0 ? Math.Min(MaxVisibleItems, Items.Count) : Items.Count;
        if (SelectedIndex < FirstVisibleIndex)
            FirstVisibleIndex = SelectedIndex;
        else if (SelectedIndex >= FirstVisibleIndex + visible)
            FirstVisibleIndex = SelectedIndex - visible + 1;
        FirstVisibleIndex = Math.Clamp(FirstVisibleIndex, 0, Math.Max(0, Items.Count - visible));
        return visible;
    }

    private static void DrawMarker(SpriteBatch spriteBatch, SpriteFont font, string marker, Vector2 position, Color color)
    {
        var origin = new Vector2(font.MeasureString(marker).X / 2, font.LineSpacing / 2f);
        spriteBatch.DrawString(font, marker, position, color, 0, origin, 0.8f, SpriteEffects.None, 0);
    }

    private void MoveSelection(int direction, UiTheme theme)
    {
        // Step over disabled items, wrapping around the ends
        for (int i = 1; i <= Items.Count; i++)
        {
            int index = ((SelectedIndex + direction * i) % Items.Count + Items.Count) % Items.Count;
            if (Items[index].IsEnabled)
            {
                if (index != SelectedIndex)
                    theme.Sounds.Play(UiSound.Move);
                SelectedIndex = index;
                return;
            }
        }
    }
}
