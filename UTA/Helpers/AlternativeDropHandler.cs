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

using System.Windows;
using DataModel.Input;
using GongSolutions.Wpf.DragDrop;
using GongSolutions.Wpf.DragDrop.Utilities;

namespace UTA.Helpers
{
    public class AlternativeDropHandler : IDropTarget
    {
        public void DragOver(IDropInfo dropInfo)
        {
            if (!(dropInfo.Data is Alternative)) return;
            dropInfo.Effects = DragDropEffects.Move;
        }

        public void Drop(IDropInfo dropInfo)
        {
            if (dropInfo.DragInfo.SourceCollection.Equals(dropInfo.TargetCollection)) return;

            var droppedAlternative = (Alternative) dropInfo.Data;
            var targetList = dropInfo.TargetCollection.TryGetList();
            if (droppedAlternative.ReferenceRank != null) // from rank
            {
                var sourceList = dropInfo.DragInfo.SourceCollection.TryGetList();
                if (!(((FrameworkElement) dropInfo.VisualTarget).Tag is string targetElementTag)) return; // return if from rank to NewRank
                if (targetElementTag == "Alternatives") // from rank to alternatives
                {
                    droppedAlternative.ReferenceRank = null;
                    sourceList.Remove(droppedAlternative);
                }
                else if (targetElementTag == "Rank") // from rank to other rank
                {
                    sourceList.Remove(droppedAlternative);
                    targetList.Add(droppedAlternative);
                }
            }
            else // from alternatives to rank
            {
                targetList.Add(droppedAlternative);
            }
        }
    }
}