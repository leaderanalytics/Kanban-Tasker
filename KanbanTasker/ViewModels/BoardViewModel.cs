﻿using KanbanTasker.Base;
using KanbanTasker.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using KanbanTasker.Model;
using System.Windows.Input;
using System.Linq;
using Windows.UI.Xaml.Controls;
using Syncfusion.UI.Xaml.Kanban;
using Microsoft.Toolkit.Uwp.UI.Controls;
using LeaderAnalytics.AdaptiveClient;

namespace KanbanTasker.ViewModels
{
    public class BoardViewModel : Observable
    {
        /// <summary>
        /// Variables/Private backing fields
        /// </summary>
        ///
        private PresentationBoard _Board;
        public PresentationBoard Board
        {
            get => _Board;
            set
            {
                _Board = value;
                OnPropertyChanged();
            }
        }
        private PresentationTask _CurrentTask;   
        public PresentationTask CurrentTask
        {
            get => _CurrentTask;
            set
            {
                _CurrentTask = value;
                OnPropertyChanged();
            }
        }
        
        private string _paneTitle;
        private bool _isPointerEntered = false;
        private bool _isEditingTask;
        private string _currentCategory;
        private IAdaptiveClient<IServiceManifest> DataProvider;
        public ICommand NewTaskCommand { get; set; }
        public ICommand EditTaskCommand { get; set; }
        public ICommand SaveTaskCommand { get; set; }
        public ICommand DeleteTaskCommand { get; set; }
        public ICommand DeleteTagCommand { get; set; }
        public ICommand CancelEditCommand { get; set; }

        #region Properties

