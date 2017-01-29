using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ImageVisualizerVSPackage
{
    /// <summary>
    /// Interaction logic for ImageViewer.xaml
    /// </summary>
    public partial class ImageViewer : Window
    {
        #region Ctor

        public ImageViewer()
        {
            InitializeComponent();

            // The window should be top most of all other windows
            Topmost = true;
        }

        #endregion

        #region Properties

        public BitmapImage ImageData
        {
            set
            {
                VisualizedImage.Source = value;
                VisualizedImage.Stretch = Stretch.Fill;
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Thgis is designated method for opening ImageViewer
        /// </summary>
        public void ShowAndSetCurrentCursorLocation()
        {
            Show();
            MoveBottomRightEdgeOfWindowToMousePosition();
        }

        #endregion

        #region Protected Methods

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            MoveBottomRightEdgeOfWindowToMousePosition();
        }

        #endregion

        #region Privare Methods

        /// <summary>
        /// Sets Window position relativly to mouse cursor
        /// </summary>
        private void MoveBottomRightEdgeOfWindowToMousePosition()
        {
            var transform = PresentationSource.FromVisual(this).CompositionTarget.TransformFromDevice;
            var mouse = transform.Transform(GetMousePosition());
            Left = mouse.X;
            Top = mouse.Y - ActualHeight;
        }

        /// <summary>
        /// Retrieves mouse current position
        /// </summary>
        /// <returns></returns>
        private Point GetMousePosition()
        {
            System.Drawing.Point point = System.Windows.Forms.Control.MousePosition;
            return new Point(point.X, point.Y);
        }

        // In case user moves mouse inside the image viewer
        private void ImagePreview_OnMouseMove(object sender, MouseEventArgs e)
        {
            Hide();
        }

        #endregion
    }
}
