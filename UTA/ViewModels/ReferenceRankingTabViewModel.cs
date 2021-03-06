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

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using DataModel.Input;
using DataModel.Results;
using UTA.Annotations;
using UTA.Helpers;
using UTA.Models;
using UTA.Models.Tab;

namespace UTA.ViewModels
{
    public class ReferenceRankingTabViewModel : Tab, INotifyPropertyChanged
    {
        private ListCollectionView _alternativesWithoutRanksCollectionView;

        public ReferenceRankingTabViewModel(ReferenceRanking referenceRanking, Alternatives alternatives)
        {
            Name = "Reference Ranking";
            ReferenceRanking = referenceRanking;
            Alternatives = alternatives;

            AlternativesWithoutRanksCollectionView = new ListCollectionView(Alternatives.AlternativesCollection)
            {
                Filter = o => ((Alternative) o).ReferenceRank == null
            };

            ReferenceRanking.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName != nameof(ReferenceRanking.RankingsCollection)) return;
                RefreshFilter();
                InitializeReferenceRankFilterWatchers();
            };

            Alternatives.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName != nameof(Alternatives.AlternativesCollection)) return;
                AlternativesWithoutRanksCollectionView = new ListCollectionView(Alternatives.AlternativesCollection)
                {
                    Filter = o => ((Alternative) o).ReferenceRank == null
                };
            };

            InitializeReferenceRankFilterWatchers();

            NewRank = new ObservableCollection<Alternative>();
            NewRank.CollectionChanged += (sender, args) =>
            {
                if (args.Action != NotifyCollectionChangedAction.Add) return;
                // NewRank is cleared by AlternativeDroppedOnNewRank event handler, so we always have only one item in this collection
                ReferenceRanking.AddAlternativeToRank(NewRank[0], ReferenceRanking.RankingsCollection.Count);
            };

            RemoveRankCommand = new RelayCommand(rank => ReferenceRanking.RemoveRank((int) rank));
            RemoveAlternativeFromRankCommand = new RelayCommand(alternative =>
            {
                var alternativeToRemove = (Alternative) alternative;
                if (alternativeToRemove.ReferenceRank != null)
                    ReferenceRanking.RemoveAlternativeFromRank(alternativeToRemove, (int) alternativeToRemove.ReferenceRank);
            });
        }


        public ReferenceRanking ReferenceRanking { get; }
        public Alternatives Alternatives { get; }

        public ListCollectionView AlternativesWithoutRanksCollectionView
        {
            get => _alternativesWithoutRanksCollectionView;
            set
            {
                _alternativesWithoutRanksCollectionView = value;
                OnPropertyChanged(nameof(AlternativesWithoutRanksCollectionView));
            }
        }

        public ObservableCollection<Alternative> NewRank { get; set; }
        public RelayCommand RemoveAlternativeFromRankCommand { get; }
        public RelayCommand RemoveRankCommand { get; }

        public event PropertyChangedEventHandler PropertyChanged;


        [UsedImplicitly]
        public void AddRank()
        {
            ReferenceRanking.AddRank();
        }

        public void InitializeReferenceRankFilterWatchers()
        {
            foreach (var referenceRanking in ReferenceRanking.RankingsCollection)
                referenceRanking.CollectionChanged += RefreshFilter;

            ReferenceRanking.RankingsCollection.CollectionChanged += InitializeRankFilterWatcher;
        }

        private void InitializeRankFilterWatcher(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add)
            {
                var addedRank = (ObservableCollection<Alternative>) e.NewItems[0];
                addedRank.CollectionChanged += RefreshFilter;
            }
            else if (e.Action == NotifyCollectionChangedAction.Remove || e.Action == NotifyCollectionChangedAction.Reset)
            {
                RefreshFilter();
            }
        }

        private void RefreshFilter(object sender = null, NotifyCollectionChangedEventArgs e = null)
        {
            AlternativesWithoutRanksCollectionView.Refresh();
        }

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}