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
using UTA.Helpers;

namespace UTA.Models.Tab
{
    public class Tab : ITab
    {
        public Tab()
        {
            CloseCommand = new RelayCommand(_ => CloseRequested?.Invoke(this, EventArgs.Empty), _ => IsCloseable);
        }

        public bool IsCloseable { get; set; } = true;

        public string Name { get; set; }
        public RelayCommand CloseCommand { get; }
        public event EventHandler CloseRequested;
    }
}