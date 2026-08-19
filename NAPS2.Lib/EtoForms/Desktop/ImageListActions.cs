using NAPS2.EtoForms.Notifications;
using NAPS2.EtoForms.Widgets;
using NAPS2.ImportExport;
using NAPS2.PostScan;

namespace NAPS2.EtoForms.Desktop;

public class ImageListActions
{
    private readonly UiImageList _imageList;
    private readonly IOperationFactory _operationFactory;
    private readonly OperationProgress _operationProgress;
    private readonly Naps2Config _config;
    private readonly ThumbnailController _thumbnailController;
    private readonly IExportController _exportController;
    private readonly INotify _notify;
    private readonly EditWithController _editWithController;
    private readonly DocumentPageTracker _pageTracker;

    public ImageListActions(UiImageList imageList, IOperationFactory operationFactory,
        OperationProgress operationProgress, Naps2Config config, ThumbnailController thumbnailController,
        IExportController exportController, INotify notify, EditWithController editWithController,
        DocumentPageTracker pageTracker)
    {
        _imageList = imageList;
        _operationFactory = operationFactory;
        _operationProgress = operationProgress;
        _config = config;
        _thumbnailController = thumbnailController;
        _exportController = exportController;
        _notify = notify;
        _editWithController = editWithController;
        _pageTracker = pageTracker;
    }

    private Func<ListSelection<UiImage>>? SelectionFunc { get; init; }

    private ListSelection<UiImage>? Selection => SelectionFunc?.Invoke();

    public ImageListActions WithSelection(Func<ListSelection<UiImage>> selectionFunc)
    {
        return new ImageListActions(_imageList, _operationFactory, _operationProgress, _config,
            _thumbnailController, _exportController, _notify, _editWithController, _pageTracker)
        {
            SelectionFunc = selectionFunc
        };
    }

    public void MoveDown()
    {
        if (MayRearrange())
        {
            _imageList.Mutate(new ImageListMutation.MoveDown(), Selection);
        }
    }

    public void MoveUp()
    {
        if (MayRearrange())
        {
            _imageList.Mutate(new ImageListMutation.MoveUp(), Selection);
        }
    }

    public void MoveTo(int index)
    {
        if (MayRearrange() && MayDropAt(index))
        {
            _imageList.Mutate(new ImageListMutation.MoveTo(index), Selection);
        }
    }

    /// <summary>
    /// Clearing the window is the session starting over, not an edit to a document: a finished document
    /// keeps its own record of what reached the archive, so it survives this untouched.
    /// </summary>
    public void DeleteAll() => _imageList.Mutate(new ImageListMutation.DeleteAll(), Selection);

    public void DeleteSelected()
    {
        var selection = Selection ?? _imageList.Selection;
        var keep = selection.Where(IsArchived).ToList();
        if (keep.Count == selection.Count && keep.Count > 0)
        {
            RefuseArchivedEdit();
            return;
        }
        if (keep.Count > 0)
        {
            // The rest still goes. Leaving the archived pages behind and saying so beats refusing the
            // whole thing, which would make the operator pick the selection apart by hand.
            ScanConsole.Document(
                $"{keep.Count} of the selected page(s) belong to a document that is already archived and " +
                "were left in the window.");
            _notify.Refused(UiStrings.ArchivedPagesRefusedTitle,
                string.Format(UiStrings.ArchivedPagesKept, keep.Count));
            _imageList.Mutate(new ImageListMutation.DeleteSelected(),
                ListSelection.From(selection.Except(keep)));
            return;
        }
        _imageList.Mutate(new ImageListMutation.DeleteSelected(), Selection);
    }

    /// <summary>Whether a page belongs to a document that has already reached the archive.</summary>
    private bool IsArchived(UiImage page) =>
        _pageTracker.DocumentFor(page)?.Status == DocumentStatus.Done;

    /// <summary>
    /// Whether the selected pages may be moved at all. The pages of a finished document are the record
    /// that exactly those pages are in the archive, so rearranging them would say something about the
    /// archive that is not true.
    /// </summary>
    private bool MayRearrange()
    {
        var selection = Selection ?? _imageList.Selection;
        if (!selection.Any(IsArchived))
        {
            return true;
        }
        RefuseArchivedEdit();
        return false;
    }

