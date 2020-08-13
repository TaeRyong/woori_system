using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Ab3d.Cameras;
using Ab3d.Common.Cameras;
using Ab3d.Common.Models;
using Ab3d.Utilities;
using Ab3d.Visuals;
using Ab3d.DirectX.Materials;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using SharpDX;
using Ab3d.DirectX;
using Random = System.Random;
using System.Timers;
using Ab3d.PowerToys.Samples.Objects3D;

namespace Ab3d.PowerToys.Samples.Lines3D
{
    /// <summary>
    /// Interaction logic for LinesSelector.xaml
    /// </summary>
    public partial class LinesSelector : Page
    {
        private Random _rnd = new Random();

        private float _linePositionsRange = 10;
        private List<LineSelectorData> _lineSelectorData;

        private LineSelectorData _lastSelectedLineSelector;
        private Color _savedLineColor;

        private bool _isCameraChanged;

        private Ab3d.Visuals.SphereVisual3D _closestPositionSphereVisual3D;
        private Point _lastMousePosition;

        private double _maxSelectionDistance;
        private Color4[] _positionColorsArray;

        List<Point3D> points = new List<Point3D>();
        List<SharpDX.Vector3> vectors = new List<Vector3>();
        List<SharpDX.Color4> colors = new List<SharpDX.Color4>();
        ScreenSpaceLineNode screenSpaceLineNode;
        PositionColoredLineMaterial lineMaterial;
        Color4 defC, minC, maxC;
        public LinesSelector()
        {
            InitializeComponent();
            Camera1.NearPlaneDistance = 0.1f;
            defC = new Color4(1, 1, 0, 1);
            minC = new Color4(0, 0, 1, 1);
            maxC = new Color4(1, 0, 0, 1);
            _lineSelectorData = new List<LineSelectorData>();
            CrateColoredPolyLine(20000);

            var lineSelectorData = new LineSelectorData(points, true);
            bool isVisualConnected; // This will be set to true if lineVisual3D is connected to Viewport3D (always true in our case).
            //lineSelectorData.PositionsTransform3D = Ab3d.Utilities.TransformationsHelper.GetVisual3DTotalTransform(lineVisual3D, true, out isVisualConnected);
            _lineSelectorData.Add(lineSelectorData);
            _isCameraChanged = true; // When true, the CalculateScreenSpacePositions method is called before calculating line distances
            Camera1.CameraChanged += delegate(object sender, CameraChangedRoutedEventArgs e)
            {
                _isCameraChanged = true;
                UpdateClosestLine();
            };

            this.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                _lastMousePosition = e.GetPosition(MainBorder);
                UpdateClosestLine();
            };

            // Cleanup
            this.Unloaded += delegate (object sender, RoutedEventArgs args)
            {
                if (lineMaterial != null)
                {
                    lineMaterial.Dispose();
                    lineMaterial = null;
                }

                MainDXViewportView.Dispose();
            };

            startStatusBarTimer();
        }

        private void startStatusBarTimer()
        {
            System.Timers.Timer statusTime = new System.Timers.Timer();

            statusTime.Interval = 100;

            statusTime.Elapsed += new System.Timers.ElapsedEventHandler(statusTimeElapsed);

            statusTime.Enabled = true;
        }

        void statusTimeElapsed(object sender, ElapsedEventArgs e)
        {
            if (App.Current == null)
                return;

            App.Current.Dispatcher.Invoke((Action)delegate
            {
                Console.WriteLine("check");
                Color4 preC = Color4.White;
                for (int i = 0; i < colors.Count; i++)
                {
                    Color4 c = colors[i];
                    colors[i] = GetColor(i, preC);
                    preC = colors[i];
                }

                if (lineMaterial == null)
                    return;

                lineMaterial.PositionColors = colors.ToArray();
                lineMaterial.Update();

                var sceneNode = MainDXViewportView.GetSceneNodeForWpfObject(screenSpaceLineNode.GetGeometryModel3D());
                if (sceneNode != null)
                    sceneNode.NotifySceneNodeChange(SceneNode.SceneNodeDirtyFlags.MaterialChanged);
            });
        }

