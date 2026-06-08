using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TM.Services.Framework.AI.SemanticKernel
{
    public partial class UIMessageItem : INotifyPropertyChanged
    {
        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        #endregion
    }
}
