﻿// Copyright © 2020 Tomasz Pućka, Piotr Hełminiak, Marcin Rochowiak, Jakub Wąsik

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
using System.Globalization;
using System.Windows.Data;

namespace UTA.Converters
{
    internal class IndexToRankConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is int index ? index + 1 : (object) null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is int index ? index - 1 : (object) null;
        }
    }
}