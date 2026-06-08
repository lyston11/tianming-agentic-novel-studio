using TM.Framework.UI.Workspace.Services.Spec;

namespace TM.Services.Framework.AI.SemanticKernel.Plugins
{
    public partial class AutoRewriteEngine
    {
        #region 常量

        public const int MaxRewriteAttempts = 2;

        public const int MaxFailureReasonsPerRewrite = 5;

        #endregion

        #region 润色配置

        private static int GetPolishMode(CreativeSpec? spec)
            => CreativeSpec.GetEffectivePolishMode(spec);

        private static int GetPolishModel(CreativeSpec? spec)
            => CreativeSpec.GetEffectivePolishModel(spec);

        #endregion

        #region 构造函数

        public AutoRewriteEngine() { }

        #endregion

    }
}
