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
using System.Linq;
using DataModel.Input;
using DataModel.Results;
using DataModel.Structs;

namespace CalculationsEngine
{
    public class Solver
    {
        private readonly List<Criterion> criteriaList;
        private readonly List<int> equals;
        private double[] arrayOfValues;
        private List<Alternative> arternativesList;
        private int[] basicVariables;
        private int criterionFieldsCount;
        private List<Alternative> otherAlternatives;
        private double[,] otherAlternativesMatrix;
        private double[,] restrictionsMatrix;
        private Dictionary<double, double> solution;
        private double[,] transientMatrix;
        private List<KeyValuePair<Alternative, int>> variantsList;
        private double[] vectorCj;

        public Solver()
        {
        }

        public Solver(List<List<Alternative>> referenceRankingList, List<Criterion> criteriaList, List<Alternative> otherAlternatives,
            Results results,
            bool preserveKendallCoefficient = true, double deltaThreshold = 0.0000001)
        {
            DeltaThreshold = deltaThreshold;
            PreserveKendallCoefficient = preserveKendallCoefficient;
            NumberOfIteration = 250;
            this.criteriaList = criteriaList;
            Result = results;
            variantsList = new List<KeyValuePair<Alternative, int>>();
            arternativesList = new List<Alternative>();
            for (var rank = 0; rank < referenceRankingList.Count; rank++)
                foreach (var alternative in referenceRankingList[rank])
                {
                    variantsList.Add(new KeyValuePair<Alternative, int>(alternative, rank));
                    arternativesList.Add(alternative);
                }

            var cfc = 0;
            foreach (var criterion in criteriaList) cfc += criterion.LinearSegments;
            criterionFieldsCount = cfc;
            equals = new List<int>();
            for (var i = 0; i < arternativesList.Count - 1; i++)
                if (variantsList[i].Value == variantsList[i + 1].Value)
                    equals.Add(i);
            equals.Add(variantsList.Count - 1);
            this.otherAlternatives = new List<Alternative>();
            this.otherAlternatives = otherAlternatives;
            otherAlternativesMatrix = CreateMatrix(otherAlternatives);
        }

        public Results Result { get; set; }
        public int NumberOfIteration { get; set; }
        public double DeltaThreshold { get; set; }
        public double EpsilonThreshold { get; set; }
        public bool PreserveKendallCoefficient { get; set; }

        public void Calculate()
        {
            var heightOfPrimaryMatrix = variantsList[0].Key.CriteriaValuesList.Count;
            var height = variantsList.Count;
            var width = WidthOfSimplexMatrix(variantsList[0].Key);
            List<PartialUtility> partialUtilityList;

            transientMatrix = CreateMatrix(arternativesList);
            var createdSimplexMatrix = CreateSimplexMatrix(height, width, transientMatrix);

            var simplex =
                new Simplex(createdSimplexMatrix, basicVariables, vectorCj);
            simplex.Run(NumberOfIteration);
            solution = simplex.solution;
            (partialUtilityList, arrayOfValues) = MakePartialUtilityFunction(solution);
            var finalReferenceList = CreateRanking(arrayOfValues, transientMatrix, arternativesList);
            var finalReferenceRanking = new FinalRanking(finalReferenceList);
            var recalculate = false;
            var widthWithoutSlack = width - height - equals.Count;
            for (var i = criterionFieldsCount; i < width; i++)
                if (solution.ContainsKey(i))
                    recalculate = true;

            if (recalculate)
            {
                var recalculatedMatrix = RecreateMatrix(finalReferenceRanking);
                restrictionsMatrix = CalculateRestrictions(recalculatedMatrix, finalReferenceRanking);
            }
            else
            {
                restrictionsMatrix = CalculateRestrictions(transientMatrix, finalReferenceRanking);
            }

            var tau = CalculateKendallCoefficient(finalReferenceRanking);
            var restOfReferenceList = CreateRanking(arrayOfValues, otherAlternativesMatrix, otherAlternatives);
            var allFinalRankingEntry = finalReferenceList.Concat(restOfReferenceList).ToList();
            allFinalRankingEntry = allFinalRankingEntry.OrderByDescending(o => o.Utility).ToList();
            for (var i = 0; i < allFinalRankingEntry.Count; i++) allFinalRankingEntry[i].Position = i + 1;

            Result.PartialUtilityFunctions = partialUtilityList;
            UpdateMinMax();
            Result.FinalRanking.FinalRankingCollection = new ObservableCollection<FinalRankingEntry>(allFinalRankingEntry);
            Result.KendallCoefficient = tau;
        }

