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
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using DataModel.Input;
using DataModel.Results;

namespace ImportModule
{
    public class XMCDALoader : DataLoader
    {
        public bool PreserveKendallCoefficient = false;
        private string currentlyProcessedAlternativeId;
        private string currentlyProcessedCriterionId;
        public string CurrentlyProcessedFile;
        private string xmcdaDirectory;

        private void validateInputFilesSet()
        {
            ValidateFilePath(Path.Combine(xmcdaDirectory, "criteria.xml"));
            ValidateFilePath(Path.Combine(xmcdaDirectory, "alternatives.xml"));
            ValidateFilePath(Path.Combine(xmcdaDirectory, "criteria_scales.xml"));
            ValidateFilePath(Path.Combine(xmcdaDirectory, "performance_table.xml"));
        }

        private bool checkIfResultsAvailable()
        {
            if (!File.Exists(Path.Combine(xmcdaDirectory, "alternatives_ranks.xml")) ||
                !File.Exists(Path.Combine(xmcdaDirectory, "value_functions.xml")) ||
                !File.Exists(Path.Combine(xmcdaDirectory, "criteria_segments.xml")))
                return false;

            return true;
        }

        private void checkIfIdProvided(XmlAttributeCollection attributesCollection, string elementType)
        {
            if (attributesCollection["id"] == null)
                throw new ImproperFileStructureException("Attribute 'id' must be provided for each  " + elementType + ".");
        }

        private string checkIfAlternativeNameProvided(XmlAttributeCollection attributesCollection)
        {
            if (attributesCollection["name"] != null)
                return checkAlternativesNamesUniqueness(attributesCollection["name"].Value);
            return checkAlternativesNamesUniqueness(attributesCollection["id"].Value);
        }

        private string checkIfCriterionNameProvided(XmlAttributeCollection attributesCollection)
        {
            if (attributesCollection["name"] != null)
                return checkCriteriaNamesUniqueness(attributesCollection["name"].Value);
            return checkCriteriaNamesUniqueness(attributesCollection["id"].Value);
        }

        private XmlDocument loadFile(string fileName)
        {
            CurrentlyProcessedFile = Path.Combine(xmcdaDirectory, fileName);
            ValidateFilePath(CurrentlyProcessedFile);

            var xmlDocument = new XmlDocument();
            xmlDocument.Load(CurrentlyProcessedFile);

            return xmlDocument;
        }

        private void LoadCriteria()
        {
            var xmlDocument = loadFile("criteria.xml");

            // this file contains only one main block - <criteria>
            foreach (XmlNode xmlNode in xmlDocument.DocumentElement.ChildNodes[0])
            {
                checkIfIdProvided(xmlNode.Attributes, "criterion");
                var criterion = new Criterion
                {
                    ID = checkCriteriaIdsUniqueness(xmlNode.Attributes["id"].Value),
                    LinearSegments = 1
                };

                criterion.Name = checkIfCriterionNameProvided(xmlNode.Attributes);

                criterionList.Add(criterion);
            }
        }

        private void LoadCriteriaScales()
        {
            var xmlDocument = loadFile("criteria_scales.xml");

            // this file contains only one main block - <criteriaScales>
            foreach (XmlNode xmlNode in xmlDocument.DocumentElement.ChildNodes[0])
            {
                var criterionID = xmlNode.ChildNodes[0].InnerText;
                var criterionDirection = xmlNode.ChildNodes[1].FirstChild.FirstChild.FirstChild.InnerText;

                var index = criterionList.FindIndex(criterion => criterion.ID == criterionID);
                criterionList[index].CriterionDirection = criterionDirection == "max" ? "Gain" : "Cost";
            }
        }

