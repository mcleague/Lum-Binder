using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using BinderApp.Models;

namespace BinderApp.ViewModels
{
    /// <summary>
    /// ViewModel окна добавления/редактирования бинда.
    /// </summary>
    public class BindEditViewModel : ViewModelBase
    {
        private readonly Bind _bind;
        private bool _isCapturing;
        private string _captureButtonText = "Записать комбинацию";
        private BindStepViewModel? _selectedStep;

        public BindEditViewModel(Bind bind)
        {
            _bind = bind;
            Steps = new ObservableCollection<BindStepViewModel>(
                bind.Steps.Select(step => new BindStepViewModel(step)));

            AddStepCommand = new RelayCommand(AddStep);
            RemoveStepCommand = new RelayCommand(RemoveStep, () => SelectedStep != null);
            MoveUpCommand = new RelayCommand(MoveUp, () => SelectedStep != null && Steps.IndexOf(SelectedStep) > 0);
            MoveDownCommand = new RelayCommand(MoveDown, () => SelectedStep != null && Steps.IndexOf(SelectedStep) < Steps.Count - 1);
            ToggleCaptureCommand = new RelayCommand(ToggleCapture);
            ResetHotkeyCommand = new RelayCommand(ResetHotkey);
        }

        public string BindName
        {
            get => _bind.Name;
            set { _bind.Name = value; OnPropertyChanged(); }
        }

        public string Hotkey
        {
            get => _bind.Hotkey;
            set { _bind.Hotkey = value; OnPropertyChanged(); }
        }

        public bool Enabled
        {
            get => _bind.Enabled;
            set { _bind.Enabled = value; OnPropertyChanged(); }
        }

        public ObservableCollection<BindStepViewModel> Steps { get; }
        public Bind EditedBind => _bind;
        public string ValidationMessage
        {
            get => _validationMessage;
            set => SetProperty(ref _validationMessage, value);
        }

        private string _validationMessage = string.Empty;

        public BindStepViewModel? SelectedStep
        {
            get => _selectedStep;
            set
            {
                SetProperty(ref _selectedStep, value);
                ((RelayCommand)RemoveStepCommand).RaiseCanExecuteChanged();
                ((RelayCommand)MoveUpCommand).RaiseCanExecuteChanged();
                ((RelayCommand)MoveDownCommand).RaiseCanExecuteChanged();
            }
        }

        public bool IsCapturing
        {
            get => _isCapturing;
            set { SetProperty(ref _isCapturing, value); }
        }

        public string CaptureButtonText
        {
            get => _captureButtonText;
            set { SetProperty(ref _captureButtonText, value); }
        }

        public ICommand AddStepCommand { get; }
        public ICommand RemoveStepCommand { get; }
        public ICommand MoveUpCommand { get; }
        public ICommand MoveDownCommand { get; }
        public ICommand ToggleCaptureCommand { get; }
        public ICommand ResetHotkeyCommand { get; }

        private void AddStep()
        {
            Steps.Add(new BindStepViewModel("Новый шаг команды"));
        }

        private void RemoveStep()
        {
            if (SelectedStep != null)
            {
                int idx = Steps.IndexOf(SelectedStep);
                Steps.Remove(SelectedStep);
                SelectedStep = idx < Steps.Count ? Steps[idx] : (Steps.Count > 0 ? Steps[^1] : null);
            }
        }

        private void MoveUp()
        {
            if (SelectedStep != null)
            {
                int idx = Steps.IndexOf(SelectedStep);
                if (idx > 0)
                {
                    Steps.Move(idx, idx - 1);
                }
            }
        }

        private void MoveDown()
        {
            if (SelectedStep != null)
            {
                int idx = Steps.IndexOf(SelectedStep);
                if (idx < Steps.Count - 1)
                {
                    Steps.Move(idx, idx + 1);
                }
            }
        }

        private void ToggleCapture()
        {
            IsCapturing = !IsCapturing;
            CaptureButtonText = IsCapturing ? "Нажмите клавиши..." : "Записать комбинацию";
        }

        private void ResetHotkey()
        {
            Hotkey = "Не назначен";
            IsCapturing = false;
            CaptureButtonText = "Записать комбинацию";
        }

        public void ProcessKeyDown(KeyEventArgs e)
        {
            if (!IsCapturing) return;
            e.Handled = true;

            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LeftShift || key == Key.RightShift ||
                key == Key.LWin || key == Key.RWin)
            {
                return;
            }

            var parts = new System.Collections.Generic.List<string>();
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");

            string keyName = key.ToString();
            if (keyName.StartsWith("NumPad")) keyName = "Num" + keyName.Substring(6);
            if (keyName.Length == 2 && keyName[0] == 'D' && char.IsDigit(keyName[1])) keyName = keyName.Substring(1);

            parts.Add(keyName);

            Hotkey = string.Join("+", parts);
            IsCapturing = false;
            CaptureButtonText = "Записать комбинацию";
        }

    }

    public class BindStepViewModel : ViewModelBase
    {
        private string _text;

        public BindStepViewModel(string text)
        {
            _text = text;
        }

        public string Text
        {
            get => _text;
            set => SetProperty(ref _text, value);
        }
    }
}