        private void UpdateClosestLine()
        {
            if (_lineSelectorData == null || _lineSelectorData.Count == 0)
                return;

            if (_isCameraChanged)
            {
                // Each time camera is changed, we need to call CalculateScreenSpacePositions method.
                // This will update the 2D screen positions of the 3D lines.

                // IMPORTANT:
                // Before calling CalculateScreenSpacePositions it is highly recommended to call Refresh method on the camera.
                Camera1.Refresh();

                if (MultiThreadedCheckBox.IsChecked ?? false)
                {
                    // This code demonstrates how to use call CalculateScreenSpacePositions from multiple threads.
                    // This significantly improves performance when many 3D lines are used (thousands)
                    // but is not needed when using only a few lines (as in this demo).
                    //
                    // When calling CalculateScreenSpacePositions we need to prepare all the data
                    // from WPF properties before calling the method because those properties 
                    // are not accessible from the other thread.
                    // We need worldToViewportMatrix and
                    // the _lineSelectorData[i].Camera and _lineSelectorData[i].UsedLineThickness need to be set
                    // (in this sample they are set in the LineSelectorData constructor).

                    var worldToViewportMatrix = new Matrix3D();
                    bool isWorldToViewportMatrixValid = Camera1.GetWorldToViewportMatrix(ref worldToViewportMatrix, forceMatrixRefresh: false);

                    if (isWorldToViewportMatrixValid)
                    {
                        Parallel.For(0, _lineSelectorData.Count, 
                                     i => _lineSelectorData[i].CalculateScreenSpacePositions(ref worldToViewportMatrix, transform: null));
                    }
                }
                else
                {
                    for (var i = 0; i < _lineSelectorData.Count; i++)
                        _lineSelectorData[i].CalculateScreenSpacePositions(Camera1);
                }

                _isCameraChanged = false;
            }


            // Now we can call the GetClosestDistance method.
            // This method calculates the closest distance from the _lastMousePosition to the line that was used to create the LineSelectorData.
            // GetClosestDistance also sets the LastDistance, LastLinePositionIndex properties on the LineSelectorData.

            if (MultiThreadedCheckBox.IsChecked ?? false)
            {
                Parallel.For(0, _lineSelectorData.Count, 
                             i => _lineSelectorData[i].GetClosestDistance(_lastMousePosition));
            }
            else
            {
                for (var i = 0; i < _lineSelectorData.Count; i++)
                    _lineSelectorData[i].GetClosestDistance(_lastMousePosition);
            }


            // Get the closest line
            IEnumerable<LineSelectorData> usedLineSelectors;

            // If we limit the distance of the specified position to the line, then we can filter all the line with Where
            if (_maxSelectionDistance >= 0)
                usedLineSelectors = _lineSelectorData.Where(l => l.LastDistance <= _maxSelectionDistance).ToList();
            else
                usedLineSelectors = _lineSelectorData;


            List<LineSelectorData> orderedLineSelectors;
            if (OrderByDistanceCheckBox.IsChecked ?? false)
            {
                // Order by camera distance
                orderedLineSelectors = usedLineSelectors.OrderBy(l => l.LastDistanceFromCamera).ToList();
            }
            else
            {
                // Order by distance to the specified position
                orderedLineSelectors = usedLineSelectors.OrderBy(l => l.LastDistance).ToList();
            }

            // Get the closest LineSelectorData
            LineSelectorData closestLineSelector;
            if (orderedLineSelectors.Count > 0)
                closestLineSelector = orderedLineSelectors[0];
            else
                closestLineSelector = null;


            // It is possible to get the positions of the line segment that is closest to the mouse position
            //var closestPolyLine = (PolyLineVisual3D)closestLineSelector.LineVisual;
            //Point3D firstSegmentPosition = closestPolyLine.Positions[closestLineSelector.LastLinePositionIndex];
            //Point3D secondSegmentPosition = closestPolyLine.Positions[closestLineSelector.LastLinePositionIndex + 1];

            // To get the actual position on the line that is closest to the mouse position, use the LastClosestPositionOnLine
            //closestLineSelector.LastClosestPositionOnLine;


            // The closest position on the line is shown with a SphereVisual3D
            if (_closestPositionSphereVisual3D == null)
            {
                _closestPositionSphereVisual3D = new SphereVisual3D()
                {
                    Radius = 0.1f,
                    Material = new DiffuseMaterial(Brushes.Red)
                };

                MainViewport.Children.Add(_closestPositionSphereVisual3D);
            }

            if (closestLineSelector == null)
            {
                ClosestDistanceValue.Text = "";
                LineSegmentIndexValue.Text = "";
                _closestPositionSphereVisual3D.IsVisible = false;
            }
            else
            {
                ClosestDistanceValue.Text = string.Format("{0:0.0}", closestLineSelector.LastDistance);
                LineSegmentIndexValue.Text = closestLineSelector.LastLinePositionIndex.ToString();

                _closestPositionSphereVisual3D.CenterPosition = closestLineSelector.LastClosestPositionOnLine;
                _closestPositionSphereVisual3D.IsVisible = true;
            }


            // Show the closest line as red
            if (!ReferenceEquals(_lastSelectedLineSelector, closestLineSelector))
            {
                if (_lastSelectedLineSelector != null)
                {
                }

                if (closestLineSelector != null)
                {
                }

                _lastSelectedLineSelector = closestLineSelector;
            }
        }