        public void LoadState(List<PartialUtility> partialUtilityList, List<List<Alternative>> referenceRankingList,
            List<Alternative> notRankedAlternatives, Results results, bool preserveKendallCoefficient = true)
        {
            Result = results;
            variantsList = new List<KeyValuePair<Alternative, int>>();
            arternativesList = new List<Alternative>();
            for (var rank = 0; rank < referenceRankingList.Count; rank++)
                foreach (var alternative in referenceRankingList[rank])
                {
                    variantsList.Add(new KeyValuePair<Alternative, int>(alternative, rank));
                    arternativesList.Add(alternative);
                }

            otherAlternatives = notRankedAlternatives;
            otherAlternativesMatrix = CreateMatrix(otherAlternatives);

            transientMatrix = CreateMatrix(arternativesList);

            var cfc = 0;
            foreach (var partialUtility in partialUtilityList) cfc += partialUtility.Criterion.LinearSegments;
            criterionFieldsCount = cfc;

            arrayOfValues = new double[criterionFieldsCount];
            var count = 0;
            for (var numOfCriterion = 0; numOfCriterion < partialUtilityList.Count; numOfCriterion++)
            {
                var sum = 0.0;
                for (var i = 1; i < partialUtilityList[numOfCriterion].PointsValues.Count; i++)
                {
                    var newValue = partialUtilityList[numOfCriterion].PointsValues[i].Y - sum;
                    arrayOfValues[count] = newValue;
                    sum += newValue;
                    count++;
                }
            }

            PreserveKendallCoefficient = preserveKendallCoefficient;
            var finalReferenceList = CreateRanking(arrayOfValues, transientMatrix, arternativesList);
            var finalReferenceRanking = new FinalRanking(finalReferenceList);
            var recalculatedMatrix = RecreateMatrix(finalReferenceRanking);
            restrictionsMatrix = CalculateRestrictions(recalculatedMatrix, finalReferenceRanking);

            var tau = CalculateKendallCoefficient(finalReferenceRanking);
            var restOfReferenceList = CreateRanking(arrayOfValues, otherAlternativesMatrix, otherAlternatives);
            var allFinalRankingEntry = finalReferenceList.Concat(restOfReferenceList).ToList();
            allFinalRankingEntry = allFinalRankingEntry.OrderByDescending(o => o.Utility).ToList();
            for (var i = 0; i < allFinalRankingEntry.Count; i++) allFinalRankingEntry[i].Position = i + 1;

            Result.PartialUtilityFunctions = partialUtilityList;
            UpdateMinMax();
            Result.FinalRanking.FinalRankingCollection = new ObservableCollection<FinalRankingEntry>(allFinalRankingEntry);
            Result.KendallCoefficient = tau;
        }

        private void UpdateMinMax()
        {
            double[] minArray;
            double[] maxArray;
            var count = 0;
            (minArray, maxArray) = CalculateRangeOfValues(restrictionsMatrix, arrayOfValues);
            count = 0;
            for (var numOfCriterion = 0; numOfCriterion < Result.PartialUtilityFunctions.Count; numOfCriterion++)
            {
                Result.PartialUtilityFunctions[numOfCriterion].PointsValues[0].MinValue = 0;
                Result.PartialUtilityFunctions[numOfCriterion].PointsValues[0].MaxValue = 0;

                for (var i = 1; i < Result.PartialUtilityFunctions[numOfCriterion].PointsValues.Count; i++)
                {
                    Result.PartialUtilityFunctions[numOfCriterion].PointsValues[i].MinValue = minArray[count];
                    Result.PartialUtilityFunctions[numOfCriterion].PointsValues[i].MaxValue = maxArray[count];
                    count++;
                }
            }
        }

