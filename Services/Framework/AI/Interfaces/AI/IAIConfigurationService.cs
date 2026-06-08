using System;
using System.Collections.Generic;
using TM.Services.Framework.AI.Core;

namespace TM.Services.Framework.AI.Interfaces.AI
{
    public interface IAIConfigurationService
    {
        UserConfiguration? GetActiveConfiguration();

        void SetActiveConfiguration(UserConfiguration configuration);

        IReadOnlyList<UserConfiguration> GetAllConfigurations();

        void AddConfiguration(UserConfiguration config);

        void UpdateConfiguration(UserConfiguration config);

        void DeleteConfiguration(string configId);

        event EventHandler? ConfigurationsChanged;
    }
}
