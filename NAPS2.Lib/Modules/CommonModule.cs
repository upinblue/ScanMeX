using Autofac;
using Microsoft.Extensions.Logging;
using NAPS2.EtoForms;
using NAPS2.EtoForms.Desktop;
using NAPS2.ImportExport;
using NAPS2.ImportExport.Email;
using NAPS2.ImportExport.Email.Mapi;
using NAPS2.Ocr;
using NAPS2.Pdf;
using NAPS2.Platform.Windows;
using NAPS2.PostScan;
using NAPS2.Recovery;
using NAPS2.Remoting;
using NAPS2.Remoting.Server;
using NAPS2.Remoting.Worker;
using NAPS2.Scan;
using NAPS2.Scan.Internal;

namespace NAPS2.Modules;

/// <summary>
/// Core module used by all entry points.
/// </summary>
public class CommonModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        // Export
        builder.RegisterType<AutofacEmailProviderFactory>().As<IEmailProviderFactory>();
        builder.RegisterType<StubMapiWrapper>().As<IMapiWrapper>();
        builder.RegisterType<OcrRequestQueue>().AsSelf().SingleInstance();

        // Scan
        builder.RegisterType<ScanPerformer>().As<IScanPerformer>().SingleInstance();
        builder.RegisterType<LocalPostProcessor>().As<ILocalPostProcessor>();
        builder.RegisterType<RemotePostProcessor>().As<IRemotePostProcessor>();
        builder.RegisterType<ScanBridgeFactory>().As<IScanBridgeFactory>();
        builder.RegisterType<ScanDriverFactory>().As<IScanDriverFactory>();
        builder.RegisterType<RemoteScanController>().As<IRemoteScanController>();
        builder.RegisterType<InProcScanBridge>().AsSelf();
        builder.RegisterType<WorkerScanBridge>().AsSelf();

        // Config
        // TODO: Make this a usable path on Mac/Linux
        var config = new Naps2Config(Path.Combine(Paths.Executable, "appsettings.xml"),
            Path.Combine(Paths.AppData, "config.xml"));
        builder.RegisterInstance(config);
        builder.RegisterBuildCallback(ctx =>
        {
            if (EtoPlatform.HasCurrent)
            {
                EtoPlatform.Current.ColorScheme.Config = ctx.Resolve<Naps2Config>();
            }
        });

        // Remoting
        builder.Register<IWorkerFactory>(_ => WorkerFactory.CreateDefault()).SingleInstance();
        builder.Register<ISharedDeviceManager>(ctx =>
            new SharedDeviceManager(
                ctx.Resolve<ScanningContext>(),
                ctx.Resolve<Naps2Config>(),
                Path.Combine(Paths.AppData, "sharing.xml"))).SingleInstance();
        builder.RegisterInstance(ProcessCoordinator.CreateDefault());

        // Logging
        var lazyLogger = new LazyLogger(() =>
            NLogConfig.CreateLogger(() => config.Get(c => c.EnableDebugLogging)));
        NLogConfig.EnvDebugLogging = config.Get(c => c.EnableDebugLogging);
        builder.RegisterInstance<ILogger>(lazyLogger);

        // Misc
        builder.RegisterType<AutofacFormFactory>().As<IFormFactory>();
        builder.RegisterType<AutofacOperationFactory>().As<IOperationFactory>();
        builder.RegisterType<UiImageList>().AsSelf().SingleInstance();
        builder.RegisterType<StillImage>().AsSelf().SingleInstance();
        // Shared between the automatic and the manual upload trigger, so it has to outlive a single scan.
        builder.RegisterType<DocumentQueue>().AsSelf().SingleInstance();
        builder.RegisterType<DocumentWriter>().AsSelf().SingleInstance();
        builder.RegisterType<DocumentUploadService>().AsSelf().SingleInstance();
        // Subscribes to the image list, so there must only ever be one of it; the pipeline is what pulls
        // it into existence, which is early enough because a document cannot exist before a scan.
        builder.RegisterType<DocumentPageTracker>().AsSelf().SingleInstance();
        builder.RegisterType<DocumentPipeline>().AsSelf().SingleInstance();
        builder.RegisterType<DocumentUploadController>().AsSelf().SingleInstance();
        // Holds the card views, so it has to be the same instance the window built its layout from.
        builder.RegisterType<DocumentPanel>().AsSelf().SingleInstance();
        builder.RegisterType<DocumentSectionBuilder>().AsSelf().SingleInstance();
        builder.RegisterType<DocumentEditor>().AsSelf().SingleInstance();
        // TODO: Use PdfiumWorkerCoordinator?
        builder.RegisterType<PdfiumPdfRenderer>().As<IPdfRenderer>();
        builder.RegisterType<OcrOperationManager>().AsSelf().SingleInstance();
        builder.RegisterType<ThumbnailController>().AsSelf().SingleInstance();
        builder.RegisterType<ThumbnailRenderQueue>().AsSelf().SingleInstance();
        builder.RegisterType<DefaultIconProvider>().As<IIconProvider>();
        builder.RegisterType<RecoveryManager>().AsSelf();
        builder.RegisterType<DeviceCapsCache>().AsSelf().SingleInstance();

        // ScanningContext has several properties that need to be populated. We do some here, and also some in
        // GuiModule/ConsoleModule/WorkerModule as they each have their own needs.
        builder.RegisterType<ScanningContext>().AsSelf().SingleInstance();
        builder.RegisterBuildCallback(ctx =>
        {
            var scanningContext = ctx.Resolve<ScanningContext>();
            scanningContext.WorkerFactory = ctx.Resolve<IWorkerFactory>();
            scanningContext.Logger = ctx.Resolve<ILogger>();
            scanningContext.TempFolderPath = Paths.Temp;
            scanningContext.RecoveryPath = Paths.Recovery;
        });

        //container.Resolve<ImageContext>().PdfRenderer = container.Resolve<PdfiumWorkerCoordinator>();

        builder.Register<IProfileManager>(ctx =>
        {
            var config = ctx.Resolve<Naps2Config>();
            return new ProfileManager(
                Path.Combine(Paths.AppData, "profiles.xml"),
                // TODO: Make this a usable path on Mac/Linux
                Path.Combine(AssemblyHelper.EntryFolder, "profiles.xml"),
                config.Get(c => c.LockSystemProfiles),
                config.Get(c => c.LockUnspecifiedDevices),
                config.Get(c => c.NoUserProfiles));
        }).SingleInstance();

        builder.Register(ctx =>
        {
            var config = ctx.Resolve<Naps2Config>();
            var customComponentsPath = config.Get(c => c.ComponentsPath);
            var componentsPath = string.IsNullOrWhiteSpace(customComponentsPath)
                ? Paths.Components
                : Environment.ExpandEnvironmentVariables(customComponentsPath);
            return new TesseractLanguageManager(componentsPath);
        }).SingleInstance();
        builder.Register<IOcrEngine>(ctx =>
        {
            var engine = TesseractOcrEngine.BundledWithModes(ctx.Resolve<TesseractLanguageManager>().TessdataBasePath);
            var errorOutput = ctx.Resolve<ErrorOutput>();
            engine.OcrError += (_, args) => errorOutput.DisplayError(SdkResources.OcrError, args.Exception);
            engine.OcrTimeout += (_, _) => errorOutput.DisplayError(SdkResources.OcrTimeout);
            return engine;
        }).SingleInstance();
    }
}