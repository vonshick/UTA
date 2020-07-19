// Copyright © 2020 Tomasz Pućka, Piotr Hełminiak, Marcin Rochowiak, Jakub Wąsik

// This file is part of UTA Extended.

// UTA Extended is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 3 of the License, or
// (at your option) any later version.

// UTA Extended is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.

// You should have received a copy of the GNU General Public License
// along with UTA Extended.  If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using CalculationsEngine;
using DataModel.Input;
using DataModel.Results;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;
using UTA.Helpers;
using UTA.Models.Tab;

namespace UTA.ViewModels
{
    public class ChartTabViewModel : Tab
    {
        private readonly List<Dictionary<double, PointAnnotation>> _chartsDraggablePoints;
        private readonly List<TextAnnotation> _chartsDraggablePointTooltip;
        private readonly List<PartialUtility> _chartsPartialUtilities;
        private readonly List<List<PartialUtilityValues>> _chartsPointsValues;
        private readonly List<Dictionary<double, LineAnnotation>> _chartsRanges;
        private readonly OxyColor _colorPrimary = OxyColor.FromRgb(51, 115, 242); // ColorPrimary
        private readonly List<Criterion> _criteria;
        private readonly OxyColor _draggablePointTooltipFillColor = OxyColor.FromArgb(208, 252, 252, 252); // ColorInterface7
        private readonly OxyColor _draggablePointTooltipStrokeColor = OxyColor.FromRgb(170, 170, 170); // ColorBorders1
        private readonly OxyColor _gridColor = OxyColor.FromRgb(240, 240, 240); // ColorInterface5
        private readonly OxyColor _lineColor = OxyColor.FromRgb(110, 110, 110); // ColorSecondary
        private readonly OxyColor _rangesColor = OxyColor.FromRgb(210, 210, 210); // ColorInterface2
        private readonly Action _refreshCharts;
        private readonly SettingsTabViewModel _settings;
        private readonly Solver _solver;

        public ChartTabViewModel(Solver solver, List<PartialUtility> chartsPartialUtilities, SettingsTabViewModel settingsTabViewModel,
            Action refreshCharts)
        {
            _solver = solver;
            _chartsPartialUtilities = chartsPartialUtilities;
            _criteria = _chartsPartialUtilities.Select(utility => utility.Criterion).ToList();
            _chartsPointsValues = _chartsPartialUtilities.Select(utility => utility.PointsValues).ToList();
            _settings = settingsTabViewModel;
            _refreshCharts = refreshCharts;
            Name = ManagesSinglePUFunction ? $"{_criteria[0].Name} - Utility" : "Partial Utility Functions";
            Title = ManagesSinglePUFunction ? $"{_criteria[0].Name} - Partial Utility Function" : "Partial Utility Functions";
            _chartsRanges = new List<Dictionary<double, LineAnnotation>>();
            _chartsDraggablePoints = new List<Dictionary<double, PointAnnotation>>();
            _chartsDraggablePointTooltip = new List<TextAnnotation>();

            const double verticalAxisExtraSpace = 0.0001;
            PlotModels = _criteria.Select(criterion =>
            {
                var horizontalAxisExtraSpace = (criterion.MaxValue - criterion.MinValue) * 0.002;
                return new ViewResolvingPlotModel
                {
                    Title = ManagesSinglePUFunction ? null : criterion.Name,
                    TitleFontSize = AreChartsDense ? 12 : 16,
                    TitleFontWeight = 400,
                    Series =
                    {
                        new LineSeries
                        {
                            Color = _lineColor,
                            StrokeThickness = 3
                        }
                    },
                    DefaultFont = "Segoe UI",
                    DefaultFontSize = AreChartsDense ? 11 : 14,
                    Padding = new OxyThickness(0, 0, 0, 0),
                    PlotAreaBackground = OxyColors.White,
                    Axes =
                    {
                        new LinearAxis
                        {
                            Position = AxisPosition.Left,
                            Title = "Utility",
                            FontSize = AreChartsDense ? 12 : 16,
                            MajorGridlineStyle = LineStyle.Solid,
                            MajorGridlineColor = _gridColor,
                            AbsoluteMinimum = 0 - verticalAxisExtraSpace,
                            AbsoluteMaximum = 1 + verticalAxisExtraSpace,
                            Minimum = 0 - verticalAxisExtraSpace,
                            Maximum = 1 + verticalAxisExtraSpace,
                            MajorTickSize = 8,
                            IntervalLength = AreChartsDense ? 20 : 30,
                            AxisTitleDistance = AreChartsDense ? 10 : 12
                        },
                        new LinearAxis
                        {
                            Position = AxisPosition.Bottom,
                            Title = "Criterion Value",
                            FontSize = AreChartsDense ? 12 : 16,
                            MajorGridlineStyle = LineStyle.Solid,
                            MajorGridlineColor = _gridColor,
                            AbsoluteMinimum = criterion.MinValue - horizontalAxisExtraSpace,
                            AbsoluteMaximum = criterion.MaxValue + horizontalAxisExtraSpace,
                            Minimum = criterion.MinValue - horizontalAxisExtraSpace,
                            Maximum = criterion.MaxValue + horizontalAxisExtraSpace,
                            MajorTickSize = 8,
                            AxisTitleDistance = 4
                        }
                    }
                };
            }).ToList();

            GenerateChartData();
        }


