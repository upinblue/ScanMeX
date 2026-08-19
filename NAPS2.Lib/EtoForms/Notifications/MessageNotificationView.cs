using Eto.Drawing;
using Eto.Forms;
using NAPS2.EtoForms.Layout;

namespace NAPS2.EtoForms.Notifications;

public class MessageNotificationView : NotificationView
{
    private readonly Label _title = new();
    private readonly Label _detail = new();

    public MessageNotificationView(MessageNotification model)
        : base(model)
    {
        _title.Text = model.Title;
        _title.Font = new Font(_title.Font.Family, _title.Font.Size, FontStyle.Bold);
        _detail.Text = model.Detail;
        // Something that went well behaves like a save notification. Anything that did not stays up
        // until it is dismissed, since it usually means someone has to act on it.
        HideTimeout = model.Severity is NotificationSeverity.Success or NotificationSeverity.Neutral
            ? HIDE_SHORT
            : 0;
    }

    protected override void BeforeCreateContent()
    {
        _title.BackgroundColor = _detail.BackgroundColor = BackgroundColor;
    }

    /// <summary>
    /// A failed upload usually means the document never reached the archive and someone has to act,
    /// so it gets the critical tint rather than the plain card it used to share with a success.
    /// </summary>
    protected override NotificationSeverity Severity =>
        Model is MessageNotification message ? message.Severity : NotificationSeverity.Neutral;

    protected override LayoutElement PrimaryContent => _title.DynamicWrap(180).MaxWidth(180).Scale();

    protected override LayoutElement SecondaryContent => _detail.DynamicWrap(180).MaxWidth(180);
}
