using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BinderApp.Models
{
    /// <summary>
    /// Представляет профиль пользователя.
    /// </summary>
    public class Profile : INotifyPropertyChanged
    {
        private string _id = "default";
        private string _name = "Основной";

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

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
    }
}
