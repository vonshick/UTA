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
using System.ComponentModel;
using System.Runtime.CompilerServices;
using UTA.Annotations;
using UTA.Models.Tab;
using UTA.Properties;

namespace UTA.ViewModels
{
    public class SettingsTabViewModel : Tab, INotifyPropertyChanged
    {
        private double _deltaThreshold = 0.0000001;
        private int _plotsPartialUtilityDecimalPlaces = 3;

        public SettingsTabViewModel()
        {
            Name = "Settings";
        }


        public int PlotsPartialUtilityDecimalPlaces
        {
            get => _plotsPartialUtilityDecimalPlaces;
            set
            {
                if (value == _plotsPartialUtilityDecimalPlaces) return;
                if (value < 1 || value > 7) throw new ArgumentException("Value must be between 1 - 7 inclusive.");
                _plotsPartialUtilityDecimalPlaces = value;
                OnPropertyChanged(nameof(PlotsPartialUtilityDecimalPlaces));
            }
        }

        public double DeltaThreshold
        {
            get => _deltaThreshold;
            set
            {
                if (value.Equals(_deltaThreshold)) return;
                if (value < 0 || value > 1) throw new ArgumentException("Value must be between 0 - 1 inclusive.");
                double refValue = value * 10E10;
                if (refValue != Math.Floor(refValue)) throw new ArgumentException("The maximum number of decimal places is 10.");
                _deltaThreshold = value;
                OnPropertyChanged(nameof(DeltaThreshold));
            }
        }

        public bool ShowWelcomeTabOnStart
        {
            get => Settings.Default.ShowWelcomeTabOnStart;
            set
            {
                Settings.Default.ShowWelcomeTabOnStart = value;
                Settings.Default.Save();
            }
        }

        public bool DisplayChartsInSeparateTabs
        {
            get => Settings.Default.DisplayChartsInSeparateTabs;
            set
            {
                Settings.Default.DisplayChartsInSeparateTabs = value;
                Settings.Default.Save();
            }
        }


        public event PropertyChangedEventHandler PropertyChanged;


        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}