        public void UpdatePreserveKendallCoefficient(bool preserveKendallCoefficient)
        {
            var referenceAlternatives = new FinalRanking();
            foreach (var finalAlternative in Result.FinalRanking.FinalRankingCollection)
            foreach (var partialAlternative in arternativesList)
                if (finalAlternative.Alternative.Name == partialAlternative.Name)
                    referenceAlternatives.FinalRankingCollection.Add(finalAlternative);
            var recalculatedMatrix = RecreateMatrix(referenceAlternatives);
            restrictionsMatrix = CalculateRestrictions(recalculatedMatrix, referenceAlternatives);
            PreserveKendallCoefficient = preserveKendallCoefficient;
            UpdateMinMax();

            var finalReferenceList = CreateRanking(arrayOfValues, transientMatrix, arternativesList);
            var finalReferenceRanking = new FinalRanking(finalReferenceList);
            var tau = CalculateKendallCoefficient(finalReferenceRanking);
            var restOfReferenceList = CreateRanking(arrayOfValues, otherAlternativesMatrix, otherAlternatives);
            var allFinalRankingEntry = finalReferenceList.Concat(restOfReferenceList).ToList();
            allFinalRankingEntry = allFinalRankingEntry.OrderByDescending(o => o.Utility).ToList();
            for (var i = 0; i < allFinalRankingEntry.Count; i++) allFinalRankingEntry[i].Position = i + 1;
            Result.FinalRanking.FinalRankingCollection = new ObservableCollection<FinalRankingEntry>(allFinalRankingEntry);
            Result.KendallCoefficient = tau;
        }

        public void ChangeValue(double value, PartialUtility partialUtility, int indexOfPointValue)
        {
            if (value < partialUtility.PointsValues[indexOfPointValue].MinValue ||
                value > partialUtility.PointsValues[indexOfPointValue].MaxValue)
                throw new ArgumentException("Value not in range", "PointsValues.Y");
            var count = 0;
            var criterionIndex = -1;
            for (var numOfCriterion = 0; numOfCriterion < Result.PartialUtilityFunctions.Count; numOfCriterion++)
                if (Result.PartialUtilityFunctions[numOfCriterion].Criterion.Name == partialUtility.Criterion.Name)
                {
                    criterionIndex = numOfCriterion;
                    break;
                }
                else
                {
                    count += Result.PartialUtilityFunctions[numOfCriterion].Criterion.LinearSegments;
                }

            if (indexOfPointValue < partialUtility.PointsValues.Count - 1)
            {
                if (indexOfPointValue > 0)
                {
                    arrayOfValues[count - 1 + indexOfPointValue + 1] += partialUtility.PointsValues[indexOfPointValue].Y - value;
                    arrayOfValues[count - 1 + indexOfPointValue] -= partialUtility.PointsValues[indexOfPointValue].Y - value;
                    partialUtility.PointsValues[indexOfPointValue].Y = value;
                    Result.PartialUtilityFunctions[criterionIndex] = partialUtility;
                }
            }
            else
            {
                var currentCount = 0;
                var subValue = (partialUtility.PointsValues[indexOfPointValue].Y - value) / (Result.PartialUtilityFunctions.Count - 1);
                for (var partialUtilityIndex = 0; partialUtilityIndex < Result.PartialUtilityFunctions.Count; partialUtilityIndex++)
                {
                    currentCount += Result.PartialUtilityFunctions[partialUtilityIndex].Criterion.LinearSegments;
                    if (partialUtilityIndex != criterionIndex)
                    {
                        var pointValue = Result.PartialUtilityFunctions[partialUtilityIndex].PointsValues;
                        pointValue[pointValue.Count - 1].Y += subValue;
                        arrayOfValues[currentCount - 1] += subValue;
                        Result.PartialUtilityFunctions[partialUtilityIndex].PointsValues = pointValue;
                    }
                }

                partialUtility.PointsValues[indexOfPointValue].Y -= subValue * (Result.PartialUtilityFunctions.Count - 1);
                arrayOfValues[count - 1 + indexOfPointValue] -= subValue * (Result.PartialUtilityFunctions.Count - 1);
                Result.PartialUtilityFunctions[criterionIndex] = partialUtility;
            }

            UpdateMinMax();

            var finalReferenceList = CreateRanking(arrayOfValues, transientMatrix, arternativesList);
            var finalReferenceRanking = new FinalRanking(finalReferenceList);
            var tau = CalculateKendallCoefficient(finalReferenceRanking);
            var restOfReferenceList = CreateRanking(arrayOfValues, otherAlternativesMatrix, otherAlternatives);
            var allFinalRankingEntry = finalReferenceList.Concat(restOfReferenceList).ToList();
            allFinalRankingEntry = allFinalRankingEntry.OrderByDescending(o => o.Utility).ToList();
            for (var i = 0; i < allFinalRankingEntry.Count; i++) allFinalRankingEntry[i].Position = i + 1;
            Result.FinalRanking.FinalRankingCollection = new ObservableCollection<FinalRankingEntry>(allFinalRankingEntry);
            Result.KendallCoefficient = tau;
        }