        private void LoadAlternatives()
        {
            var xmlDocument = loadFile("alternatives.xml");

            // this file contains only one main block - <criteria>
            foreach (XmlNode xmlNode in xmlDocument.DocumentElement.ChildNodes[0])
            {
                checkIfIdProvided(xmlNode.Attributes, "alternative");
                var alternative = new Alternative
                {
                    ID = checkAlternativesIdsUniqueness(xmlNode.Attributes["id"].Value),
                    CriteriaValuesList = new ObservableCollection<CriterionValue>()
                };

                alternative.Name = checkIfAlternativeNameProvided(xmlNode.Attributes);

                alternativeList.Add(alternative);
            }
        }

        private bool compareAlternativeIds(Alternative alternative)
        {
            return alternative.ID.Equals(currentlyProcessedAlternativeId);
        }

        private void LoadPerformanceTable()
        {
            var xmlDocument = loadFile("performance_table.xml");
            var nodeCounter = 1;

            if (xmlDocument.DocumentElement.ChildNodes[0].ChildNodes.Count != alternativeList.Count)
                throw new ImproperFileStructureException("There are provided " +
                                                         xmlDocument.DocumentElement.ChildNodes[0].ChildNodes.Count +
                                                         " alternative performances and required are " + alternativeList.Count + ".");

            // this file contains only one main block - <criteriaScales>
            foreach (XmlNode xmlNode in xmlDocument.DocumentElement.ChildNodes[0])
            {
                // one of children nodes is the name node  
                if (xmlNode.ChildNodes.Count - 1 != criterionList.Count)
                    throw new ImproperFileStructureException("There are provided " + (xmlNode.ChildNodes.Count - 1) +
                                                             " criteria values and required are " + criterionList.Count + ": node " +
                                                             nodeCounter + " of alternativePerformances.");

                foreach (XmlNode performance in xmlNode.ChildNodes)
                    // first node containts alternative ID
                    if (performance.Name == "alternativeID")
                    {
                        currentlyProcessedAlternativeId = performance.InnerText;
                    }
                    else
                    {
                        var criterionID = performance.ChildNodes[0].InnerText;
                        var matchingCriterion = criterionList.Find(criterion => criterion.ID == criterionID);

                        if (matchingCriterion == null)
                            throw new ImproperFileStructureException("Error while processing alternative " +
                                                                     currentlyProcessedAlternativeId + ": criterion with ID " +
                                                                     criterionID + " does not exist.");

                        var value = performance.ChildNodes[1].FirstChild.InnerText;
                        checkIfValueIsValid(value, criterionID, currentlyProcessedAlternativeId);

                        var alternativeIndex = alternativeList.FindIndex(compareAlternativeIds);

                        alternativeList[alternativeIndex].CriteriaValuesList.Add(new CriterionValue(matchingCriterion.Name,
                            double.Parse(value, CultureInfo.InvariantCulture)));
                    }

                nodeCounter++;
            }
        }

        private void LoadAlternativesRanks()
        {
            CurrentlyProcessedFile = Path.Combine(xmcdaDirectory, "alternatives_ranks.xml");

            if (!File.Exists(CurrentlyProcessedFile))
                return;

            var xmlDocument = new XmlDocument();
            xmlDocument.Load(CurrentlyProcessedFile);

            foreach (XmlNode xmlNode in xmlDocument.DocumentElement.ChildNodes[0])
            foreach (XmlNode alternativeResult in xmlNode.ChildNodes)
                // first node containts alternative ID
                if (alternativeResult.Name == "alternativeID")
                {
                    currentlyProcessedAlternativeId = alternativeResult.InnerText;
                }
                else
                {
                    var rank = int.Parse(alternativeResult.ChildNodes[0].InnerText);
                    var alternativeIndex = alternativeList.FindIndex(compareAlternativeIds);
                    alternativeList[alternativeIndex].ReferenceRank = rank - 1;
                }
        }

        private bool compareCriterionIds(Criterion criterion)
        {
            return criterion.ID.Equals(currentlyProcessedCriterionId);
        }

