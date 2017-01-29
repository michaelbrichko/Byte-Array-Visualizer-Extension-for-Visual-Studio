using EnvDTE90a;
using IOleServiceProvider = Microsoft.VisualStudio.OLE.Interop.IServiceProvider;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Debugger.Interop;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace ImageVisualizerVSPackage
{
    /// <summary>
    /// This is the class that implements the package exposed by this assembly.
    ///
    /// This class is responsible for listening debug events and retrieving ExpressionContext of the debug breakpoint.
    /// Once an ImageVisualizerController will receive a mouse hover event on some variable name that in the debug context
    /// we will be able to retrieve its data from that expression context
    /// 
    /// </summary>
    // This attribute tells the PkgDef creation utility (CreatePkgDef.exe) that this class is
    // a package.
    [PackageRegistration(UseManagedResourcesOnly = true)]
    // This attribute is used to register the information needed to show this package
    // in the Help/About dialog of Visual Studio.
    [InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400)]
    [Guid(GuidList.guidAutoPkgString)]
    [DefaultRegistryRoot(@"Software\Microsoft\VisualStudio\10.0")]
    [ProvideAutoLoad(UIContextGuids80.SolutionExists)]
    [Export(typeof(IIntellisenseControllerProvider))]
    [Name("Image Visualizer Controller")]
    [ContentType("text")]
    public sealed class ImageVisualizerPackage : Package, IDebugEventCallback2, IIntellisenseControllerProvider
    {
        #region Static members

        // Expression context that is being retrieved from the debug event. We will evaluate our expressions regarding this context
        public static IDebugExpressionContext2 ExpressionContext = null;

        #endregion

        #region Properties

        [Import]
        internal ITextStructureNavigatorSelectorService NavigatorService { get; set; }

        #endregion

        #region Default Ctor

        /// <summary>
        /// Default constructor of the package.
        /// Inside this method you can place any initialization code that does not require 
        /// any Visual Studio service because at this point the package object is created but 
        /// not sited yet inside Visual Studio environment. The place to do all the other 
        /// initialization is the Initialize method.
        /// </summary>
        public ImageVisualizerPackage()
        {
            base.Initialize();
            Debug.WriteLine(string.Format(CultureInfo.CurrentCulture, "Entering constructor for: {0}", this.ToString()));
        }

        #endregion

        #region Package Members

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited, so this is the place
        /// where you can put all the initialization code that rely on services provided by VisualStudio.
        /// </summary>
        protected override void Initialize()
        {
            Debug.WriteLine (string.Format(CultureInfo.CurrentCulture, "Entering Initialize() of: {0}", this.ToString()));

            // Get the Debugger service.
            IVsDebugger debugService = Package.GetGlobalService(typeof(SVsShellDebugger)) as IVsDebugger;
            if (debugService != null)
            {
                // Register for debug events.
                debugService.AdviseDebugEventCallback(this);
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// The event is being triggered in debug mode (any debug related event) and used to evaluate Expression Context of a variable whose value we want to retrieve
        /// </summary>
        /// <param name="pEngine">Object that represents the debug engine (DE) that is sending this event. A DE is required to fill out this parameter.</param>
        /// <param name="pProcess">Object that represents the process in which the event occurs. This parameter is filled in by the session debug manager (SDM). A DE always passes a null value for this parameter.</param>
        /// <param name="pProgram">Object that represents the program in which this event occurs. For most events, this parameter is not a null value</param>
        /// <param name="pThread">Object that represents the thread in which this event occurs. For stopping events, this parameter cannot be a null value as the stack frame is obtained from this parameter.</param>
        /// <param name="pEvent">Object that represents the debug event.</param>
        /// <param name="riidEvent">GUID that identifies which event interface to obtain from the pEvent parameter.</param>
        /// <param name="dwAttrib">A combination of flags from the EVENTATTRIBUTES enumeration.</param>
        /// <returns>If successful, returns S_OK; otherwise, returns an error code.</returns>
        public int Event(IDebugEngine2 pEngine, IDebugProcess2 pProcess, IDebugProgram2 pProgram, IDebugThread2 pThread, IDebugEvent2 pEvent, ref Guid riidEvent, uint dwAttrib)
        {
            // Get the automation API DTE object.
            EnvDTE.DTE dte = Package.GetGlobalService(typeof(SDTE)) as EnvDTE.DTE;
            if (dte == null)
            {
                Debug.WriteLine("Could not get DTE service.");
                return VSConstants.S_FALSE;
            }
            if (dte.Debugger.CurrentStackFrame == null)
            {
                // No current stack frame.
                return VSConstants.S_FALSE;
            }

            // Cast to StackFrame2, as it contains the Depth property that we need.
            StackFrame2 currentFrame2 = dte.Debugger.CurrentStackFrame as StackFrame2;
            if (currentFrame2 == null)
            {
                Debug.WriteLine("CurrentStackFrame is not a StackFrame2.");
                return VSConstants.S_FALSE;
            }

            // Depth property is 1-based.
            uint currentFrameDepth = currentFrame2.Depth - 1;

            if (pThread == null)
            {
                return VSConstants.S_FALSE;
            }

            // Get frame info enum interface.
            IEnumDebugFrameInfo2 enumDebugFrameInfo2;
            if (VSConstants.S_OK != pThread.EnumFrameInfo((uint)enum_FRAMEINFO_FLAGS.FIF_FRAME, 0, out enumDebugFrameInfo2))
            {
                Debug.WriteLine("Could not enumerate stack frames.");
                return VSConstants.S_FALSE;
            }

            // Skip frames above the current one.
            enumDebugFrameInfo2.Reset();
            if (VSConstants.S_OK != enumDebugFrameInfo2.Skip(currentFrameDepth))
            {
                Debug.WriteLine("Current stack frame could not be enumerated.");
                return VSConstants.S_FALSE;
            }

            // Get the current frame.
            FRAMEINFO[] frameInfo = new FRAMEINFO[1];
            uint fetched = 0;
            int hr = enumDebugFrameInfo2.Next(1, frameInfo, ref fetched);

            if (hr != VSConstants.S_OK || fetched != 1)
            {
                Debug.WriteLine("Failed to get current stack frame info.");
                return VSConstants.S_FALSE;
            }

            IDebugStackFrame2 stackFrame = frameInfo[0].m_pFrame;
            if (stackFrame == null)
            {
                Debug.WriteLine("Current stack frame is null.");
                return VSConstants.S_FALSE;
            }

            // Get a context for evaluating expressions.
            IDebugExpressionContext2 expressionContext;

            if (VSConstants.S_OK != stackFrame.GetExpressionContext(out expressionContext))
            {
                Debug.WriteLine("Failed to get expression context.");
                return VSConstants.S_FALSE;
            }

            // Set expression context
            ImageVisualizerPackage.ExpressionContext = expressionContext;

            return VSConstants.S_OK;
        }

        public IIntellisenseController TryCreateIntellisenseController(ITextView textView, IList<ITextBuffer> subjectBuffers)
        {
            return new ImageVisualizerController(textView, subjectBuffers, this);
        }

        #endregion
    }
}
