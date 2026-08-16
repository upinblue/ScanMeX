namespace NAPS2.Operation;

/// <summary>
/// An <see cref="IProgress{T}"/> that runs its callback on the thread that reported, rather than posting
/// it elsewhere.
/// </summary>
/// <remarks>
/// <see cref="Progress{T}"/> captures the calling <see cref="SynchronizationContext"/>, and an upload runs
/// on a background thread that has none -- so its callbacks go to the thread pool, may arrive out of
/// order, and may arrive after the upload has already finished. For a progress bar that shows up as the
/// percentage jumping backwards, or as a finished upload still reading "uploading". The operations set
/// their status from the worker thread everywhere else and marshal to the UI in InvokeStatusChanged, so
/// reporting inline is both simpler and the behaviour the rest of the class already assumes.
/// </remarks>
public sealed class InlineProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;

    public InlineProgress(Action<T> handler) => _handler = handler;

    public void Report(T value) => _handler(value);
}
