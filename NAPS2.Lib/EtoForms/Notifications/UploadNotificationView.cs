using Eto.Drawing;
using Eto.Forms;
using NAPS2.EtoForms.Layout;

namespace NAPS2.EtoForms.Notifications;

public class UploadNotificationView : NotificationView
{
    private readonly Label _title = new();
    private readonly Label _detail = new();

    public UploadNotificationView(UploadNotification model)
        : base(model)
    {
        _title.Text = model.Title;
        _title.Font = new Font(_title.Font.Family, _title.Font.Size, FontStyle.Bold);
        _detail.Text = model.Detail;
        // A successful upload behaves like a save notification. A failure stays up until it's dismissed,
        // since it usually means the document didn't reach the archive and someone has to act on it.
        HideTimeout = model.IsError ? 0 : HIDE_SHORT;
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
        Model is UploadNotification { IsError: true } ? NotificationSeverity.Error : NotificationSeverity.Success;

    protected override LayoutElement PrimaryContent => _title.DynamicWrap(180).MaxWidth(180).Scale();

    protected override LayoutElement SecondaryContent => _detail.DynamicWrap(180).MaxWidth(180);
}