        public string Title { get; }
        public List<ViewResolvingPlotModel> PlotModels { get; }
        public bool AreChartsDense => _chartsPartialUtilities.Count >= 10;
        public bool ManagesSinglePUFunction => _chartsPartialUtilities.Count == 1;
        public int NumberOfColumns => (int) Math.Sqrt(PlotModels.Count);

        public int NumberOfRows
        {
            get
            {
                var rows = (int) Math.Round(Math.Sqrt(PlotModels.Count));
                return rows * NumberOfColumns < PlotModels.Count ? rows + 1 : rows;
            }
        }


        public void GenerateChartData()
        {
            _chartsDraggablePointTooltip.Clear();
            _chartsDraggablePoints.Clear();
            _chartsRanges.Clear();
            for (var i = 0; i < PlotModels.Count; i++)
            {
                var chartIndex = i;

                void ChartAnnotationOnMouseDown(object sender, OxyMouseDownEventArgs e)
                {
                    AnnotationOnMouseDown(chartIndex, sender, e);
                }

                void ChartAnnotationOnMouseMove(object sender, OxyMouseEventArgs e)
                {
                    AnnotationOnMouseMove(chartIndex, sender, e);
                }

                void ChartAnnotationOnMouseUp(object sender, OxyMouseEventArgs e)
                {
                    AnnotationOnMouseUp(chartIndex, sender, e);
                }

                var plotModel = PlotModels[i];
                var linePoints = GetPlotLinePoints(plotModel);
                plotModel.Annotations.Clear();
                linePoints.Clear();
                _chartsRanges.Add(new Dictionary<double, LineAnnotation>());
                _chartsDraggablePoints.Add(new Dictionary<double, PointAnnotation>());

                foreach (var pointValues in _chartsPointsValues[i])
                {
                    var point = new DataPoint(pointValues.X, pointValues.Y);
                    linePoints.Add(point);

                    var range = new LineAnnotation
                    {
                        Type = LineAnnotationType.Vertical,
                        X = pointValues.X,
                        MinimumY = pointValues.MinValue,
                        MaximumY = pointValues.MaxValue,
                        StrokeThickness = 8,
                        Color = _rangesColor,
                        LineStyle = LineStyle.Solid
                    };
                    _chartsRanges[i].Add(pointValues.X, range);
                    range.MouseDown += ChartAnnotationOnMouseDown;
                    range.MouseMove += ChartAnnotationOnMouseMove;
                    range.MouseUp += ChartAnnotationOnMouseUp;
                    plotModel.Annotations.Add(range);

                    var draggablePoint = new PointAnnotation
                    {
                        X = pointValues.X,
                        Y = pointValues.Y,
                        Size = 8,
                        Fill = _colorPrimary
                    };
                    _chartsDraggablePoints[i].Add(pointValues.X, draggablePoint);
                    draggablePoint.MouseDown += ChartAnnotationOnMouseDown;
                    draggablePoint.MouseMove += ChartAnnotationOnMouseMove;
                    draggablePoint.MouseUp += ChartAnnotationOnMouseUp;
                    plotModel.Annotations.Add(draggablePoint);
                }

                var draggablePointTooltip = new TextAnnotation
                {
                    Background = _draggablePointTooltipFillColor,
                    Stroke = _draggablePointTooltipStrokeColor,
                    StrokeThickness = 1,
                    Padding = new OxyThickness(8, 2, 8, 2)
                };
                _chartsDraggablePointTooltip.Add(draggablePointTooltip);

                plotModel.Annotations.Add(draggablePointTooltip);
                plotModel.InvalidatePlot(false);
            }
        }