        private (double[] deepCopyofMin, double[] deepCopyofMax) CalculateRangeOfValues(double[,] restrictions, double[] arrayOfValues)
        {
            var min = new double[restrictions.GetLength(1) - 1];
            var max = new double[restrictions.GetLength(1) - 1];

            for (var i = 0; i < restrictions.GetLength(1) - 1; i++)
            {
                min[i] = 0;
                max[i] = 1;
            }

            var criterionEndPoints = new List<int>();
            var count = 0;
            for (var i = 0; i < criteriaList.Count; i++)
            {
                count += criteriaList[i].LinearSegments;
                criterionEndPoints.Add(count - 1);
            }


            for (var rowIndex = 0; rowIndex < restrictions.GetLength(0); rowIndex++)
            {
                count = 0;
                for (var numOfCriterion = 0; numOfCriterion < criteriaList.Count; numOfCriterion++)
                for (var i = 0; i < criteriaList[numOfCriterion].LinearSegments; i++)
                {
                    double localMin;
                    double localMax;
                    if (PreserveKendallCoefficient)
                    {
                        if (i == criteriaList[numOfCriterion].LinearSegments - 1)
                        {
                            (localMin, localMax) =
                                CalculateValue(GetRow(restrictions, rowIndex), count, arrayOfValues, false, criterionEndPoints);
                        }
                        else
                        {
                            (localMin, localMax) =
                                CalculateValue(GetRow(restrictions, rowIndex), count, arrayOfValues, true, criterionEndPoints);
                            if (localMax > arrayOfValues[count] + arrayOfValues[count + 1])
                                localMax = Math.Round(arrayOfValues[count] + arrayOfValues[count + 1], 10);
                        }
                    }
                    else
                    {
                        if (i == criteriaList[numOfCriterion].LinearSegments - 1)
                        {
                            localMin = 0;
                            localMax = 1;
                        }
                        else
                        {
                            localMin = 0;
                            localMax = arrayOfValues[count] + arrayOfValues[count + 1];
                        }
                    }

                    if (localMin < 0) localMin = 0;
                    if (localMax > 1) localMax = 1;
                    if (min[count] < localMin) min[count] = localMin;
                    if (max[count] > localMax) max[count] = localMax;
                    count++;
                }
            }

            var deepCopyofMin = new double[min.Length];
            var deepCopyofMax = new double[max.Length];
            var precision = 10E+14;
            for (var i = 0; i < min.Length; i++)
            {
                deepCopyofMax[i] = Math.Floor(max[i] * precision) / precision;
                deepCopyofMin[i] = Math.Ceiling(min[i] * precision) / precision;
                if (deepCopyofMax[i] < deepCopyofMin[i])
                {
                    if (deepCopyofMax[i] < arrayOfValues[i]) deepCopyofMax[i] = arrayOfValues[i];

                    if (deepCopyofMin[i] > arrayOfValues[i]) deepCopyofMin[i] = arrayOfValues[i];
                    if (max[i] < arrayOfValues[i]) max[i] = arrayOfValues[i];
                    if (min[i] > arrayOfValues[i]) min[i] = arrayOfValues[i];
                }
            }

            foreach (var element in criterionEndPoints)
            {
                double endPointsMin = 1;
                double endPointsMax = 1;
                foreach (var innerElement in criterionEndPoints)
                    if (element != innerElement)
                    {
                        var val = Math.Floor((max[innerElement] - arrayOfValues[innerElement]) * precision) / precision;
                        if (endPointsMax > val)
                            endPointsMax = val;
                        val = Math.Floor((arrayOfValues[innerElement] - min[innerElement]) * precision) / precision;
                        if (endPointsMin > val)
                            endPointsMin = val;
                    }

                if (endPointsMax * (criterionEndPoints.Count - 1) < arrayOfValues[element] - min[element])
                    deepCopyofMin[element] = arrayOfValues[element] - endPointsMax * (criterionEndPoints.Count - 1);
                if (endPointsMin * (criterionEndPoints.Count - 1) < max[element] - arrayOfValues[element])
                    deepCopyofMax[element] = arrayOfValues[element] + endPointsMin * (criterionEndPoints.Count - 1);
            }

            count = 0;
            for (var numOfCriterion = 0; numOfCriterion < criteriaList.Count; numOfCriterion++)
            {
                var sum = (double) 0;
                for (var i = 0; i < criteriaList[numOfCriterion].LinearSegments; i++)
                {
                    deepCopyofMin[count] += sum;
                    deepCopyofMax[count] += sum;
                    sum += arrayOfValues[count];
                    if (deepCopyofMin[count] > 1) deepCopyofMin[count] = 1;
                    if (deepCopyofMax[count] > 1) deepCopyofMax[count] = 1;
                    count++;
                }
            }


            return (deepCopyofMin, deepCopyofMax);
        }