        private void LoadCriteriaSegments()
        {
            CurrentlyProcessedFile = Path.Combine(xmcdaDirectory, "criteria_segments.xml");

            if (!File.Exists(CurrentlyProcessedFile))
                return;

            var xmlDocument = new XmlDocument();
            xmlDocument.Load(CurrentlyProcessedFile);

            foreach (XmlNode xmlNode in xmlDocument.DocumentElement.ChildNodes[0])
            foreach (XmlNode criterionSegments in xmlNode.ChildNodes)
                // first node containts alternative ID
                if (criterionSegments.Name == "criterionID")
                {
                    currentlyProcessedCriterionId = criterionSegments.InnerText;
                }
                else
                {
                    var numberOfSegments = int.Parse(criterionSegments.ChildNodes[0].InnerText);
                    var criterionIndex = criterionList.FindIndex(compareCriterionIds);
                    criterionList[criterionIndex].LinearSegments = numberOfSegments;
                }
        }

        private void CheckFunctionMonotonicity(PartialUtility partialUtility)
        {
            var criterionId = partialUtility.Criterion.ID;
            var criterionDirection = partialUtility.Criterion.CriterionDirection;
            for (var i = 1; i < partialUtility.PointsValues.Count; i++)
            {
                if (criterionDirection.Equals("Gain"))
                {
                    if (partialUtility.PointsValues[i].Y < partialUtility.PointsValues[i - 1].Y)
                        throw new ImproperFileStructureException("criterion " + criterionId + " data set is not valid for UTA - utility function has to be increasing for criterion direction '" + criterionDirection + "'.");
                }
                else if (criterionDirection.Equals("Cost"))
                {
                    if (partialUtility.PointsValues[i].Y > partialUtility.PointsValues[i - 1].Y)
                        throw new ImproperFileStructureException("criterion " + criterionId + " data set is not valid for UTA - utility function has to be descending  for criterion direction '" + criterionDirection + "'.");

                }
            }
        }

        private double CheckEdgePoints(PartialUtility partialUtility)
        {
            var criterionId = partialUtility.Criterion.ID;
            var criterionMax = partialUtility.Criterion.MaxValue;
            var criterionMin = partialUtility.Criterion.MinValue;

            var lowestAbscissa = partialUtility.PointsValues.Min(o => o.X);
            var highestAbscissa = partialUtility.PointsValues.Max(o => o.X);

            var lowestUtility = partialUtility.PointsValues.Min(o => o.Y);
            var highestUtility = partialUtility.PointsValues.Max(o => o.Y);

            if (lowestAbscissa != criterionMin)
                throw new ImproperFileStructureException("criterion " + criterionId +
                                                         " data set is not valid for UTA - lowest abscissa equals " + lowestAbscissa.ToString("G", CultureInfo.InvariantCulture) +
                                                         " and it should be the same like the lowest value for this criterion in performance_table.xml: " +
                                                         criterionMin.ToString("G", CultureInfo.InvariantCulture) + ".");

            if (highestAbscissa != criterionMax)
                throw new ImproperFileStructureException("criterion " + criterionId +
                                                         " data set is not valid for UTA - highest abscissa equals " + highestAbscissa.ToString("G", CultureInfo.InvariantCulture) +
                                                         " and it should be the same like the highest value for this criterion performance_table.xml: " +
                                                         criterionMax.ToString("G", CultureInfo.InvariantCulture) + ".");


            if (lowestUtility != 0)
                throw new ImproperFileStructureException("criterion " + criterionId +
                                                         " data set is not valid for UTA - lowest utility value of each function should be equal to 0 and it is " +
                                                         lowestUtility.ToString("G", CultureInfo.InvariantCulture) + ".");


            return highestUtility;
        }


