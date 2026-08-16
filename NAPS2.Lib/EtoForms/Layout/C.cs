using System.Windows.Input;
using Eto.Drawing;
using Eto.Forms;

namespace NAPS2.EtoForms.Layout;

public static class C
{
    /// <summary>
    /// Creates a label with wrapping disabled. For WinForms support, all labels must not wrap.
    /// </summary>
    /// <param name="text"></param>
    /// <returns></returns>
    public static Label NoWrap(string text) =>
        new Label { Text = text, Wrap = WrapMode.None };

    /// <summary>
    /// Creates a link button with the specified text.
    /// </summary>
    /// <param name="text"></param>
    /// <returns></returns>
    public static LinkButton Link(string text)
    {
        var link = new LinkButton { Text = text };
        if (EtoPlatform.Current.IsWinForms)
        {
            // TODO: Remove this when https://github.com/dotnet/winforms/issues/11935 is fixed
            link.TextColor = EtoPlatform.Current.ColorScheme.LinkColor;
        }
        return link;
    }

    /// <summary>
    /// Creates a link button with the specified text and action.
    /// </summary>
    /// <param name="text"></param>
    /// <param name="onClick"></param>
    /// <returns></returns>
    public static LinkButton Link(string text, Action onClick)
    {
        var link = Link(text);
        link.Command = new ActionCommand(onClick);
        return link;
    }

    /// <summary>
    /// Creates a link button with the given URL as both text and click action.
    /// </summary>
    /// <param name="url"></param>
    /// <param name="label"></param>
    /// <returns></returns>
    public static LinkButton UrlLink(string url, string? label = null)
    {
        void OnClick() => ProcessHelper.OpenUrl(url);
        return Link(label ?? url, OnClick);
    }

    /// <summary>
    /// Creates a button with the specified text.
    /// </summary>
    /// <param name="text"></param>
    /// <returns></returns>
    public static Button Button(string text) => new() { Text = text };

    /// <summary>
    /// Creates a button with the specified text and action.
    /// </summary>
    /// <param name="text"></param>
    /// <param name="onClick"></param>
    /// <returns></returns>
    public static Button Button(string text, Action onClick) =>
        Button(text, new ActionCommand(onClick));

    /// <summary>
    /// Creates a button with the specified text and command.
    /// </summary>
    /// <param name="text"></param>
    /// <param name="command"></param>
    /// <returns></returns>
    public static Button Button(string text, ActionCommand command)
    {
        var button = new Button
        {
            Text = text,
            Command = command
        };
        if (string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(command.ToolTip))
        {
            button.ToolTip = command.ToolTip;
        }
        return button;
    }

    public static Button Button(ActionCommand command) => Button(command.Text, command);

    public static Button Button(ActionCommand command, ButtonImagePosition imagePosition, ButtonFlags flags = default)
    {
        return Button(command, command.IconName, imagePosition, flags);
    }

    public static Button Button(ActionCommand command, string? iconName, ButtonImagePosition imagePosition = default,
        ButtonFlags flags = default)
    {
        var button = Button(imagePosition == ButtonImagePosition.Overlay ? "" : command.MenuText, command);
        if (command.Image != null)
        {
            EtoPlatform.Current.AttachDpiDependency(button,
                scale =>
                {
                    int targetSize = flags.HasFlag(ButtonFlags.LargeIcon) ? 32 : 16;
                    button.Image = command.Image.Clone().ResizeTo((int) (targetSize * scale));
                });
        }
        else if (iconName != null)
        {
            bool oversized = imagePosition == ButtonImagePosition.Above && flags.HasFlag(ButtonFlags.LargeIcon);
            // The icon provider tints for the window background. An accent button is filled, so its
            // glyph has to be re-tinted for the accent instead or it disappears into it.
            bool onAccent = flags.HasFlag(ButtonFlags.Accent);
            EtoPlatform.Current.AttachDpiDependency(button, scale =>
            {
                var icon = EtoPlatform.Current.IconProvider.GetIcon(iconName, scale, oversized);
                button.Image = onAccent && icon != null
                    ? icon.Tint(EtoPlatform.Current.ColorScheme.AccentForegroundColor)
                    : icon;
            });
        }
        button.ImagePosition = imagePosition;
        if (flags.HasFlag(ButtonFlags.LargeText))
        {
            var baseFontSize = button.Font.Size;
            EtoPlatform.Current.AttachDpiDependency(button,
                _ => button.Font = new Font(button.Font.Family, baseFontSize * 4 / 3));
        }
        EtoPlatform.Current.ConfigureImageButton(button, flags);
        return button;
    }

    // TODO: Clean up button overloads
    public static Button ImageButton(Command command) =>
        new Button
        {
            Text = command.MenuText,
            Command = command,
            Image = command.Image
        };

    /// <summary>
    /// Creates a null placeholder for Eto layouts that absorbs scaling.
    /// </summary>
    /// <returns></returns>
    public static LayoutControl Filler() =>
        new LayoutControl(null).Scale();