        private (double min, double max) CalculateValue(double[] row, int index, double[] arrayOfValues, bool hasNext,
            List<int> criterionEndPoints)
        {
            var min = (double) -1;
            var max = (double) 1;
            var nominator = (double) 0;
            var denominator = (double) 0;
            var sum = (double) 0;


            for (var i = 0; i < arrayOfValues.Length; i++)
                nominator -= arrayOfValues[i] * row[i];
            nominator += row[row.Length - 1];


            if (hasNext)
            {
                denominator = row[index] - row[index + 1];
            }
            else
            {
                denominator = row[index];
                foreach (var element in criterionEndPoints)
                    if (element != index)
                        denominator -= row[element] / (criterionEndPoints.Count - 1);
            }


            if (row[row.Length - 1] > 0)
            {
                if (denominator > 0)
                {
                    sum = arrayOfValues[index] + nominator / denominator;
                    min = sum;
                    max = 1;
                }
                else if (denominator < 0)
                {
                    sum = arrayOfValues[index] + nominator / denominator;
                    min = -1;
                    max = sum;
                }
                else
                {
                    min = -1;
                    max = 1;
                }

                return (min, max);
            }

            nominator += DeltaThreshold - 10E-14;
            if (denominator > 0)
            {
                sum = arrayOfValues[index] + nominator / denominator;
                min = -1;
                max = sum;
            }
            else if (denominator < 0)
            {
                sum = arrayOfValues[index] + nominator / denominator;
                min = sum;
                max = 1;
            }
            else
            {
                min = -1;
                max = 1;
            }

            return (min, max);
        }


        private double[,] CalculateRestrictions(double[,] recalculatedMatrix, FinalRanking finalRanking)
        {
            var height = finalRanking.FinalRankingCollection.Count - 2 > 0
                ? factorialRecursion(variantsList.Count) /
                  factorialRecursion(variantsList.Count - 2)
                : 2;
            var matrix = new double[height, criterionFieldsCount + 1];
            var extra = -1;
            for (var row1 = 0; row1 < variantsList.Count; row1++)
            for (var row2 = row1; row2 < variantsList.Count; row2++)
                if (row1 != row2)
                    if (Math.Round(finalRanking.FinalRankingCollection[row1].Utility - finalRanking.FinalRankingCollection[row2].Utility,
                            14) >=
                        DeltaThreshold)
                    {
                        extra++;
                        for (var c = 0; c < criterionFieldsCount; c++)
                            matrix[extra, c] = Math.Round(recalculatedMatrix[row1, c] - recalculatedMatrix[row2, c], 14);
                        matrix[extra, matrix.GetLength(1) - 1] = DeltaThreshold;
                    }
                    else
                    {
                        extra++;
                        for (var c = 0; c < criterionFieldsCount; c++)
                            matrix[extra, c] = Math.Round(recalculatedMatrix[row1, c] - recalculatedMatrix[row2, c], 14);
                        matrix[extra, matrix.GetLength(1) - 1] = 0;
                        extra++;
                        for (var c = 0; c < criterionFieldsCount; c++)
                            matrix[extra, c] = Math.Round(recalculatedMatrix[row2, c] - recalculatedMatrix[row1, c], 14);
                        matrix[extra, matrix.GetLength(1) - 1] = 0;
                    }

            return matrix;
        }

        public int factorialRecursion(int number)
        {
            if (number == 1)
                return 1;
            return number * factorialRecursion(number - 1);
        }

        public double[,] RecreateMatrix(FinalRanking finalRanking)
        {
            var finalRankingEntryList = finalRanking.FinalRankingCollection;
            var matrix = new double[finalRankingEntryList.Count, criterionFieldsCount];
            for (var i = 0; i < variantsList.Count; i++)
            {
                var row = GenerateRow(criterionFieldsCount, finalRankingEntryList[i].Alternative);
                for (var j = 0; j < criterionFieldsCount; j++) matrix[i, j] = row[j];
            }

            return matrix;
        }