        private void AnnotationOnMouseDown(int chartIndex, object sender, OxyMouseDownEventArgs e)
        {
            if (e.ChangedButton != OxyMouseButton.Left) return;
            Mouse.OverrideCursor = Cursors.Hand;
            SetLineAndPointAnnotation(sender, chartIndex, out var lineAnnotation, out var pointAnnotation);
            pointAnnotation.Fill = OxyColor.FromRgb(97, 149, 250);
            if (sender is LineAnnotation)
            {
                var cursorCoords = lineAnnotation.InverseTransform(e.Position);
                if (cursorCoords.Y >= lineAnnotation.MinimumY && cursorCoords.Y <= lineAnnotation.MaximumY)
                    pointAnnotation.Y = cursorCoords.Y;
                else if (cursorCoords.Y > lineAnnotation.MaximumY) pointAnnotation.Y = lineAnnotation.MaximumY;
                else pointAnnotation.Y = lineAnnotation.MinimumY;
                // initializing new datapoint, because it doesn't have a setter
                var linePoints = GetPlotLinePoints(PlotModels[chartIndex]);
                linePoints[linePoints.FindIndex(point => point.X == pointAnnotation.X)] =
                    new DataPoint(pointAnnotation.X, pointAnnotation.Y);
            }

            GenerateDraggablePointTooltip(chartIndex, pointAnnotation);
            PlotEventHandler(chartIndex, e);
        }

        private void AnnotationOnMouseMove(int chartIndex, object sender, OxyMouseEventArgs e)
        {
            SetLineAndPointAnnotation(sender, chartIndex, out var lineAnnotation, out var pointAnnotation);
            var cursorCoords = pointAnnotation.InverseTransform(e.Position);
            if (cursorCoords.Y >= lineAnnotation.MinimumY && cursorCoords.Y <= lineAnnotation.MaximumY)
                pointAnnotation.Y = cursorCoords.Y;
            else if (cursorCoords.Y > lineAnnotation.MaximumY) pointAnnotation.Y = lineAnnotation.MaximumY;
            else pointAnnotation.Y = lineAnnotation.MinimumY;
            var linePoints = GetPlotLinePoints(PlotModels[chartIndex]);
            linePoints[linePoints.FindIndex(point => point.X == pointAnnotation.X)] = new DataPoint(pointAnnotation.X, pointAnnotation.Y);
            GenerateDraggablePointTooltip(chartIndex, pointAnnotation);
            PlotEventHandler(chartIndex, e);
        }

        private void AnnotationOnMouseUp(int chartIndex, object sender, OxyMouseEventArgs e)
        {
            SetLineAndPointAnnotation(sender, chartIndex, out _, out var pointAnnotation);
            Mouse.OverrideCursor = null;
            pointAnnotation.Fill = _colorPrimary;
            _chartsDraggablePointTooltip[chartIndex].TextPosition = DataPoint.Undefined;
            PlotEventHandler(chartIndex, e);

            _solver.ChangeValue(pointAnnotation.Y, _chartsPartialUtilities[chartIndex],
                _chartsPartialUtilities[chartIndex].PointsValues.FindIndex(point => point.X == pointAnnotation.X));
            _refreshCharts();
        }

        private void GenerateDraggablePointTooltip(int chartIndex, PointAnnotation pointAnnotation)
        {
            var draggablePointTooltip = _chartsDraggablePointTooltip[chartIndex];
            draggablePointTooltip.Text = Math.Round(pointAnnotation.Y, _settings.PlotsPartialUtilityDecimalPlaces)
                .ToString($"F{_settings.PlotsPartialUtilityDecimalPlaces}");
            draggablePointTooltip.TextPosition = new DataPoint(pointAnnotation.X, pointAnnotation.Y);
            double xOffset = 4 * _settings.PlotsPartialUtilityDecimalPlaces + 24;
            draggablePointTooltip.Offset =
                new ScreenVector(pointAnnotation.X == _criteria[chartIndex].MinValue ? xOffset : -1 * xOffset, 9);
        }

        private void SetLineAndPointAnnotation(object sender, int chartIndex, out LineAnnotation outLineAnnotation,
            out PointAnnotation outPointAnnotation)
        {
            if (sender is PointAnnotation)
            {
                outPointAnnotation = (PointAnnotation) sender;
                outLineAnnotation = _chartsRanges[chartIndex][outPointAnnotation.X];
            }
            else
            {
                outLineAnnotation = (LineAnnotation) sender;
                outPointAnnotation = _chartsDraggablePoints[chartIndex][outLineAnnotation.X];
            }
        }

        private List<DataPoint> GetPlotLinePoints(PlotModel plotModel)
        {
            return ((LineSeries) plotModel.Series[0]).Points;
        }

        private void PlotEventHandler(int chartIndex, OxyInputEventArgs e)
        {
            PlotModels[chartIndex].InvalidatePlot(false);
            e.Handled = true;
        }
    }
}