using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using TrelloNet;
using WPF.JoshSmith.ServiceProviders.UI;
using WorkHorse.Annotations;
using Action = System.Action;

namespace WorkHorse
{
    public partial class MainWindow : Window
    {
        private const string trelloUserTokenSettingName = "trello_user_token";
        private static string settingsFile = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "/trello-settings.conf";
            
        public MainWindow()
        {            
            InitializeComponent();
            this.Title = "Work Horse";

            this.ControlBoxAddItem.Click += ControlBoxAddItem_Click;

            new ListViewDragDropManager<TaskItem>(this.TaskListBox);
            this.TaskListBox.MouseRightButtonUp += (sender, args) =>
                                                       {
                                                           var clickedTaskItem =
                                                               (TaskItem)
                                                               ((FrameworkElement) args.OriginalSource).DataContext;

                                                           var editDlg = new AddItemDialog();
                                                           editDlg.TaskTitle.Text = clickedTaskItem.TaskName;
                                                           editDlg.TaskDescription.Text = clickedTaskItem.TaskDescription;

                                                           editDlg.OkButton.Click += (o, eventArgs) =>
                                                                                         {
                                                                                             clickedTaskItem
                                                                                                 .TaskDescription =
                                                                                                 editDlg.TaskDescription
                                                                                                        .Text;
                                                                                             clickedTaskItem
                                                                                                 .TaskName =
                                                                                                 editDlg.TaskTitle.Text;
                                                                                             
                                                                                             editDlg.Close();
                                                                                         };
                                                           editDlg.ShowDialog();
                                                       };

            TaskList = new ObservableCollection<TaskItem>();

            TaskList.CollectionChanged += (sender, args) =>
                                              {
                                                  if (!initialLoadComplete) return;
                                                 
                                                  if (args.Action == NotifyCollectionChangedAction.Remove)
                                                  {
                                                      foreach (var oldItem in args.OldItems.Cast<TaskItem>())
                                                      {
                                                          trelloService.MoveCardToDone(oldItem.Card);                                                          
                                                      }                                                      
                                                  }

                                                  if (args.Action == NotifyCollectionChangedAction.Move)
                                                  {
                                                      if (TaskList.Count == 1) return;

                                                      var taskItem = args.OldItems.Cast<TaskItem>().Single();

                                                      double newPos;
                                                      if (args.NewStartingIndex == 0)
                                                      {
                                                          newPos = TaskList[1].Card.Pos/2;
                                                      }
                                                      else if (args.NewStartingIndex == TaskList.Count - 1)
                                                      {
                                                          newPos = TaskList[args.NewStartingIndex - 1].Card.Pos + 10;
                                                      }
                                                      else
                                                      {
                                                          newPos = (TaskList[args.NewStartingIndex - 1].Card.Pos +
                                                                    TaskList[args.NewStartingIndex + 1].Card.Pos)/2;
                                                      }

                                                      trelloService.ChangeCardPos(taskItem.Card, newPos);                                                      
                                                  }
                                              };

            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(onFinishedLoading));            
        }

        private void onFinishedLoading()
        {
            settings = readSettings();
            
            trelloService = new TrelloService();

            var authToken = trelloService.Authorise(authoriseTrello);
            settings[trelloUserTokenSettingName] = authToken;

            flushSettings(settings);

            var currentCards = trelloService.CurrentCards();
            currentCards.ContinueWith(cardsLoaded);

            initialLoadComplete = true;
        }

