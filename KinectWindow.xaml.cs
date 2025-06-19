//------------------------------------------------------------------------------
// <copyright file="KinectWindow.xaml.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

namespace Microsoft.Samples.Kinect.KinectExplorer
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.IO;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Data;
    using System.Windows.Input;
    using System.Windows.Media;
    using System.Windows.Media.Imaging;
    using System.Windows.Threading;
    using Microsoft.Kinect;
    using Microsoft.Samples.Kinect.WpfViewers;

    /// <summary>
    /// Interaction logic for KinectWindow.xaml.
    /// </summary>
    public partial class KinectWindow : Window
    {
        public static readonly DependencyProperty KinectSensorProperty =
            DependencyProperty.Register(
                "KinectSensor",
                typeof(KinectSensor),
                typeof(KinectWindow),
                new PropertyMetadata(null));

        private readonly KinectWindowViewModel viewModel;
        private bool isRecording = false;
        private string recordingPath;
        private int frameIndex;

        /// <summary>
        /// Initializes a new instance of the KinectWindow class, which provides access to many KinectSensor settings
        /// and output visualization.
        /// </summary>
        public KinectWindow()
        {
            this.viewModel = new KinectWindowViewModel();

            // The KinectSensorManager class is a wrapper for a KinectSensor that adds
            // state logic and property change/binding/etc support, and is the data model
            // for KinectDiagnosticViewer.
            this.viewModel.KinectSensorManager = new KinectSensorManager();

            Binding sensorBinding = new Binding("KinectSensor");
            sensorBinding.Source = this;
            BindingOperations.SetBinding(this.viewModel.KinectSensorManager, KinectSensorManager.KinectSensorProperty, sensorBinding);

            // Attempt to turn on Skeleton Tracking for each Kinect Sensor
            this.viewModel.KinectSensorManager.SkeletonStreamEnabled = true;

            this.DataContext = this.viewModel;
            
            InitializeComponent();
        }

        public KinectSensor KinectSensor
        {
            get { return (KinectSensor)GetValue(KinectSensorProperty); }
            set { SetValue(KinectSensorProperty, value); }
        }

        public void StatusChanged(KinectStatus status)
        {
            this.viewModel.KinectSensorManager.KinectSensorStatus = status;
        }

        private void Swap_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            Grid colorFrom = null;
            Grid depthFrom = null;

            if (this.MainViewerHost.Children.Contains(this.ColorVis))
            {
                colorFrom = this.MainViewerHost;
                depthFrom = this.SideViewerHost;
            }
            else
            {
                colorFrom = this.SideViewerHost;
                depthFrom = this.MainViewerHost;
            }

            colorFrom.Children.Remove(this.ColorVis);
            depthFrom.Children.Remove(this.DepthVis);
            colorFrom.Children.Insert(0, this.DepthVis);
            depthFrom.Children.Insert(0, this.ColorVis);
        }

        private void StartRecording_Click(object sender, RoutedEventArgs e)
        {
            if (this.isRecording)
            {
                return;
            }

            var sensor = this.viewModel.KinectSensorManager.KinectSensor;
            if (sensor == null || sensor.Status != KinectStatus.Connected)
            {
                MessageBox.Show("Kinect sensor not available");
                return;
            }

            if (!sensor.ColorStream.IsEnabled)
            {
                sensor.ColorStream.Enable(ColorImageFormat.RgbResolution640x480Fps30);
            }

            if (!sensor.DepthStream.IsEnabled)
            {
                sensor.DepthStream.Enable(DepthImageFormat.Resolution320x240Fps30);
            }

            if (!sensor.IsRunning)
            {
                try
                {
                    sensor.Start();
                }
                catch (InvalidOperationException ex)
                {
                    MessageBox.Show(ex.Message, "Sensor start failed");
                    return;
                }
            }

            string name = this.RecordingNameBox.Text;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = "Recording";
            }

            this.recordingPath = System.IO.Path.Combine(System.IO.Path.GetFullPath("Recordings"), name + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            System.IO.Directory.CreateDirectory(this.recordingPath);

            sensor.ColorFrameReady += this.SensorColorFrameReady;
            sensor.DepthFrameReady += this.SensorDepthFrameReady;

            this.frameIndex = 0;
            this.isRecording = true;

            this.StartRecordingButton.IsEnabled = false;
            this.StopRecordingButton.IsEnabled = true;
        }

        private void StopRecording_Click(object sender, RoutedEventArgs e)
        {
            if (!this.isRecording)
            {
                return;
            }

            var sensor = this.viewModel.KinectSensorManager.KinectSensor;
            if (sensor != null)
            {
                sensor.ColorFrameReady -= this.SensorColorFrameReady;
                sensor.DepthFrameReady -= this.SensorDepthFrameReady;
            }

            this.isRecording = false;
            this.StartRecordingButton.IsEnabled = true;
            this.StopRecordingButton.IsEnabled = false;
        }

        private void SensorColorFrameReady(object sender, ColorImageFrameReadyEventArgs e)
        {
            if (!this.isRecording)
            {
                return;
            }

            using (var frame = e.OpenColorImageFrame())
            {
                if (frame == null)
                {
                    return;
                }

                byte[] pixels = new byte[frame.PixelDataLength];
                frame.CopyPixelDataTo(pixels);

                this.Dispatcher.BeginInvoke(new Action(() =>
                {
                    var bitmap = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgr32, null, pixels, frame.Width * 4);
                    bitmap.Freeze();

                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    string path = Path.Combine(this.recordingPath, $"color_{this.frameIndex:D6}.png");
                    using (var fs = new FileStream(path, FileMode.Create))
                    {
                        encoder.Save(fs);
                    }
                }), DispatcherPriority.Background);
            }
        }

        private void SensorDepthFrameReady(object sender, DepthImageFrameReadyEventArgs e)
        {
            if (!this.isRecording)
            {
                return;
            }

            using (var frame = e.OpenDepthImageFrame())
            {
                if (frame == null)
                {
                    return;
                }

                short[] depth = new short[frame.PixelDataLength];
                frame.CopyPixelDataTo(depth);

                ushort[] pixels = new ushort[frame.Width * frame.Height];
                for (int i = 0; i < depth.Length; i++)
                {
                    pixels[i] = (ushort)depth[i];
                }

                this.Dispatcher.BeginInvoke(new Action(() =>
                {
                    var bitmap = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Gray16, null, pixels, frame.Width * 2);
                    bitmap.Freeze();

                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    string path = Path.Combine(this.recordingPath, $"depth_{this.frameIndex:D6}.png");
                    using (var fs = new FileStream(path, FileMode.Create))
                    {
                        encoder.Save(fs);
                    }
                }), DispatcherPriority.Background);
            }

            this.frameIndex++;
        }
    }

    /// <summary>
    /// A ViewModel for a KinectWindow.
    /// </summary>
    public class KinectWindowViewModel : DependencyObject
    {
        public static readonly DependencyProperty KinectSensorManagerProperty =
            DependencyProperty.Register(
                "KinectSensorManager",
                typeof(KinectSensorManager),
                typeof(KinectWindowViewModel),
                new PropertyMetadata(null));

        public static readonly DependencyProperty DepthTreatmentProperty =
            DependencyProperty.Register(
                "DepthTreatment",
                typeof(KinectDepthTreatment),
                typeof(KinectWindowViewModel),
                new PropertyMetadata(KinectDepthTreatment.ClampUnreliableDepths));

        public KinectSensorManager KinectSensorManager
        {
            get { return (KinectSensorManager)GetValue(KinectSensorManagerProperty); }
            set { SetValue(KinectSensorManagerProperty, value); }
        }

        public KinectDepthTreatment DepthTreatment
        {
            get { return (KinectDepthTreatment)GetValue(DepthTreatmentProperty); }
            set { SetValue(DepthTreatmentProperty, value); }
        }
    }

    /// <summary>
    /// The Command to swap the viewer in the main panel with the viewer in the side panel.
    /// </summary>
    public class KinectWindowsViewerSwapCommand : RoutedCommand
    {  
    }
}
