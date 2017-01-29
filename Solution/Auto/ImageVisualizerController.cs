using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Debugger.Interop;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Operations;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Media.Imaging;

namespace ImageVisualizerVSPackage
{
    /// <summary>
    /// This class is responsible for listening mouse event from editor's text buffer and evaluate
    /// variables of byte array that represent BitmapImage againt ExpressionContext (Provided by the ImageVisualizerPackage).
    /// Upon variable value evaluation the controller is responsible to fetch the image into Image Viewer.
    /// </summary>
    internal class ImageVisualizerController : IIntellisenseController
    {
        #region Members

        // This is an assumption. Should be checked further.
        private const int c_variableMetaDataSizeInBytes = 8;
        private ITextView m_textView;
        private readonly IList<ITextBuffer> m_subjectBuffers;
        private readonly ImageVisualizerPackage m_package;
        private readonly ImageViewer m_imageVisualizer;
        private byte[] m_lastEvaluatedImageValue;

        #endregion

        #region Public Methods

        public void Detach(ITextView textView)
        {
            if (m_textView == textView)
            {
                m_textView.MouseHover -= this.OnTextViewMouseHover;
                m_textView = null;
            }
        }

        public void ConnectSubjectBuffer(ITextBuffer subjectBuffer)
        {
        }

        public void DisconnectSubjectBuffer(ITextBuffer subjectBuffer)
        {
        }

        #endregion

        #region Ctor