        private void cardsLoaded(Task<IEnumerable<Card>> task)
        {
            var currentCards = task.Result;
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => currentCards.OrderBy(_ => _.Pos)
                                                                                                .Select(card => new TaskItem(TaskList, card, card.Name, trelloService)
                                                                                                                    {
                                                                                                                        TaskDescription = card.Desc
                                                                                                                    })
                                                                                                .ToList()
                                                                                                .ForEach(TaskList.Add)));
            
        }

        private string authoriseTrello(Uri authUrl)
        {
            if (settings.ContainsKey(trelloUserTokenSettingName))
            {
                var existingToken = settings[trelloUserTokenSettingName];
                settings.Remove(trelloUserTokenSettingName);
                return existingToken;
            }

            var trelloAuthDialog = new TrelloAuthDialog();
            trelloAuthDialog.TwitterAuthUrl.NavigateUri = authUrl;
            trelloAuthDialog.TwitterAuthUrl.RequestNavigate += (sender, args) =>
                                                                    {
                                                                        Process.Start(
                                                                            new ProcessStartInfo(args.Uri.AbsoluteUri));
                                                                        args.Handled = true;
                                                                    };

            string authFromUser = null;
            trelloAuthDialog.OkButton.Click += (sender, args) =>
                                                    {
                                                        authFromUser = trelloAuthDialog.TwitterAuthText.Text;
                                                        trelloAuthDialog.Close();
                                                    };
            trelloAuthDialog.ShowDialog();

            return authFromUser;
        }

        private void flushSettings(IDictionary<string, string> dictionary)
        {
            var settingBuilder = new StringBuilder();
            foreach (var setting in dictionary)
            {
                settingBuilder.AppendFormat("{0}:{1}", setting.Key, setting.Value);
            }

            File.WriteAllText(settingsFile, settingBuilder.ToString());
        }

        private static IDictionary<string, string> readSettings()
        {
            var settings = new Dictionary<string, string>();
            
            if (!File.Exists(settingsFile))
            {
                File.WriteAllText(settingsFile, "");
            }

            var readAllLines = File.ReadAllLines(settingsFile);
            foreach (var setting in readAllLines)
            {
                var settingParts = setting.Split(':');
                if (settingParts.Length != 2) continue;

                settings[settingParts[0]] = settingParts[1];                
            }

            return settings;
        }

        private void ControlBoxAddItem_Click(object sender, RoutedEventArgs e)
        {
            var addItemDialog = new AddItemDialog();
            addItemDialog.OkButton.Click += (okSender, args) =>
                                                {
                                                    var card = trelloService.CreateCard(addItemDialog.TaskTitle.Text);
                                                    TaskList.Add(new TaskItem(TaskList, card, addItemDialog.TaskTitle.Text, trelloService){ TaskDescription = addItemDialog.TaskDescription.Text});                                                    
                                                    addItemDialog.Close();
                                                };
            addItemDialog.ShowDialog();            
        }

        public ObservableCollection<TaskItem> TaskList
        {
            get { return (ObservableCollection<TaskItem>) GetValue(TaskListProperty); }
            set { SetValue(TaskListProperty, value);}
        }

        public static readonly DependencyProperty TaskListProperty =
            DependencyProperty.Register("TaskList", typeof (ObservableCollection<TaskItem>), typeof (MainWindow),
                                        new UIPropertyMetadata(null));

        private IDictionary<string, string> settings;
        private bool initialLoadComplete = false;
        private TrelloService trelloService;


        public class TaskItem : INotifyPropertyChanged
        {
            private string _taskName;
            private readonly TrelloService trelloService;

            private string _taskDescription;
            private bool _isDone;

            public TaskItem(ObservableCollection<TaskItem> taskList, Card card, string taskName, TrelloService trelloService)
            {
                Card = card;
                TaskDoneCmd = new TaskDoneCommand(this, taskList);
                _taskName = taskName;
                this.trelloService = trelloService;
            }

            public string TaskName
            {
                get { return _taskName; }
                set
                {
                    _taskName = value;
                    OnPropertyChanged();
                    trelloService.ChangeName(Card, _taskName);
                }
            }

            public bool IsDone
            {
                get { return _isDone; }
                set
                {
                    _isDone = value;
                    OnPropertyChanged();
                    if (value)
                    {
                        trelloService.MoveCardToDone(Card);
                    }
                }
            }

            public string TaskDescription
            {
                get { return _taskDescription; }
                set
                {
                    _taskDescription = value;
                    OnPropertyChanged();
                    trelloService.ChangeDescription(Card, _taskDescription);
                }
            }

            public Card Card { get; private set; }

            public TaskDoneCommand TaskDoneCmd { get; set; }

            public Visibility TaskDescriptionVisibility 
            { 
                get
                {
                    return !string.IsNullOrEmpty(TaskDescription)
                           ? Visibility.Visible
                           : Visibility.Hidden;
                } 
            }

            public event PropertyChangedEventHandler PropertyChanged;

            [NotifyPropertyChangedInvocator]
            protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
            {
                var handler = PropertyChanged;
                if (handler != null) handler(this, new PropertyChangedEventArgs(propertyName));
            }

            public class TaskDoneCommand : ICommand
            {
                private readonly TaskItem _list;
                private readonly ObservableCollection<TaskItem> _taskList;

                public TaskDoneCommand(TaskItem list, ObservableCollection<TaskItem> taskList)
                {
                    _list = list;
                    _taskList = taskList;
                }

                public bool CanExecute(object parameter)
                {
                    return true;
                }

                public void Execute(object parameter)
                {
                    _taskList.Remove(_list);                    
                }

                public event EventHandler CanExecuteChanged;
            }
        }
    }
}