    /// <summary>
    /// Whether the pages may land where they are being dropped: not inside a finished document, and not
    /// in a document belonging to a different profile -- that would move them to another folder, another
    /// name and another archive without anything saying so.
    /// </summary>
    private bool MayDropAt(int index)
    {
        var pages = _imageList.Images;
        if (pages.Count == 0)
        {
            return true;
        }
        // The page comes to rest after the one above the drop position, and that is the document it
        // would join -- the same page DocumentPageAssignment takes its answer from, so the guard and the
        // rule cannot disagree about where a drop at a boundary lands.
        var anchor = pages[index > 0 ? Math.Min(index - 1, pages.Count - 1) : 0];
        var target = _pageTracker.DocumentFor(anchor);
        if (target == null)
        {
            return true;
        }
        if (target.Status == DocumentStatus.Done)
        {
            RefuseArchivedEdit();
            return false;
        }
        var selection = Selection ?? _imageList.Selection;
        var sources = selection
            .Select(_pageTracker.DocumentFor)
            .Where(x => x != null)
            .Distinct()
            .ToList();
        if (sources.All(x => ReferenceEquals(x!.Profile, target.Profile)))
        {
            return true;
        }
        ScanConsole.Document(
            $"A page was dropped into {target.Describe()}, which was scanned with a different profile; " +
            "the move was refused.");
        _notify.Refused(UiStrings.CrossProfileMoveRefusedTitle, UiStrings.CrossProfileMoveRefused);
        return false;
    }

    private void RefuseArchivedEdit()
    {
        ScanConsole.Document(
            "The selected page(s) belong to a document that is already archived; the edit was refused.");
        _notify.Refused(UiStrings.ArchivedPagesRefusedTitle, UiStrings.ArchivedPagesRefused);
    }

    public void Interleave() => _imageList.Mutate(new ImageListMutation.Interleave(), Selection);

    public void Deinterleave() => _imageList.Mutate(new ImageListMutation.Deinterleave(), Selection);

    public void AltInterleave() => _imageList.Mutate(new ImageListMutation.AltInterleave(), Selection);

    public void AltDeinterleave() => _imageList.Mutate(new ImageListMutation.AltDeinterleave(), Selection);

    public void ReverseAll() => _imageList.Mutate(new ImageListMutation.ReverseAll(), Selection);

    public void ReverseSelected() => _imageList.Mutate(new ImageListMutation.ReverseSelection(), Selection);

    public async Task RotateLeft() =>
        await _imageList.MutateAsync(new ImageListMutation.RotateFlip(270), Selection);

    public async Task RotateRight() =>
        await _imageList.MutateAsync(new ImageListMutation.RotateFlip(90), Selection);

    public async Task Flip() => await _imageList.MutateAsync(new ImageListMutation.RotateFlip(180), Selection);

    public void DocumentCorrection() =>
        _imageList.Mutate(new ImageListMutation.AddTransforms([new CorrectionTransform(CorrectionMode.Document)]),
            Selection);

    // TODO: Does it make sense to move this method somewhere else?
    public void Deskew()
    {
        var images = Selection ?? _imageList.Selection;
        if (!images.Any())
        {
            return;
        }

        var op = _operationFactory.Create<DeskewOperation>();
        if (op.Start(_imageList, images.ToList(), new DeskewParams { ThumbnailSize = _thumbnailController.RenderSize }))
        {
            _operationProgress.ShowProgress(op);
        }
    }

    public async Task RotateFlip(double angle) =>
        await _imageList.MutateAsync(new ImageListMutation.RotateFlip(angle), Selection);

    public void ResetTransforms() => _imageList.Mutate(new ImageListMutation.ResetTransforms(), Selection);

    public void SelectAll() => _imageList.UpdateSelection(ListSelection.From(_imageList.Images));

    public async Task Undo() => await _imageList.Undo();

    public async Task Redo() => await _imageList.Redo();

    public Task SaveAllAsPdf() => _exportController.SavePdf(_imageList.Images, _notify);
    public Task SaveSelectedAsPdf() => _exportController.SavePdf(_imageList.Selection, _notify);
    public Task SaveAllAsImages() => _exportController.SaveImages(_imageList.Images, _notify);
    public Task SaveSelectedAsImages() => _exportController.SaveImages(_imageList.Selection, _notify);
    public Task SaveAllAsPdfOrImages() => _exportController.SavePdfOrImages(_imageList.Images, _notify);
    public Task SaveSelectedAsPdfOrImages() => _exportController.SavePdfOrImages(_imageList.Selection, _notify);
    public Task EmailAllAsPdf() => _exportController.EmailPdf(_imageList.Images);
    public Task EmailSelectedAsPdf() => _exportController.EmailPdf(_imageList.Selection);
    
    public void EditWithApp() => _editWithController.EditWithApp(Selection ?? _imageList.Selection);
    public void EditWithPick() => _editWithController.EditWithPick(Selection ?? _imageList.Selection);
}