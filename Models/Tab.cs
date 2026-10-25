using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BinderApp.Models
{
    /// <summary>
    /// Представляет вкладку (категорию биндов, например "ФСБ", "МВД").
    /// </summary>
    public class Tab : INotifyPropertyChanged
    {
        private string _id = Guid.NewGuid().ToString();
        private string _name = "Новая вкладка";
        private ObservableCollection<Bind> _binds = new();

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

        public ObservableCollection<Bind> Binds
        {
            get => _binds;
            set { _binds = value ?? new ObservableCollection<Bind>(); OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
    }

    /// <summary>
    /// Корневой контейнер структуры binder.json.
    /// </summary>
    public class BinderConfig
    {
        public ObservableCollection<Tab> Tabs { get; set; } = new();
    }
}
