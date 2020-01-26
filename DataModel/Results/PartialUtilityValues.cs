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

namespace DataModel.Results
{
    public class PartialUtilityValues
    {
        public double MaxValue;
        public double MinValue;
        public double X;
        public double Y;

        public PartialUtilityValues(double x, double y)
        {
            X = x;
            Y = y;
            MinValue = double.MaxValue;
            MaxValue = double.MinValue;
        }

        public PartialUtilityValues(double point, double value, double minValue, double maxValue)
        {
            X = point;
            Y = value;
            MinValue = minValue;
            MaxValue = maxValue;
        }
    }
}