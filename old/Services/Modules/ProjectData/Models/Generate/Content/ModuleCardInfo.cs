using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.Generate.Content
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class ModuleCardInfo : INotifyPropertyChanged
    {
        private bool _isEnabled;
        private string _statusText = "最新";
        private bool _hasChanges;

        [JsonPropertyName("ModulePath")]
        public string ModulePath { get; set; } = string.Empty;

        [JsonPropertyName("ModuleType")]
        public string ModuleType { get; set; } = string.Empty;

        [JsonPropertyName("SubModuleName")]
        public string SubModuleName { get; set; } = string.Empty;

        [JsonPropertyName("DisplayName")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("Icon")]
        public string Icon { get; set; } = "Icon.Folder";

        [JsonPropertyName("ItemCountText")]
        public string ItemCountText { get; set; } = "0项";

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

        public string StatusText
        {
            get => _statusText;
            set
            {
                if (_statusText != value)
                {
                    _statusText = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool HasChanges
        {
            get => _hasChanges;
            set
            {
                if (_hasChanges != value)
                {
                    _hasChanges = value;
                    OnPropertyChanged();
                    StatusText = _hasChanges ? "⚠有变更" : "✓最新";
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