        private void ValidateUtilityFunctions()
        {
            double sumOfHighestUtilities = 0;

            foreach (var utilityFunction in results.PartialUtilityFunctions)
            {
                utilityFunction.PointsValues.Sort((first, second) => first.X.CompareTo(second.X));
                sumOfHighestUtilities += CheckEdgePoints(utilityFunction);
                CheckFunctionMonotonicity(utilityFunction);

                utilityFunction.PointsValues.Sort((first, second) => first.Y.CompareTo(second.Y));
                if(utilityFunction.Criterion.CriterionDirection.Equals("Cost"))
                    utilityFunction.PointsValues.Sort((first, second) => second.X.CompareTo(first.X));
                else
                    utilityFunction.PointsValues.Sort((first, second) => first.X.CompareTo(second.X));
            }

            sumOfHighestUtilities = Math.Round(sumOfHighestUtilities, 2);
            if(sumOfHighestUtilities != 1)
                throw new ImproperFileStructureException(" data set is not valid for UTA - sum of highest utilities for each criterion should be equal to 1 but it equals " + sumOfHighestUtilities.ToString("G", CultureInfo.InvariantCulture));
        }

        private void LoadValueFunctions()
        {
            CurrentlyProcessedFile = Path.Combine(xmcdaDirectory, "value_functions.xml");

            if (!File.Exists(CurrentlyProcessedFile))
                return;

            var xmlDocument = new XmlDocument();
            xmlDocument.Load(CurrentlyProcessedFile);

            foreach (XmlNode xmlNode in xmlDocument.DocumentElement.ChildNodes[0])
            {
                var criterionID = "";
                var argumentsValues = new List<PartialUtilityValues>();
                foreach (XmlNode criterionFunction in xmlNode.ChildNodes)
                    if (criterionFunction.Name == "criterionID")
                    {
                        criterionID = criterionFunction.InnerText;
                    }
                    else
                    {
                        var criteria_segments = criterionFunction.FirstChild.ChildNodes.Count - 1;
                        foreach (XmlNode point in criterionFunction.FirstChild.ChildNodes)
                        {
                            var argument = double.PositiveInfinity;
                            var value = double.PositiveInfinity;

                            foreach (XmlNode coordinate in point.ChildNodes)
                                if (coordinate.Name == "abscissa")
                                {
                                    argument = double.Parse(coordinate.FirstChild.InnerText, CultureInfo.InvariantCulture);
                                }
                                else
                                {
                                    value = double.Parse(coordinate.FirstChild.InnerText, CultureInfo.InvariantCulture);
                                    if (argument == double.PositiveInfinity || value == double.PositiveInfinity)
                                    {
                                        Trace.WriteLine("Format of the file is not valid");
                                        return;
                                    }

                                    argumentsValues.Add(new PartialUtilityValues(argument, value));
                                }
                        }

                        var matchingCriterion = criterionList.Find(criterion => criterion.ID == criterionID);
                        matchingCriterion.LinearSegments = criteria_segments == 0 ? 1 : criteria_segments;
                        results.PartialUtilityFunctions.Add(new PartialUtility(matchingCriterion, argumentsValues));
                    }
            }

            ValidateUtilityFunctions();
        }

        private void LoadMethodParameters()
        {
            CurrentlyProcessedFile = Path.Combine(xmcdaDirectory, "method_parameters.xml");

            if (!File.Exists(CurrentlyProcessedFile))
                return;

            var xmlDocument = new XmlDocument();
            xmlDocument.Load(CurrentlyProcessedFile);

            bool preserveKendallCoefficient;

            if(xmlDocument.GetElementsByTagName("boolean")[0] != null)
                if (bool.TryParse(xmlDocument.GetElementsByTagName("boolean")[0].InnerText, out preserveKendallCoefficient))
                    PreserveKendallCoefficient = preserveKendallCoefficient;
                else
                    PreserveKendallCoefficient = false;
        }

        protected override void ProcessFile(string xmcdaDirectory)
        {
            this.xmcdaDirectory = xmcdaDirectory;

            validateInputFilesSet();

            LoadCriteria();
            LoadCriteriaScales();
            LoadAlternatives();
            LoadPerformanceTable();
            setMinAndMaxCriterionValues();

            LoadCriteriaSegments();
            LoadAlternativesRanks();
            LoadValueFunctions();
            LoadMethodParameters();

            CurrentlyProcessedFile = "";
        }
    }
}