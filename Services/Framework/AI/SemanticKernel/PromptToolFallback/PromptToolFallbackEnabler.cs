using TM.Framework.UI.Workspace.RightPanel.Modes;
using TM.Services.Framework.AI.Core.Capabilities;

namespace TM.Services.Framework.AI.SemanticKernel.PromptToolFallback
{
    public static class PromptToolFallbackEnabler
    {
        public static bool ShouldUseFallback(ResolvedCapability resolved, ChatMode mode)
        {
            if (resolved == null) return false;

            if (!RequiresTools(mode)) return false;

            if (resolved.Tools.SupportsNativeToolUse) return false;

            return true;
        }

        public static bool RequiresTools(ChatMode mode)
        {
            return mode is ChatMode.Edit or ChatMode.Plan or ChatMode.Agent;
        }
    }
}
