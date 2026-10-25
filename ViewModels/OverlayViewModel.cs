using BinderApp.Models;
using BinderApp.Services;

namespace BinderApp.ViewModels
{
    /// <summary>
    /// ViewModel оверлей-окна: отображает активный бинд и текущий шаг.
    /// </summary>
    public class OverlayViewModel : ViewModelBase
    {
        private readonly BindExecutor _executor;

        private string _bindName = string.Empty;
        private string _stepLabel = string.Empty;
        private string _stepPreview = string.Empty;
        private bool _isVisible = false;

        public OverlayViewModel(BindExecutor executor)
        {
            _executor = executor;
            _executor.StepChanged += OnStepChanged;
            _executor.BindFinished += OnBindFinished;

            StopBindCommand = new RelayCommand(StopBind);
        }

        public string BindName
        {
            get => _bindName;
            set => SetProperty(ref _bindName, value);
        }

        public string StepLabel
        {
            get => _stepLabel;
            set => SetProperty(ref _stepLabel, value);
        }

        public string StepPreview
        {
            get => _stepPreview;
            set => SetProperty(ref _stepPreview, value);
        }

        public bool IsVisible
        {
            get => _isVisible;
            set => SetProperty(ref _isVisible, value);
        }

        public System.Windows.Input.ICommand StopBindCommand { get; }

        private void OnStepChanged(Bind bind, int stepIndex)
        {
            BindName = bind.Name;
            StepLabel = $"Шаг {stepIndex + 1}/{bind.Steps.Count}";

            string text = stepIndex < bind.Steps.Count ? bind.Steps[stepIndex] : string.Empty;
            StepPreview = text.Length > 55 ? text.Substring(0, 55) + "…" : text;

            IsVisible = true;
        }

        private void OnBindFinished()
        {
            IsVisible = false;
            BindName = string.Empty;
            StepLabel = string.Empty;
            StepPreview = string.Empty;
        }

        private void StopBind(object? _)
        {
            _executor.Stop(notify: true);
        }
    }
}
