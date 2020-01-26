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
using System.ComponentModel;
using System.Runtime.CompilerServices;
using DataModel.Annotations;
using DataModel.PropertyChangedExtended;

namespace DataModel.Input
{
    public class Criterion : INotifyPropertyChanged, INotifyPropertyChangedExtended<string>
    {
        public static double MinNumberOfLinearSegments = 1; // type double, because can't use other type in xaml
        public static double MaxNumberOfLinearSegments = 99; // TODO: change this value according to app performance
        private string _criterionDirection;
        private string _description;
        private int _linearSegments;
        private string _name;
        private double _minValue = double.MaxValue;
        private double _maxValue = double.MinValue;


        public Criterion()
        {
        }

        public Criterion(string name, string criterionDirection)
        {
            Name = name;
            CriterionDirection = criterionDirection;
        }

        public Criterion(string name, string description, string criterionDirection, int linearSegments)
        {
            Name = name;
            Description = description;
            CriterionDirection = criterionDirection;
            LinearSegments = linearSegments;
        }

        [UsedImplicitly] public static string[] CriterionDirectionTypesList { get; } = {"Gain", "Cost"};

        public string ID { get; set; }

        public double MinValue
        {
            get => _minValue;
            set => _minValue = Math.Round(value, 14);
        }

        public double MaxValue
        {
            get => _maxValue;
            set => _maxValue = Math.Round(value, 14);
        }

        public bool IsEnum { get; set; } = false;

        public string Name
        {
            get => _name;
            set
            {
                if (value == _name) return;
                var oldValue = _name;
                _name = value;
                OnPropertyChangedExtended(nameof(Name), oldValue, _name);
            }
        }

        public string CriterionDirection
        {
            get => _criterionDirection;
            set
            {
                if (value == _criterionDirection) return;
                if (value != "Cost" && value != "Gain")
                    throw new ArgumentException("Value must be \"Cost\" or \"Gain\".");
                _criterionDirection = value;
                OnPropertyChanged(nameof(CriterionDirection));
            }
        }

        public int LinearSegments
        {
            get => _linearSegments;
            set
            {
                if (value == _linearSegments) return;
                if (value < MinNumberOfLinearSegments || value > MaxNumberOfLinearSegments)
                    throw new ArgumentException(
                        $"Value must be between {MinNumberOfLinearSegments} - {MaxNumberOfLinearSegments} inclusive.");
                _linearSegments = value;
                OnPropertyChanged(nameof(LinearSegments));
            }
        }

        public string Description
        {
            get => _description;
            set
            {
                if (value == _description) return;
                _description = value;
                OnPropertyChanged(nameof(Description));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;


        protected void OnPropertyChangedExtended(string propertyName, string oldValue, string newValue)
        {
            OnPropertyChanged(new PropertyChangedExtendedEventArgs<string>(propertyName, oldValue, newValue));
        }

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public virtual void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            PropertyChanged?.Invoke(this, e);
        }
    }
}