        private double CalculateKendallCoefficient(FinalRanking finalReferenceRanking)
        {
            var matrix1 = CreateKendallMatrix();
            var matrix2 = CreateKendallMatrix(finalReferenceRanking);
            double lengthBetweenMatrix = 0, tau;
            for (var row = 0; row < matrix1.GetLength(0); row++)
            for (var c = 0; c < matrix1.GetLength(1); c++)
                lengthBetweenMatrix += Math.Abs(matrix1[row, c] - matrix2[row, c]);

            lengthBetweenMatrix /= 2;
            tau = 1 - 4 * lengthBetweenMatrix /
                  (finalReferenceRanking.FinalRankingCollection.Count * (finalReferenceRanking.FinalRankingCollection.Count - 1));
            return tau;
        }

        private double[,] CreateKendallMatrix()
        {
            var rankingMatrix = new double[variantsList.Count, variantsList.Count];
            for (var row = 0; row < rankingMatrix.GetLength(0); row++)
            for (var c = 0; c < rankingMatrix.GetLength(1); c++)
                if (row == c || variantsList[row].Value - variantsList[c].Value > 0)
                    rankingMatrix[row, c] = 0;
                else if (variantsList[row].Value - variantsList[c].Value == 0)
                    rankingMatrix[row, c] = 0.5;
                else rankingMatrix[row, c] = 1;

            return rankingMatrix;
        }

        private double[,] CreateKendallMatrix(FinalRanking ranking)
        {
            var rankingMatrix = new double[ranking.FinalRankingCollection.Count, ranking.FinalRankingCollection.Count];
            for (var row = 0; row < rankingMatrix.GetLength(0); row++)
            {
                var indexRow = -1;
                for (var rowInRanking = 0; rowInRanking < rankingMatrix.GetLength(0); rowInRanking++)
                    if (ranking.FinalRankingCollection[row].Alternative.Name == variantsList[rowInRanking].Key.Name)
                    {
                        indexRow = rowInRanking;
                        break;
                    }

                for (var c = 0; c < rankingMatrix.GetLength(1); c++)
                {
                    var indexCol = -1;
                    for (var colInRanking = 0; colInRanking < rankingMatrix.GetLength(0); colInRanking++)
                        if (ranking.FinalRankingCollection[c].Alternative.Name == variantsList[colInRanking].Key.Name)
                        {
                            indexCol = colInRanking;
                            break;
                        }

                    if (indexRow == indexCol ||
                        Math.Round(ranking.FinalRankingCollection[c].Utility - ranking.FinalRankingCollection[row].Utility, 10) >=
                        DeltaThreshold)
                        rankingMatrix[indexRow, indexCol] = 0;
                    else if (Math.Abs(
                                 Math.Round(ranking.FinalRankingCollection[c].Utility - ranking.FinalRankingCollection[row].Utility, 10)) <
                             DeltaThreshold)
                        rankingMatrix[indexRow, indexCol] = 0.5;
                    else rankingMatrix[indexRow, indexCol] = 1;
                }
            }

            return rankingMatrix;
        }

        private ObservableCollection<FinalRankingEntry> CreateRanking(double[] arrayOfValues, double[,] transientMatrix,
            List<Alternative> listOfAlternatives)
        {
            var finalRankingList = new List<FinalRankingEntry>();
            for (var i = 0; i < transientMatrix.GetLength(0); i++)
            {
                var score = CreateFinalRankingEntryUtility(arrayOfValues, GetRow(transientMatrix, i));
                var finalRankingEntry = new FinalRankingEntry(-1, listOfAlternatives[i], score);
                finalRankingList.Add(finalRankingEntry);
            }

            var finalRankingSorted = finalRankingList.OrderByDescending(o => o.Utility).ToList();
            for (var i = 0; i < transientMatrix.GetLength(0); i++) finalRankingSorted[i].Position = i;

            return new ObservableCollection<FinalRankingEntry>(finalRankingSorted);
        }

        private double CreateFinalRankingEntryUtility(double[] arrayOfValues, double[] row)
        {
            double score = 0;
            for (var i = 0; i < arrayOfValues.Length; i++) score += arrayOfValues[i] * row[i];
            return Math.Round(score, 14);
        }