        /// <summary>
        /// Used to fill indicator key combo box
        /// </summary>
        /// 
        private ObservableCollection<ComboBoxItem> _ColorKeys;
        public ObservableCollection<ComboBoxItem> ColorKeys
        {
            get { return _ColorKeys; }
            set
            {
                _ColorKeys = value;
                OnPropertyChanged();
            }
        }

        
        public string PaneTitle
        {
            get { return _paneTitle; }
            set
            {
                _paneTitle = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Used to determine if pointer entered
        /// inside of a card
        /// </summary>
        public bool IsPointerEntered
        {
            get { return _isPointerEntered; }
            set
            {
                _isPointerEntered = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Used to display Edit/New text on splitview pane
        /// </summary>
        public bool IsEditingTask
        {
            get { return _isEditingTask; }
            set { _isEditingTask = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// The category to be displayed on the edit/new task pane
        /// </summary>
        public string CurrentCategory
        {
            get { return _currentCategory; }
            set
            {
                _currentCategory = value;
                OnPropertyChanged();
            }
        }

        public PresentationTask OriginalTask
        {
            get;
            set;
        }

        private InAppNotification MessagePump;
        private const int MessageDuration = 3000;

        #endregion Properties

        /// <summary>
        /// Constructor / Initialization of tasks.
        /// </summary>
        public BoardViewModel(PresentationBoard board, IAdaptiveClient<IServiceManifest> dataProvider, InAppNotification messagePump)
        {
            Board = board;
            DataProvider = dataProvider;
            MessagePump = messagePump;
        
            CurrentTask = new PresentationTask(new TaskDTO());
            NewTaskCommand = new RelayCommand<ColumnTag>(NewTaskCommandHandler, () => true); // CanExecuteChanged is not working 
            EditTaskCommand = new RelayCommand<int>(EditTaskCommandHandler, () => true);
            SaveTaskCommand = new RelayCommand(SaveTaskCommandHandler, () => true);
            DeleteTaskCommand = new RelayCommand<int>(DeleteTaskCommandHandler, () => true);
            DeleteTagCommand = new RelayCommand<string>(DeleteTagCommandHandler, () => true);
            CancelEditCommand = new RelayCommand(CancelEditCommandHandler, () => true);

            ColorKeys = new ObservableCollection<ComboBoxItem>();
            ColorKeys.Add(new ComboBoxItem { Content = "High" });
            ColorKeys.Add(new ComboBoxItem { Content = "Normal" });
            ColorKeys.Add(new ComboBoxItem { Content = "Low" });

            if (Board.Tasks != null && board.Tasks.Any())   // hack
                foreach (PresentationTask task in Board.Tasks)
                    task.ColorKeyComboBoxItem = GetComboBoxItemForColorKey(task.ColorKey);
        }

        #region Functions


        public void NewTaskCommandHandler(ColumnTag tag)
        {
            PaneTitle = "New Task";
            string category = tag?.Header?.ToString();
            CurrentTask = new PresentationTask(new TaskDTO() { Category = category }) { Board = Board, BoardId = Board.ID,  ColorKeyComboBoxItem = ColorKeys[1] };
            OriginalTask = null; 
            IsEditingTask = true;
        }

        public void EditTaskCommandHandler(int taskID)
        {
            PaneTitle = "Edit Task";
            CurrentTask = Board.Tasks.First(x => x.ID == taskID);
            IsEditingTask = true;
            // clone a copy of CurrentTask so we can restore if user cancels
            OriginalTask = new PresentationTask(CurrentTask.To_TaskDTO());
        }

        public void SaveTaskCommandHandler()
        {
            IsEditingTask = false;

            if (CurrentTask == null)
                return;

            TaskDTO dto = CurrentTask.To_TaskDTO();
            dto.ColorKey = ((ComboBoxItem)CurrentTask.ColorKeyComboBoxItem)?.Content.ToString() ?? "Normal"; // hack
            
            bool isNew = dto.Id == 0;

            if (isNew)
            {
                dto.ColumnIndex = Board.Tasks?.Where(x => x.Category == dto.Category).Count() ?? 0;
                dto.DateCreated = DateTime.Now.ToString();
            }
            dto.Id = DataProvider.Call(x => x.TaskServices.SaveTask(dto)).Entity.Id;


            if (isNew)
            {
                CurrentTask.ID = dto.Id;
                CurrentTask.ColumnIndex = dto.ColumnIndex;
                Board.Tasks.Add(CurrentTask);
            }
            
            MessagePump.Show("Task was saved successfully", MessageDuration);
        }

        public void DeleteTaskCommandHandler(int taskID)
        {
            PresentationTask task = Board.Tasks.First(x => x.ID == taskID);
            RowOpResult result = DataProvider.Call(x => x.TaskServices.DeleteTask(taskID));

            if (result.Success)
            {
                Board.Tasks.Remove(task);
                CurrentTask = Board.Tasks.LastOrDefault();
                int startIndex = task.ColumnIndex;

                // Calling OrderBy after Where, reordering a whole collection prior to filter is high overhead
                // If we do not sort by ColumnIndex, the tasks in Board.Tasks will be in unsorted order when assigning startIndex 

                // Questionable issue:
                //  Sometimes the task index value for a task in Board.Tasks are wrong, but correct in db (shouldn't be because of this though)
                //      Ex: We have a task named t2 which is index 2 in db (expected), but index 4 or something in Board.Tasks at time of deletion
                //      - I've only noticed it when moving a task and then deleting, sometimes... Possible binding issue when changing property, since db is correct? 
                //  otherTask.ColumnIndex -=1 works without OrderBy, but has the problem above
                // Fix: If an index in the db gets messed up, moving one card in the column fixes the whole column. Low severity.
                foreach (PresentationTask otherTask in Board.Tasks.Where(x => x.Category == task.Category && x.ColumnIndex > task.ColumnIndex).OrderBy(x => x.ColumnIndex)) 
                {
                    otherTask.ColumnIndex = startIndex++;
                    //otherTask.ColumnIndex -= 1;
                    UpdateCardIndex(otherTask.ID, otherTask.ColumnIndex);
                }
                MessagePump.Show("Task deleted from board successfully", MessageDuration);
            }
            else
                MessagePump.Show("Task failed to be deleted. Please try again or restart the application.", MessageDuration);
        }

        public void DeleteTagCommandHandler(string tag)
        {
            if (CurrentTask == null)
            {
                MessagePump.Show("Tag failed to be deleted.  CurrentTask is null. Please try again or restart the application.", MessageDuration);
                return;
            }
            CurrentTask.Tags.Remove(tag);
            MessagePump.Show("Tag deleted successfully", MessageDuration);
        }

        public void CancelEditCommandHandler()
        {
            IsEditingTask = false;

            if (OriginalTask == null)
                return;
            // roll back changes to CurrentTask
           else
            {
                int index = Board.Tasks.IndexOf(CurrentTask);
                Board.Tasks.Remove(CurrentTask);
                CurrentTask = new PresentationTask(OriginalTask.To_TaskDTO());
                Board.Tasks.Insert(index, CurrentTask);
            }
        }

        /// <summary>
        /// Inserts a tag to the current task's tag collection.
        /// </summary>
        /// <param name="tag"></param>
        /// <returns>A bool containing whether the tag was successfully added or not.</returns>
        public bool AddTag(string tag)
        {
            bool result = false;

            if (CurrentTask == null)
            {
                MessagePump.Show("Tag failed to be deleted.  CurrentTask is null. Please try again or restart the application.", MessageDuration);
                return result;
            }

            if (CurrentTask.Tags.Contains(tag))
                MessagePump.Show("Tag already exists", 3000);
            else
            {
                CurrentTask.Tags.Add(tag);
                MessagePump.Show($"Tag {tag} added successfully", 3000);
                result = true;
            }
            return result;
        }


        /// <summary>
        /// Updates the selected card category and column index after dragging it to
        /// a new column.
        /// </summary>
        /// <param name="targetCategory"></param>
        /// <param name="selectedCardModel"></param>
        /// <param name="targetIndex"></param>
        public void UpdateCardColumn(string targetCategory, PresentationTask selectedCardModel, int targetIndex)
        {
            TaskDTO task = selectedCardModel.To_TaskDTO();
            task.Category = targetCategory;
            task.ColumnIndex = targetIndex;
            DataProvider.Call(x => x.TaskServices.UpdateColumnData(task));
        }

        /// <summary>
        /// Updates a specific card index in the database when reordering after dragging a card.
        /// </summary>
        /// <param name="iD"></param>
        /// <param name="currentCardIndex"></param>
        internal void UpdateCardIndex(int iD, int currentCardIndex)
        {
            DataProvider.Call(x => x.TaskServices.UpdateCardIndex(iD, currentCardIndex));
        }

        private ComboBoxItem GetComboBoxItemForColorKey(string colorKey) => ColorKeys.FirstOrDefault(x => x.Content.ToString() == colorKey);

        public void LoadTasksForBoard(int ID)
        {
            
            IEnumerable<TaskDTO> tasks = DataProvider.Call(t => t.TaskServices.GetTasks().Where(x => x.BoardId == ID).OrderBy(x => x.Category).ThenBy(x => x.ColumnIndex));
            Board.Tasks.Clear();
            foreach (TaskDTO task in tasks)
                Board.Tasks.Add(new PresentationTask(task));
        }

        #endregion
    }
}