        Color4 GetColor(int index, Color4 preC)
        {
            int _r = _rnd.Next(0, 100);
            if (index % 2 == 0)
            {
                if (_r < 5)
                    return maxC;
                else if (_r < 10)
                    return minC;
                else
                    return defC;
            }
            return preC;
        }

        void CrateColoredPolyLine(int count)
        {
            Vector3 preV = Vector3.Zero;
            Color4 preC = Color4.White;
            for(int i = 0; i < count; i++)
            {
                float max = 1;
                Vector3 v = Vector3.Left;
                v = preV + new Vector3(_rnd.NextFloat(max * -1f, max), 0, _rnd.NextFloat(max * -1f, max));
                preC = GetColor(i, preC);
                colors.Add(preC);
                points.Add(new Point3D((double)v.X, (double)v.Y, (double)v.Z));
                vectors.Add(v);
                preV = v;
            }

            DisposeList disposables = new DisposeList();
            float lineThickness = 10;
            bool isPolyLine = true;
            lineMaterial = new PositionColoredLineMaterial()
            {
                LineColor = Color4.White, // When PositionColors are used, then LineColor is used as a mask - each color is multiplied by LineColor - use White to preserve PositionColors
                LineThickness = lineThickness,
                PositionColors = colors.ToArray(),
                IsPolyLine = isPolyLine
            };

            // NOTE: When rendering multi-lines we need to set isLineStrip to false
            screenSpaceLineNode = new ScreenSpaceLineNode(vectors.ToArray(), isLineClosed: false, isLineStrip: isPolyLine, lineMaterial: lineMaterial);

            if (disposables != null)
            {
                disposables.Add(screenSpaceLineNode);
                disposables.Add(lineMaterial);
            }

            var sceneNodeVisual = new SceneNodeVisual3D(screenSpaceLineNode);
            MainViewport.Children.Add(sceneNodeVisual);
            Camera1.RotateTo(90, -90);
        }

        private void UpdateMaxDistanceText()
        {
            if (_maxSelectionDistance < 0)
                MaxDistanceValue.Text = "unlimited";
            else
                MaxDistanceValue.Text = string.Format("{0:0}", _maxSelectionDistance);
        }

        private void MaxDistanceSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _maxSelectionDistance = MaxDistanceSlider.Value;

            if (_maxSelectionDistance > 20)
                _maxSelectionDistance = -1;

            UpdateMaxDistanceText();
        }

        float rotateSpeed = 5;
        private void CameraRotationButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (Camera1.IsRotating)
            {
                Camera1.StopRotation();
                CameraRotationButton.Content = "Start camera rotation";
            }
            else
            {
                Camera1.StartRotation(rotateSpeed, 0);
                CameraRotationButton.Content = "Stop camera rotation";
            }
        }
    }
}
