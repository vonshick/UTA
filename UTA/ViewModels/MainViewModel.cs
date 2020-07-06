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
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using CalculationsEngine;
using DataModel.Input;
using DataModel.Results;
using ExportModule;
using ImportModule;
using MahApps.Metro.Controls.Dialogs;
using Microsoft.Win32;
using Ookii.Dialogs.Wpf;
using UTA.Annotations;
using UTA.Helpers;
using UTA.Models;
using UTA.Models.Tab;

namespace UTA.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly IDialogCoordinator _dialogCoordinator;
        private List<Alternative> _currentCalculationAlternativesCopy;
        private List<Criterion> _currentCalculationCriteriaCopy;
        private List<List<Alternative>> _currentCalculationReferenceRankingCopy;
        private bool _preserveKendallCoefficient = true;
        private SaveData _saveData;
        private Solver _solver;
        private ITab _tabToSelect;


        public MainViewModel(IDialogCoordinator dialogCoordinator)
        {
            Criteria = new Criteria();
            ReferenceRanking = new ReferenceRanking();
            Alternatives = new Alternatives(Criteria, ReferenceRanking);
            Results = new Results();

            _dialogCoordinator = dialogCoordinator;
            _saveData = new SaveData(null, null);

            Tabs = new ObservableCollection<ITab>();
            Tabs.CollectionChanged += TabsCollectionChanged;
            ChartTabViewModels = new ObservableCollection<ChartTabViewModel>();
            ShowTabCommand = new RelayCommand(tabViewModel => ShowTab((ITab) tabViewModel));

            CriteriaTabViewModel = new CriteriaTabViewModel(Criteria);
            AlternativesTabViewModel = new AlternativesTabViewModel(Criteria, Alternatives);
            ReferenceRankingTabViewModel = new ReferenceRankingTabViewModel(ReferenceRanking, Alternatives);
            SettingsTabViewModel = new SettingsTabViewModel();
            WelcomeTabViewModel = new WelcomeTabViewModel();

            Criteria.CriteriaCollection.CollectionChanged += InstancePropertyChanged;
            Alternatives.AlternativesCollection.CollectionChanged += InstancePropertyChanged;
            ReferenceRanking.RankingsCollection.CollectionChanged += InstancePropertyChanged;
            Results.FinalRanking.FinalRankingCollection.CollectionChanged += InstancePropertyChanged;

            Criteria.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName != nameof(Criteria.CriteriaCollection)) return;
                InstancePropertyChanged();
                Criteria.CriteriaCollection.CollectionChanged += InstancePropertyChanged;
            };
            Alternatives.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName != nameof(Alternatives.AlternativesCollection)) return;
                InstancePropertyChanged();
                Alternatives.AlternativesCollection.CollectionChanged += InstancePropertyChanged;
            };
            ReferenceRanking.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName != nameof(ReferenceRanking.RankingsCollection)) return;
                InstancePropertyChanged();
                ReferenceRanking.RankingsCollection.CollectionChanged += InstancePropertyChanged;
            };
            Results.FinalRanking.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName != nameof(Results.FinalRanking.FinalRankingCollection)) return;
                InstancePropertyChanged();
                Results.FinalRanking.FinalRankingCollection.CollectionChanged += InstancePropertyChanged;
            };
        }


        public Alternatives Alternatives { get; set; }
        public Criteria Criteria { get; set; }
        public ReferenceRanking ReferenceRanking { get; set; }
        public Results Results { get; set; }

        public ObservableCollection<ITab> Tabs { get; }
        public ObservableCollection<ChartTabViewModel> ChartTabViewModels { get; }
        public RelayCommand ShowTabCommand { get; }

        public CriteriaTabViewModel CriteriaTabViewModel { get; }
        public AlternativesTabViewModel AlternativesTabViewModel { get; }
        public ReferenceRankingTabViewModel ReferenceRankingTabViewModel { get; }
        public SettingsTabViewModel SettingsTabViewModel { get; }
        public WelcomeTabViewModel WelcomeTabViewModel { get; }


        public bool PreserveKendallCoefficient
        {
            get => _preserveKendallCoefficient;
            set
            {
                if (_preserveKendallCoefficient == value) return;
                _preserveKendallCoefficient = value;
                _solver?.UpdatePreserveKendallCoefficient(_preserveKendallCoefficient);
                RefreshCharts();
                OnPropertyChanged(nameof(PreserveKendallCoefficient));
            }
        }

        public ITab TabToSelect
        {
            get => _tabToSelect;
            set
            {
                _tabToSelect = value;
                OnPropertyChanged(nameof(TabToSelect));
            }
        }

        public bool IsThereAnyApplicationProgress =>
            Results.FinalRanking.FinalRankingCollection.Count != 0
            || ReferenceRanking.RankingsCollection.Count != 0
            || Alternatives.AlternativesCollection.Count != 0
            || Criteria.CriteriaCollection.Count != 0;

        public event PropertyChangedEventHandler PropertyChanged;


        private void TabsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add)
                ((ITab) e.NewItems[0]).CloseRequested += Tabs_Removing;
            else if (e.Action == NotifyCollectionChangedAction.Remove)
                ((ITab) e.OldItems[0]).CloseRequested -= Tabs_Removing;
        }

        private void Tabs_Removing(object sender, EventArgs e)
        {
            Tabs.Remove((ITab) sender);
        }

        public void ShowTab(ITab tabModel)
        {
            if (Tabs.Contains(tabModel)) TabToSelect = tabModel;
            else Tabs.Add(tabModel);
        }

        [UsedImplicitly]
        public async void CalculateButtonClicked(object sender, RoutedEventArgs e)
        {
            if (!await IsInstanceCorrectToRunCalculations()) return;

            if (ChartTabViewModels.Count > 0 && await ShowLosingProgressWarning() == MessageDialogResult.Negative) return;

            foreach (var viewModel in ChartTabViewModels) Tabs.Remove(viewModel);
            ChartTabViewModels.Clear();
            Results.FinalRanking.FinalRankingCollection.Clear();
            Results.PartialUtilityFunctions.Clear();
            Results.KendallCoefficient = null;

            _currentCalculationCriteriaCopy = Criteria.GetDeepCopyOfCriteria();
            _currentCalculationAlternativesCopy = Alternatives.GetDeepCopyOfAlternatives();
            _currentCalculationReferenceRankingCopy = ReferenceRanking.GetDeepCopyOfReferenceRanking(_currentCalculationAlternativesCopy);
            var alternativesWithoutRanks =
                _currentCalculationAlternativesCopy.Where(alternative => alternative.ReferenceRank == null).ToList();
            _solver = new Solver(
                _currentCalculationReferenceRankingCopy,
                _currentCalculationCriteriaCopy,
                alternativesWithoutRanks,
                Results,
                PreserveKendallCoefficient,
                SettingsTabViewModel.DeltaThreshold,
                SettingsTabViewModel.EpsilonThreshold
            );
            _solver.Calculate();

            foreach (var partialUtility in Results.PartialUtilityFunctions)
            {
                var viewModel = new ChartTabViewModel(_solver, partialUtility, SettingsTabViewModel, RefreshCharts);
                ChartTabViewModels.Add(viewModel);
                Tabs.Add(viewModel);
            }

            if (ChartTabViewModels.Count > 0) ShowTab(ChartTabViewModels[0]);
        }

        private async Task<MessageDialogResult> ShowLosingProgressWarning()
        {
            var dialogResult = await _dialogCoordinator.ShowMessageAsync(this, "Losing current progress.",
                "Your current calculations progress will be lost.\n" +
                "If you accidentally closed some tabs, you can show them again by using \"Show\" menu option.\n" +
                "Do you want to continue?",
                MessageDialogStyle.AffirmativeAndNegative,
                new MetroDialogSettings
                {
                    AffirmativeButtonText = "Yes",
                    NegativeButtonText = "Cancel",
                    DefaultButtonFocus = MessageDialogResult.Affirmative,
                    AnimateShow = false,
                    AnimateHide = false
                });
            return dialogResult;
        }

        private async Task<bool> IsInstanceCorrectToRunCalculations()
        {
            if (Criteria.CriteriaCollection.Count == 0)
            {
                ShowCalculateErrorDialog("It's required to provide at least 1 criterion to begin UTA calculations.");
                AddTabIfNeeded(CriteriaTabViewModel);
                AddTabIfNeeded(AlternativesTabViewModel);
                AddTabIfNeeded(ReferenceRankingTabViewModel);
                ShowTab(CriteriaTabViewModel);
                return false;
            }

            if (Alternatives.AlternativesCollection.Count <= 1)
            {
                ShowCalculateErrorDialog("It's required to provide at least 2 alternatives to begin UTA calculations.");
                AddTabIfNeeded(AlternativesTabViewModel);
                AddTabIfNeeded(ReferenceRankingTabViewModel);
                ShowTab(AlternativesTabViewModel);
                return false;
            }

            if (!await IsCriteriaValuesPrecisionAcceptable(Criteria.CriteriaCollection)) return false;

            var isAnyCriterionValueNull = Alternatives.AlternativesCollection.Any(alternative =>
                alternative.CriteriaValuesList.Any(criterionValue => criterionValue.Value == null));
            if (isAnyCriterionValueNull)
            {
                ShowCalculateErrorDialog(
                    "It's required to provide data to every criterion value to all alternatives to begin UTA calculations.");
                ShowTab(AlternativesTabViewModel);
                return false;
            }

            if (ReferenceRanking.RankingsCollection.Count < 2)
            {
                ShowCalculateErrorDialog(
                    "It's required to provide at least 2 ranks in Reference Ranking filled with at least 1 alternative to begin UTA calculations.");
                ShowTab(ReferenceRankingTabViewModel);
                return false;
            }

            if (ReferenceRanking.RankingsCollection.Any(rank => rank.Count == 0))
            {
                ShowCalculateErrorDialog(
                    "It's required to fill every rank in Reference Ranking with at least 1 alternative to begin UTA calculations.");
                ShowTab(ReferenceRankingTabViewModel);
                return false;
            }

            return true;
        }

        private async void ShowCalculateErrorDialog(string message, string title = "Incomplete instance data.")
        {
            await _dialogCoordinator.ShowMessageAsync(this,
                title,
                message,
                MessageDialogStyle.Affirmative,
                new MetroDialogSettings
                {
                    AffirmativeButtonText = "OK",
                    DefaultButtonFocus = MessageDialogResult.Affirmative,
                    AnimateShow = false,
                    AnimateHide = false
                });
        }

        private void AddTabIfNeeded(ITab tab)
        {
            if (!Tabs.Contains(tab)) Tabs.Add(tab);
        }

        private async Task<bool> IsCriteriaValuesPrecisionAcceptable(IEnumerable<Criterion> criteriaList)
        {
            var invalidCriteriaValuesNames = new List<string>();
            foreach (var criterion in criteriaList)
                if (Math.Abs(criterion.MaxValue - criterion.MinValue) < 1E-14)
                    invalidCriteriaValuesNames.Add(criterion.Name);

            if (invalidCriteriaValuesNames.Count == 0) return true;

            ShowTab(AlternativesTabViewModel);
            var warningMessage = "Alternatives values on the following criteria have too high precision or are the same:\n";
            foreach (var criterionName in invalidCriteriaValuesNames) warningMessage += $"{criterionName},\n";
            warningMessage +=
                "Please provide lower precision values or at least two unique values on a whole set of alternatives values. Maximal allowed criteria values precision is 14.";
            await _dialogCoordinator.ShowMessageAsync(this,
                "Invalid alternatives values.",
                warningMessage,
                MessageDialogStyle.Affirmative,
                new MetroDialogSettings
                {
                    AffirmativeButtonText = "OK",
                    DefaultButtonFocus = MessageDialogResult.Affirmative,
                    AnimateShow = false,
                    AnimateHide = false
                });
            return false;
        }

        private void RefreshCharts()
        {
            foreach (var chartTabViewModel in ChartTabViewModels) chartTabViewModel.GenerateChartData();
        }

        // xaml enforces void return type
        [UsedImplicitly]
        public async void NewSolution(object sender, RoutedEventArgs e)
        {
            await NewSolution();
        }

        public async Task<bool> NewSolution()
        {
            if (!IsThereAnyApplicationProgress) return true;

            var saveDialogResult = await _dialogCoordinator.ShowMessageAsync(this,
                "Losing current progress.",
                "Your progress will be lost. Would you like to proceed without saving?",
                MessageDialogStyle.AffirmativeAndNegativeAndSingleAuxiliary,
                new MetroDialogSettings
                {
                    AffirmativeButtonText = "Yes",
                    NegativeButtonText = "Save",
                    FirstAuxiliaryButtonText = "Cancel",
                    DialogResultOnCancel = MessageDialogResult.FirstAuxiliary,
                    DefaultButtonFocus = MessageDialogResult.Affirmative,
                    AnimateShow = false,
                    AnimateHide = false
                });

            if (saveDialogResult == MessageDialogResult.FirstAuxiliary) return false;
            if (saveDialogResult == MessageDialogResult.Negative)
                await SaveTypeChooserDialog();

            ResetProgress();
            return true;
        }

        private async Task SaveTypeChooserDialog()
        {
            var saveFileWithResultsDialog = await _dialogCoordinator.ShowMessageAsync(this,
                "Choose save type.",
                "Would you like to save your progress with or without results?",
                MessageDialogStyle.AffirmativeAndNegativeAndSingleAuxiliary,
                new MetroDialogSettings
                {
                    AffirmativeButtonText = "Save With Results",
                    NegativeButtonText = "Save Without Results",
                    FirstAuxiliaryButtonText = "Cancel",
                    DialogResultOnCancel = MessageDialogResult.FirstAuxiliary,
                    DefaultButtonFocus = MessageDialogResult.Affirmative,
                    AnimateShow = false,
                    AnimateHide = false
                });

            if (saveFileWithResultsDialog == MessageDialogResult.Affirmative) SaveWithResultsAsMenuItemClicked();
            else if (saveFileWithResultsDialog == MessageDialogResult.Negative) SaveAsMenuItemClicked();
        }

        private void ResetProgress()
        {
            _solver = null;
            Results.Reset();
            ReferenceRanking.Reset();
            Alternatives.Reset();
            Criteria.Reset();
            foreach (var chartTabViewModel in ChartTabViewModels)
                Tabs.Remove(chartTabViewModel);
            ChartTabViewModels.Clear();
            _currentCalculationReferenceRankingCopy?.Clear();
            _currentCalculationAlternativesCopy?.Clear();
            _currentCalculationCriteriaCopy?.Clear();
            _saveData.IsSavingWithResults = null;
            _saveData.FilePath = null;
        }

        [UsedImplicitly]
        public async void OpenFileMenuItemClicked(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "UTA Extended Input Files (*.xml; *.csv; *.utx; *.xd)|*.xml;*.csv;*.utx;*.xd",
                InitialDirectory = AppDomain.CurrentDomain.BaseDirectory
            };
            if (openFileDialog.ShowDialog() != true) return;

            if (!await NewSolution()) return;

            var filePath = openFileDialog.FileName;
            try
            {
                DataLoader dataLoader = null;
                if (filePath.EndsWith(".xml")) dataLoader = new XMLLoader();
                else if (filePath.EndsWith(".csv")) dataLoader = new CSVLoader();
                else if (filePath.EndsWith(".utx")) dataLoader = new UTXLoader();
                else if (filePath.EndsWith(".xd")) LoadXMCDADirectory(Path.Combine(Path.GetDirectoryName(filePath), Path.GetFileNameWithoutExtension(filePath)));

                if (dataLoader == null) return;

                dataLoader.LoadData(filePath);
                Criteria.CriteriaCollection = new ObservableCollection<Criterion>(dataLoader.CriterionList);
                Alternatives.AlternativesCollection = new ObservableCollection<Alternative>(dataLoader.AlternativeList);
            }
            catch (Exception exception)
            {
                ResetProgress();
                ShowLoadErrorDialog(exception);
            }
        }

        [UsedImplicitly]
        public async void OpenXMCDAMenuItemClicked(object sender, RoutedEventArgs e)
        {
            var openDirectoryDialog = new VistaFolderBrowserDialog {ShowNewFolderButton = true};
            if (openDirectoryDialog.ShowDialog() != true) return;

            if (!await NewSolution()) return;

            var filePath = openDirectoryDialog.SelectedPath;

            LoadXMCDADirectory(filePath);
        }

        private async void LoadXMCDADirectory(string filePath)
        {
            var dataLoader = new XMCDALoader();

            try
            {
                dataLoader.LoadData(filePath);
                Criteria.CriteriaCollection = new ObservableCollection<Criterion>(dataLoader.CriterionList);
                // works assuming that CriteriaValuesList are initialized properly
                Alternatives.AlternativesCollection = new ObservableCollection<Alternative>(dataLoader.AlternativeList);
                PreserveKendallCoefficient = dataLoader.PreserveKendallCoefficient;
                if (dataLoader.Results.PartialUtilityFunctions.Count != Criteria.CriteriaCollection.Count ||
                    !await IsInstanceCorrectToRunCalculations()) return;
                Results.PartialUtilityFunctions = dataLoader.Results.PartialUtilityFunctions;
                _currentCalculationCriteriaCopy = Criteria.GetDeepCopyOfCriteria();
                _currentCalculationAlternativesCopy = Alternatives.GetDeepCopyOfAlternatives();
                _currentCalculationReferenceRankingCopy =
                    ReferenceRanking.GetDeepCopyOfReferenceRanking(_currentCalculationAlternativesCopy);

                var alternativesWithoutRanks =
                    _currentCalculationAlternativesCopy.Where(alternative => alternative.ReferenceRank == null).ToList();
                _solver = new Solver(
                    _currentCalculationReferenceRankingCopy,
                    _currentCalculationCriteriaCopy,
                    alternativesWithoutRanks,
                    Results,
                    PreserveKendallCoefficient,
                    SettingsTabViewModel.DeltaThreshold,
                    SettingsTabViewModel.EpsilonThreshold
                    );
                _solver.LoadState(Results.PartialUtilityFunctions, _currentCalculationReferenceRankingCopy, alternativesWithoutRanks,
                    Results);

                foreach (var partialUtility in Results.PartialUtilityFunctions)
                {
                    var viewModel = new ChartTabViewModel(_solver, partialUtility, SettingsTabViewModel, RefreshCharts);
                    ChartTabViewModels.Add(viewModel);
                    Tabs.Add(viewModel);
                }

                if (ChartTabViewModels.Count > 0) ShowTab(ChartTabViewModels[0]);
            }
            catch (Exception exception)
            {
                ResetProgress();
                if (dataLoader.CurrentlyProcessedFile == null || dataLoader.CurrentlyProcessedFile.Equals(""))
                    ShowLoadErrorDialog(exception);
                else
                    ShowLoadErrorDialog(new Exception(Path.GetFileName(dataLoader.CurrentlyProcessedFile) +
                                                      (exception.Message != null ? $" - {exception.Message}" : "")));
            }
        }


        private async void ShowLoadErrorDialog(Exception exception)
        {
            await _dialogCoordinator.ShowMessageAsync(this,
                "Loading error.",
                exception.Message != null
                    ? $"Can't read this file. An error was encountered with a following message:\n\"{exception.Message}\""
                    : "Can't read this file.",
                MessageDialogStyle.Affirmative,
                new MetroDialogSettings
                {
                    AffirmativeButtonText = "OK",
                    AnimateShow = false,
                    AnimateHide = false,
                    DefaultButtonFocus = MessageDialogResult.Affirmative
                });
        }

        [UsedImplicitly]
        public async void SaveMenuItemClicked(object sender, RoutedEventArgs e)
        {
            await SaveMenuItemClicked();
        }

        // Task return type isn't allowed in XAML
        public async Task SaveMenuItemClicked()
        {
            if (_saveData.IsSavingWithResults == null || _saveData.FilePath == null)
            {
                await SaveTypeChooserDialog();
                return;
            }

            var dataSaver = new XMCDAExporter(
                    _saveData.FilePath,
                    (bool) _saveData.IsSavingWithResults && _currentCalculationCriteriaCopy != null
                        ? _currentCalculationCriteriaCopy
                        : new List<Criterion>(Criteria.CriteriaCollection),
                    (bool) _saveData.IsSavingWithResults && _currentCalculationAlternativesCopy != null
                        ? _currentCalculationAlternativesCopy
                        : new List<Alternative>(Alternatives.AlternativesCollection),
                    Results,
                    PreserveKendallCoefficient)
                {OverwriteFile = true};
            try
            {
                if (_saveData.IsSavingWithResults == true && _currentCalculationCriteriaCopy != null) dataSaver.saveSession();
                else dataSaver.saveInput();
            }
            catch (Exception exception)
            {
                ShowSaveErrorDialog(exception);
            }
        }

        public async void SaveAsMenuItemClicked(object sender = null, RoutedEventArgs e = null)
        {
            var saveXMCDADialog = new SaveFileDialog
            {
                DefaultExt = ".xd",
                ValidateNames = true,
                Filter = "XMCDA output indicator (.xd)|*.xd",
                Title = "Save XMCDA output as"
            };
            if (saveXMCDADialog.ShowDialog() != true) return;

            var directoryPath = saveXMCDADialog.FileName;
            var dataSaver = new XMCDAExporter(directoryPath, new List<Criterion>(Criteria.CriteriaCollection),
                new List<Alternative>(Alternatives.AlternativesCollection), Results, PreserveKendallCoefficient);

            await TryToSave(false, dataSaver, directoryPath);
        }

        public async void SaveWithResultsAsMenuItemClicked(object sender = null, RoutedEventArgs e = null)
        {
            var saveXMCDADialog = new SaveFileDialog
            {
                DefaultExt = ".xd",
                ValidateNames = true,
                Filter = "XMCDA output indicator (.xd)|*.xd",
                Title = "Save XMCDA output as"
            };
            if (saveXMCDADialog.ShowDialog() != true) return;

            var directoryPath = saveXMCDADialog.FileName;
            var dataSaver = new XMCDAExporter(
                directoryPath,
                _currentCalculationCriteriaCopy ?? new List<Criterion>(Criteria.CriteriaCollection),
                _currentCalculationAlternativesCopy ?? new List<Alternative>(Alternatives.AlternativesCollection),
                Results,
                PreserveKendallCoefficient);

            await TryToSave(true, dataSaver, directoryPath);
        }

        private async Task TryToSave(bool shouldSaveWithResults, XMCDAExporter dataSaver, string directoryPath)
        {
            try
            {
                // results are available when copies had been made
                if (shouldSaveWithResults && _currentCalculationCriteriaCopy != null) dataSaver.saveSession();
                else dataSaver.saveInput();
                _saveData.IsSavingWithResults = shouldSaveWithResults;
                _saveData.FilePath = directoryPath;
            }
            catch (XmcdaFileExistsException)
            {
                var dialogResult = await _dialogCoordinator.ShowMessageAsync(this,
                    "Overwriting files.",
                    "Some XMCDA files already exist in this directory. Would you like to overwrite them?",
                    MessageDialogStyle.AffirmativeAndNegative,
                    new MetroDialogSettings
                    {
                        AffirmativeButtonText = "Yes",
                        NegativeButtonText = "Cancel",
                        DefaultButtonFocus = MessageDialogResult.Affirmative,
                        AnimateShow = false,
                        AnimateHide = false
                    });
                if (dialogResult == MessageDialogResult.Affirmative)
                {
                    dataSaver.OverwriteFile = true;
                    try
                    {
                        if (shouldSaveWithResults && _currentCalculationCriteriaCopy != null) dataSaver.saveSession();
                        else dataSaver.saveInput();
                        _saveData.IsSavingWithResults = shouldSaveWithResults;
                        _saveData.FilePath = directoryPath;
                    }
                    catch (Exception exception)
                    {
                        ShowSaveErrorDialog(exception);
                    }
                }
            }
            catch (Exception exception)
            {
                ShowSaveErrorDialog(exception);
            }
        }

        private async void ShowSaveErrorDialog(Exception exception)
        {
            await _dialogCoordinator.ShowMessageAsync(this,
                "Saving error.",
                exception.Message != null
                    ? $"Can't save files. An error was encountered with a following message:\n\"{exception.Message}\""
                    : "Can't save files.",
                MessageDialogStyle.Affirmative,
                new MetroDialogSettings
                {
                    AffirmativeButtonText = "OK",
                    AnimateShow = false,
                    AnimateHide = false,
                    DefaultButtonFocus = MessageDialogResult.Affirmative
                });
        }

        private void InstancePropertyChanged(object sender = null, EventArgs e = null)
        {
            OnPropertyChanged(nameof(IsThereAnyApplicationProgress));
        }

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private struct SaveData
        {
            public bool? IsSavingWithResults;
            [CanBeNull] public string FilePath;

            public SaveData(bool? isSavingWithResults, [CanBeNull] string filePath)
            {
                IsSavingWithResults = isSavingWithResults;
                FilePath = filePath;
            }
        }
    }
}