    /// <summary>
    /// Creates a null placeholder for Eto layouts.
    /// </summary>
    /// <returns></returns>
    public static LayoutControl Spacer() =>
        new LayoutControl(null);

    /// <summary>
    /// Creates an label of default height to be used as a visual paragraph separator.
    /// </summary>
    /// <returns></returns>
    public static LayoutElement TextSpace() => NoWrap(" ");

    public static Label Label(string text) => new() { Text = text };

    // The Fluent type ramp is defined in absolute pixels (Body 14/20, Body Strong 14/20 semibold,
    // Subtitle 20/28 semibold). These helpers scale the *app's* base font instead, because setting
    // absolute sizes would mean overriding the size in every form that uses one, and any form whose
    // layout was measured for the smaller default could clip -- German labels are long enough to
    // make that a real risk. The ratios below match the ramp's relative steps.
    //
    // Bold rather than Semibold: Fluent asks for Semibold, but that is a separate font family
    // ("Segoe UI Semibold") that does not exist on Gtk or Mac, and the rest of this codebase already
    // emphasises with Bold (see the notification views). Consistency beats the weight difference.

    /// <summary>
    /// Fluent's Body Strong: a label that titles a group of controls without being a heading.
    /// </summary>
    public static Label BodyStrong(string text)
    {
        var label = NoWrap(text);
        label.Font = new Font(label.Font.Family, label.Font.Size, FontStyle.Bold);
        return label;
    }

    /// <summary>
    /// Fluent's Subtitle: the heading of an empty state or a page-level section.
    /// </summary>
    public static Label Subtitle(string text)
    {
        var label = NoWrap(text);
        label.Font = new Font(label.Font.Family, label.Font.Size * 5 / 3, FontStyle.Bold);
        return label;
    }

    /// <summary>
    /// Secondary text: the explanatory line under a heading, or a caption. Dimmer than body text so
    /// the hierarchy reads without another font size.
    /// </summary>
    public static Label Secondary(string text)
    {
        var label = NoWrap(text);
        label.TextColor = EtoPlatform.Current.ColorScheme.SecondaryTextColor;
        return label;
    }

    public static DropDown DropDown(bool scale = true)
    {
        var dropDown = new DropDown();
        EtoPlatform.Current.ConfigureDropDown(dropDown, scale);
        return dropDown;
    }

    public static CheckBox CheckBox(string text) => new() { Text = text };

    public static Button CancelButton(Dialog dialog, string? text = null) =>
        DialogButton(dialog, text ?? UiStrings.Cancel, isAbort: true);

    public static Button OkButton(Dialog dialog, Action? beforeClose = null, string? text = null) =>
        DialogButton(dialog, text ?? UiStrings.OK, isDefault: true, beforeClose: beforeClose);

    public static Button OkButton(Dialog dialog, Func<bool>? beforeCloseWithCancel, string? text = null) =>
        DialogButton(dialog, text ?? UiStrings.OK, isDefault: true, beforeCloseWithCancel: beforeCloseWithCancel);

    public static Button DialogButton(Dialog dialog, string text, bool isDefault = false, bool isAbort = false,
        Action? beforeClose = null, Func<bool>? beforeCloseWithCancel = null)
    {
        var button = Button(text, () =>
        {
            if (!(beforeCloseWithCancel?.Invoke() ?? true))
            {
                return;
            }
            beforeClose?.Invoke();
            dialog.Close();
        });
        if (isDefault)
        {
            dialog.DefaultButton = button;
        }
        if (isAbort)
        {
            dialog.AbortButton = button;
        }
        return button;
    }

    private static IEnumerable<Control> GetAllControls(Control control)
    {
        if (control is not Container container) return Enumerable.Repeat(control, 1);
        return container.Controls.SelectMany(GetAllControls).Append(control);
    }

    public static LayoutElement None()
    {
        return new SkipLayoutElement();
    }

    public static LayoutControl IconButton(string iconName, Action onClick)
    {
        var button = new Button
        {
            ImagePosition = ButtonImagePosition.Overlay
        };
        EtoPlatform.Current.AttachDpiDependency(button, scale =>
        {
            var icon = EtoPlatform.Current.IconProvider.GetIcon(iconName, scale)!;
            button.Image = icon;
            button.MinimumSize = new Size(icon.Width + 30, 0);
        });
        button.Click += (_, _) => onClick();
        return button.Width(40);
    }

    public static MenuItem ButtonMenuItem(Window window, ActionCommand command)
    {
        var menuItem = new ButtonMenuItem(command);
        // TODO: Can we fix this memory leak?
        command.TextChanged += (_, _) => menuItem.Text = command.MenuText;
        EtoPlatform.Current.AttachDpiDependency(window, scale =>
        {
            EtoPlatform.Current.SetImageSize(menuItem, (int) (16 * scale));
            menuItem.Image = command.GetIconImage(scale);
        });
        return menuItem;
    }
}