        private (List<PartialUtility>, double[]) MakePartialUtilityFunction(Dictionary<double, double> doubles)
        {
            var partialUtilityList = new List<PartialUtility>();
            List<PartialUtilityValues> list;
            double[] partialArray = { }, arrayOfValues = { };
            var count = 0;
            foreach (var criterion in criteriaList)
            {
                (count, list, partialArray) = NextPartialUtility(doubles, criterion, count);
                arrayOfValues = AddTailToArray(arrayOfValues, partialArray);
                partialUtilityList.Add(new PartialUtility(criterion, list));
            }

            return (partialUtilityList, arrayOfValues);
        }

        private double[] AddTailToArray(double[] arrayOfValues, double[] partialArray)
        {
            var newArray = new double[arrayOfValues.Length + partialArray.Length];
            for (var i = 0; i < arrayOfValues.Length; i++) newArray[i] = arrayOfValues[i];
            for (var i = arrayOfValues.Length; i < arrayOfValues.Length + partialArray.Length; i++)
                newArray[i] = partialArray[i - arrayOfValues.Length];

            return newArray;
        }

        private (int, List<PartialUtilityValues>, double[]) NextPartialUtility(Dictionary<double, double> doubles, Criterion criterion,
            int count)
        {
            var linearSegments = criterion.LinearSegments;
            var arrayOfValues = new double[criterion.LinearSegments];
            List<PartialUtilityValues> list;
            double segmentValue = Math.Round(((criterion.MaxValue - criterion.MinValue) / linearSegments), 14), currentPoint, currentValue = 0;
            if (criterion.CriterionDirection == "Gain")
            {
                currentPoint = criterion.MinValue;
                var partialUtilityValues = new PartialUtilityValues(currentPoint, currentValue);
                list = new List<PartialUtilityValues> {partialUtilityValues};
                for (var s = 0; s < linearSegments; s++)
                {
                    currentPoint = s < linearSegments - 1 ? criterion.MinValue + (s + 1) * segmentValue : criterion.MaxValue;
                    arrayOfValues[s] = 0;
                    if (doubles.Keys.Contains(count))
                    {
                        currentValue += doubles[count];
                        arrayOfValues[s] = doubles[count];
                    }

                    partialUtilityValues = new PartialUtilityValues(currentPoint, currentValue);
                    list.Add(partialUtilityValues);
                    count++;
                }
            }
            else
            {
                currentPoint = criterion.MaxValue;
                var partialUtilityValues = new PartialUtilityValues(currentPoint, currentValue);
                list = new List<PartialUtilityValues> {partialUtilityValues};
                for (var s = 0; s < linearSegments; s++)
                {
                    currentPoint = s < linearSegments - 1 ? criterion.MaxValue - (s + 1) * segmentValue : criterion.MinValue;
                    arrayOfValues[s] = 0;
                    if (doubles.Keys.Contains(count))
                    {
                        currentValue += doubles[count];
                        arrayOfValues[s] = doubles[count];
                    }
                    partialUtilityValues = new PartialUtilityValues(currentPoint, currentValue);
                    list.Add(partialUtilityValues);
                    count++;
                }
            }

            return (count, list, arrayOfValues);
        }


        public double[,] CreateSimplexMatrix(int height, int width, double[,] matrix)
        {
            var simplexMatrix = new double[height, width];
            for (var r = 0; r < height; r++)
            for (var c = 0; c < height; c++)
                simplexMatrix[r, c] = 0;

            var widthWithoutSlack = width - height - variantsList.Count + equals.Count;
            var cj = new double[width];
            var basic = new int[height];
            for (var c = 0; c < cj.Length; c++) cj[c] = 0;

            for (var r = 0; r < variantsList.Count - 1; r++)
            {
                for (var c = 0; c < criterionFieldsCount; c++) simplexMatrix[r, c] = matrix[r, c] - matrix[r + 1, c];
                simplexMatrix[r, r * 2 + criterionFieldsCount] = -1;
                simplexMatrix[r, r * 2 + criterionFieldsCount + 1] = 1;
                simplexMatrix[r, r * 2 + criterionFieldsCount + 2] = 1;
                simplexMatrix[r, r * 2 + criterionFieldsCount + 3] = -1;
                for (var field = 0; field < 4; field++)
                    cj[r * 2 + criterionFieldsCount + field] = 1;
            }

            for (var c = 0; c < criterionFieldsCount; c++)
                simplexMatrix[variantsList.Count - 1, c] = c < criterionFieldsCount ? 1 : 0;

            var i = 0;
            var bound = widthWithoutSlack + variantsList.Count - equals.Count;
            for (var r = 0; r < height; r++)
            {
                for (var c = widthWithoutSlack; c < bound; c++)
                    if (!equals.Contains(r) && c == widthWithoutSlack + i)
                    {
                        simplexMatrix[r, c] = -1;
                        i++;
                        break;
                    }

                for (var c = bound; c < width; c++)
                    simplexMatrix[r, c] = c != r + bound ? 0 : 1;
            }

            for (var c = bound; c < width; c++)
                cj[c] = double.PositiveInfinity;

            for (var r = 0; r < height; r++)
                basic[r] = bound + r;

            vectorCj = cj;
            basicVariables = basic;
            simplexMatrix = AddAnswerColumn(simplexMatrix);
            return simplexMatrix;
        }