        internal ImageVisualizerController(ITextView textView, IList<ITextBuffer> subjectBuffers, ImageVisualizerPackage package)
        {
            m_textView = textView;
            m_subjectBuffers = subjectBuffers;
            m_package = package;
            m_textView.MouseHover += OnTextViewMouseHover;
            m_imageVisualizer = new ImageViewer();
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Text View Mouse Hover event handler. Responsible for showing and hiding image visualizer
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnTextViewMouseHover(object sender, MouseHoverEventArgs e)
        {
            //find the mouse position by mapping down to the subject buffer
            SnapshotPoint? point = m_textView.BufferGraph.MapDownToFirstMatch
                (new SnapshotPoint(m_textView.TextSnapshot, e.Position),
                    PointTrackingMode.Positive,
                    snapshot => m_subjectBuffers.Contains(snapshot.TextBuffer),
                    PositionAffinity.Predecessor);

            if (!point.HasValue)
            {
                m_imageVisualizer.Hide();
                return;
            }

            //look for occurrences of our QuickInfo words in the span
            ITextStructureNavigator navigator = m_package.NavigatorService.GetTextStructureNavigator(point.Value.Snapshot.TextBuffer);
            TextExtent extent = navigator.GetExtentOfWord(point.Value);
            string searchText = extent.Span.GetText();

            if (ImageVisualizerPackage.ExpressionContext != null && !string.IsNullOrWhiteSpace(searchText))
            {
                GetVariableValueAndShowImagePreview(searchText);
            }
        }

        /// <summary>
        /// Gets variable value from previously captured debugging context and shows window tool pane
        /// </summary>
        /// <param name="key">Variable name</param>
        private void GetVariableValueAndShowImagePreview(string key)
        {
            // Parse the expression string.
            IDebugExpression2 expression;
            string error;
            uint errorCharIndex;
            if (VSConstants.S_OK != ImageVisualizerPackage.ExpressionContext.ParseText(
                key,
                (uint) enum_PARSEFLAGS.PARSE_EXPRESSION, 10, out expression, out error, out errorCharIndex))
            {
                Debug.WriteLine("Failed to parse expression.");
            }

            // In case the debugger was detached
            if (expression == null)
            {
                return;
            }

            // Evaluate the parsed expression.
            IDebugProperty2 debugProperty = null;
            if (VSConstants.S_OK != expression.EvaluateSync((uint) enum_EVALFLAGS.EVAL_NOSIDEEFFECTS,
                unchecked((uint) Timeout.Infinite), null, out debugProperty))
            {
                Debug.WriteLine("Failed to evaluate expression.");
            }

            IEnumDebugPropertyInfo2 ppEnum;
            Guid g = Guid.Empty;
            if (VSConstants.S_OK != debugProperty.EnumChildren(
                (uint) enum_DEBUGPROP_INFO_FLAGS.DEBUGPROP_INFO_ALL,
                10,
                ref g,
                0x00000000ffffffff,/*DBG_ATTRIB_FLAGS.DBG_ATTRIB_ALL */
                string.Empty,
                unchecked((uint) Timeout.Infinite),
                out ppEnum))
            {
                Debug.WriteLine("Failed to evaluate expression.");
            }

            uint pcelt;
            ppEnum.GetCount(out pcelt);

            // Get memory context for the property.
            IDebugMemoryContext2 memoryContext;
            if (VSConstants.S_OK != debugProperty.GetMemoryContext(out memoryContext))
            {
                // In practice, this is where it seems to fail if you enter an invalid expression.
                Debug.WriteLine("Failed to get memory context.");
                m_imageVisualizer.Hide();
                return; //probably the key does not exist in the current context
            }

            // Get memory bytes interface.
            IDebugMemoryBytes2 memoryBytes;
            if (VSConstants.S_OK != debugProperty.GetMemoryBytes(out memoryBytes))
            {
                Debug.WriteLine("Failed to get memory bytes interface.");
            }

            // The number of bytes to read.
            uint dataSize = pcelt + c_variableMetaDataSizeInBytes;

            // Allocate space for the result.
            byte[] data = new byte[dataSize];
            uint writtenBytes = 0;

            // Read data from the debuggee.
            uint unreadable = 0;
            int hrr = memoryBytes.ReadAt(memoryContext, dataSize, data, out writtenBytes, ref unreadable);

            if (hrr != VSConstants.S_OK)
            {
                // Read failed.
            }
            else if (writtenBytes < dataSize)
            {
                // Read partially succeeded.
                
                // Implementation missing ! Need to continue looping until all needed data was retrieved
                //IDebugMemoryContext2 temp;
                //memoryContext.Add(writtenBytes + unreadable, out temp);
            }
            else
            {
                // Read successful.
                ReadingVariableValueSucceeded(key, data);
            }
        }

        /// <summary>
        /// This method is being called once we succeeded to read variable value
        /// </summary>
        /// <param name="variableName">Variable name</param>
        /// <param name="retrievedVariableData">Variable data (metadata + value)</param>
        private void ReadingVariableValueSucceeded(string variableName, byte[] retrievedVariableData)
        {
            var imageData = new byte[retrievedVariableData.Length - c_variableMetaDataSizeInBytes];

            for (var i = 0; i < imageData.Length; i++)
            {
                imageData[i] = retrievedVariableData[i + c_variableMetaDataSizeInBytes];
            }

            var image = GetImageFromByreArray(imageData);

            // In case the data was evaluated to an image
            if (image != null)
            {
                if (IsSameContent(imageData))
                {
                    // The content is the same, just update it's location
                    m_imageVisualizer.ShowAndSetCurrentCursorLocation();
                }

                // Let's show new content
                m_imageVisualizer.ImageData = image;
                m_imageVisualizer.ShowAndSetCurrentCursorLocation();
                m_lastEvaluatedImageValue = imageData;

            }
            // In case new data is not an image and the image visualizer is open
            else if(m_imageVisualizer.IsVisible)
            {
                m_imageVisualizer.Hide();
            }
        }

        /// <summary>
        /// Creates a BitmapImage from byte array.
        /// </summary>
        /// <param name="imageData">Byte array of BitmapImage</param>
        /// <returns>BitmapImage or null in case the array of bytes doesn't represent BitmapImage</returns>
        private BitmapImage GetImageFromByreArray(byte[] imageData)
        {
            if (imageData != null && imageData.Length > 0)
            {
                using (var memStream = new MemoryStream(imageData))
                {
                    try
                    {
                        var img = new BitmapImage();
                        img.BeginInit();
                        img.CacheOption = BitmapCacheOption.OnLoad;
                        img.StreamSource = memStream;
                        img.EndInit();
                        return img;
                    }
                    catch (NotSupportedException)
                    {
                        return null;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Checks if last content value and new are the same. Most probably that method might be more efficient
        /// </summary>
        /// <param name="newIamge">New content to check</param>
        /// <returns></returns>
        private bool IsSameContent(byte[] newIamge)
        {
            if (m_lastEvaluatedImageValue == null && newIamge != null)
            {
                return false;
            }

            if (m_lastEvaluatedImageValue != null && newIamge == null)
            {
                return false;
            }

            if (m_lastEvaluatedImageValue.Length != newIamge.Length)
            {
                return false;
            }

            for (int i = 0; i < newIamge.Length; i++)
            {
                if (m_lastEvaluatedImageValue[i] != newIamge[i])
                {
                    return false;
                }
            }

            return true;
        }

        #endregion
    }
}
