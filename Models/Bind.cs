using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BinderApp.Models
{
    /// <summary>
    /// Модель бинда RP-команд.
    /// Совместима с форматом binder.json: "name", "hotkey", "enabled", "steps".
    /// </summary>
    public class Bind : INotifyPropertyChanged
    {
        private string _id = Guid.NewGuid().ToString();
        private string _name = "Новый бинд";
        private string _hotkey = "Ctrl+1";
        private bool _enabled = true;
        private ObservableCollection<string> _steps = new();

        public Bind()
        {
            _steps.CollectionChanged += Steps_CollectionChanged;
        }

        public string Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public string Hotkey
        {
            get => _hotkey;
            set { _hotkey = value; OnPropertyChanged(); }
        }

        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; OnPropertyChanged(); }
        }

        public ObservableCollection<string> Steps
        {
            get => _steps;
            set
            {
                if (ReferenceEquals(_steps, value)) return;
                _steps.CollectionChanged -= Steps_CollectionChanged;
                _steps = value ?? new ObservableCollection<string>();
                _steps.CollectionChanged += Steps_CollectionChanged;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StepsCount));
            }
        }

        public int StepsCount => Steps?.Count ?? 0;

        private void Steps_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(StepsCount));
            OnPropertyChanged(nameof(Steps));
        }

        public Bind Clone()
        {
            var clone = new Bind
            {
                Id = Guid.NewGuid().ToString(),
                Name = this.Name,
                Hotkey = this.Hotkey,
                Enabled = this.Enabled,
                Steps = new ObservableCollection<string>()
            };

            foreach (var step in this.Steps)
            {
                clone.Steps.Add(step);
            }

            return clone;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