        public double[,] AddAnswerColumn(double[,] sM)
        {
            int height = sM.GetLength(0), width = sM.GetLength(1) + 1;
            var simplexMatrix = new double[height, width];
            for (var row = 0; row < height; row++)
            {
                for (var c = 0; c < sM.GetLength(1); c++) simplexMatrix[row, c] = sM[row, c];
                simplexMatrix[row, simplexMatrix.GetLength(1) - 1] = equals.Contains(row) ? 0 : DeltaThreshold;
            }

            simplexMatrix[height - 1, simplexMatrix.GetLength(1) - 1] = 1;

            return simplexMatrix;
        }

        public double[,] CreateMatrix(List<Alternative> listOfAlternatives)
        {
            var matrix = new double[listOfAlternatives.Count, criterionFieldsCount];
            for (var i = 0; i < listOfAlternatives.Count; i++)
            {
                var row = GenerateRow(criterionFieldsCount, listOfAlternatives[i]);
                for (var j = 0; j < criterionFieldsCount; j++) matrix[i, j] = row[j];
            }

            return matrix;
        }

        public double[] GenerateRow(int width, Alternative ae)
        {
            var row = new double[width];
            var index = 0;
            foreach (var entry in ae.CriteriaValuesList)
            {
                var tmpCriterion = new Criterion();
                foreach (var criterion in criteriaList)
                    if (criterion.Name == entry.Name)
                        tmpCriterion = criterion;
                var fields = GenerateCriterionFields(tmpCriterion, (double) entry.Value);
                for (var j = 0; j < fields.Length; j++) row[index++] = fields[j];
            }

            return row;
        }

        public double[] GenerateCriterionFields(Criterion criterion, double value)
        {
            var linearSegments = criterion.LinearSegments;
            double segmentValue = (criterion.MaxValue - criterion.MinValue) / linearSegments, lowerBound, upperBound;
            var fields = new double[linearSegments];
            if (criterion.CriterionDirection == "Gain")
            {
                lowerBound = criterion.MinValue;
                for (var s = 0; s < linearSegments; s++)
                {
                    upperBound = criterion.MinValue + (s + 1) * segmentValue;
                    if (value < upperBound)
                    {
                        if (value <= lowerBound)
                        {
                            fields[s] = 0;
                        }
                        else
                        {
                            fields[s] = Math.Round((value - lowerBound) / (upperBound - lowerBound), 14);
                            if (s > 0) fields[s - 1] = 1;
                        }
                    }
                    else
                    {
                        fields[s] = 1;
                    }

                    lowerBound = upperBound;
                }
            }
            else
            {
                lowerBound = criterion.MaxValue;
                for (var s = 0; s < linearSegments; s++)
                {
                    upperBound = criterion.MaxValue - (s + 1) * segmentValue;
                    if (value > upperBound)
                    {
                        if (value >= lowerBound)
                        {
                            fields[s] = 0;
                        }
                        else
                        {
                            fields[s] = Math.Round((lowerBound - value) / (lowerBound - upperBound), 14);
                            if (s > 0) fields[s - 1] = 1;
                        }
                    }
                    else
                    {
                        fields[s] = 1;
                    }

                    lowerBound = upperBound;
                }
            }

            return fields;
        }

        public int WidthOfSimplexMatrix(Alternative alternative)
        {
            var width = criterionFieldsCount;
            width += variantsList.Count * 2;
            width += variantsList.Count + variantsList.Count - equals.Count;
            return width;
        }

        public double[] GetRow(double[,] matrix, int rowNumber)
        {
            return Enumerable.Range(0, matrix.GetLength(1))
                .Select(x => matrix[rowNumber, x])
                .ToArray();
        }
    }
}