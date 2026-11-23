#nullable enable
using NAPS2.EtoForms;
using NAPS2.EtoForms.Desktop;
using NAPS2.PostScan;
using NAPS2.Scan;
using Xunit;

namespace NAPS2.Lib.Tests.Scan;

/// <summary>
/// What the document list and the canvas headings animate, and for how long. The timing comes from the
/// Leonardo tokens; what is pinned here is which event starts which motion, that nothing already there
/// when the window opens is announced as new, and that the frame timer only runs while it has to.
/// </summary>
public class DocumentMotionTests
{
    private DateTime _now = new(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
    private bool _animations = true;
    private int _tickersStarted;
    private readonly DocumentMotion _motion;

    public DocumentMotionTests()
    {
        _motion = new DocumentMotion(() => _animations, () => _now, _ =>
        {
            _tickersStarted++;
            return new Ticker();
        });
    }

    private sealed class Ticker : IDisposable
    {
        public void Dispose()
        {
        }
    }

    private static ScannedDocument Doc(DocumentStatus status = DocumentStatus.Pending) => new()
    {
        Profile = new ScanProfile(),
        ScannedPages = [],
        SequenceIndex = 0,
        Status = status
    };

    private void Wait(int ms) => _now = _now.AddMilliseconds(ms);

    [Fact]
    public void WhatIsThereWhenTheWindowOpensIsNotNew()
    {
        // Changing the language rebuilds the window; every document lighting up afterwards would be the
        // decorative motion at launch that Leonardo rules out.
        var existing = Doc(DocumentStatus.Done);
        var announced = new List<ScannedDocument>();
        _motion.Arrived += (_, d) => announced.Add(d);

        _motion.Observe([existing]);

        Assert.Equal(0, _motion.ArriveBar(existing.Id));
        Assert.Equal(0, _motion.Confirmation(existing.Id));
        Assert.Empty(announced);
        Assert.False(_motion.IsTicking);
    }

    [Fact]
    public void ANewDocumentArrivesWithAMarkerThatHoldsAndThenFades()
    {
        _motion.Observe([]);
        var document = Doc();
        var announced = new List<ScannedDocument>();
        _motion.Arrived += (_, d) => announced.Add(d);

        _motion.Observe([document]);

        Assert.Equal([document], announced);
        Assert.Equal(1, _motion.ArriveBar(document.Id));
        Wait(LeonardoTokens.Motion.ArriveHold - 1);
        Assert.Equal(1, _motion.ArriveBar(document.Id));
        Wait(1 + LeonardoTokens.Motion.DurationShort / 2);
        Assert.InRange(_motion.ArriveBar(document.Id), 0.4f, 0.6f);
        Wait(LeonardoTokens.Motion.DurationShort);
        Assert.Equal(0, _motion.ArriveBar(document.Id));
    }

    [Fact]
    public void AFiledDocumentIsGreenForAMomentAndThenNeutral()
    {
        var document = Doc();
        _motion.Observe([document]);

        document.Status = DocumentStatus.Done;
        _motion.Observe([document]);

        Assert.Equal(1, _motion.Confirmation(document.Id));
        Wait(LeonardoTokens.Motion.ArriveHold + LeonardoTokens.Motion.DurationNormal);
        Assert.Equal(0, _motion.Confirmation(document.Id));
    }

    [Fact]
    public void ADocumentPutBackInTheQueueLosesItsGreen()
    {
        var document = Doc();
        _motion.Observe([document]);
        document.Status = DocumentStatus.Done;
        _motion.Observe([document]);

        document.Status = DocumentStatus.Pending;
        _motion.Observe([document]);

        Assert.Equal(0, _motion.Confirmation(document.Id));
    }

    [Fact]
    public void ARemovedDocumentLeavesQuickerThanItArrives()
    {
        var document = Doc();
        _motion.Observe([document]);

        _motion.Observe([]);

        Assert.True(_motion.IsLeaving(document.Id));
        Assert.Equal(1, _motion.Leaving(document.Id));
        Wait(LeonardoTokens.Motion.DurationShort);
        Assert.Equal(0, _motion.Leaving(document.Id));
        Assert.True(LeonardoTokens.Motion.DurationShort < LeonardoTokens.Motion.DurationNormal);
    }

    [Fact]
    public void WithAnimationsOffEveryTransitionIsTheShortCrossfade()
    {
        _animations = false;
        var document = Doc();
        _motion.Observe([document]);
        document.Status = DocumentStatus.Done;
        _motion.Observe([document]);

        Wait(LeonardoTokens.Motion.ArriveHold + LeonardoTokens.Motion.DurationFade);

        Assert.Equal(0, _motion.Confirmation(document.Id));
    }

    [Fact]
    public void AClickEndsEverything()
    {
        _motion.Observe([]);
        var document = Doc(DocumentStatus.Done);
        _motion.Observe([document]);
        var frames = 0;
        _motion.Frame += (_, _) => frames++;

        _motion.SkipToEnd();

        Assert.Equal(0, _motion.ArriveBar(document.Id));
        Assert.Equal(0, _motion.Confirmation(document.Id));
        Assert.Equal(1, frames);
        Assert.False(_motion.IsTicking);
    }

    [Fact]
    public void TheTimerRunsOnlyWhileSomethingMovesAndNothingRepaintsDuringTheHold()
    {
        _motion.Observe([]);
        var document = Doc();
        _motion.Observe([document]);
        var frames = 0;
        _motion.Frame += (_, _) => frames++;

        Assert.Equal(1, _tickersStarted);
        Wait(100);
        _motion.Tick();
        Assert.Equal(0, frames);

        Wait(LeonardoTokens.Motion.ArriveHold);
        _motion.Tick();
        Assert.Equal(1, frames);

        Wait(LeonardoTokens.Motion.DurationShort);
        _motion.Tick();
        Assert.Equal(2, frames);
        Assert.False(_motion.IsTicking);
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(1f, 1f)]
    public void TheEasingCurvesStartAtZeroAndEndAtOne(float x, float expected)
    {
        Assert.Equal(expected, CubicBezier.Evaluate(LeonardoTokens.Motion.EaseEnter, x), 3);
        Assert.Equal(expected, CubicBezier.Evaluate(LeonardoTokens.Motion.EaseExit, x), 3);
        Assert.Equal(expected, CubicBezier.Evaluate(LeonardoTokens.Motion.EaseMove, x), 3);
    }

    [Fact]
    public void ArrivingDeceleratesAndLeavingAccelerates()
    {
        // The signature of Leonardo motion: halfway through, an entrance is nearly there and an exit has
        // barely started.
        Assert.True(CubicBezier.Evaluate(LeonardoTokens.Motion.EaseEnter, 0.5f) > 0.8f);
        Assert.True(CubicBezier.Evaluate(LeonardoTokens.Motion.EaseExit, 0.5f) < 0.2f);
        Assert.Equal(0.5f, CubicBezier.Evaluate(LeonardoTokens.Motion.EaseFade, 0.5f), 2);
    }
}
