namespace TM.Services.Framework.AI.SemanticKernel.Prompts.Dialog
{
    public static class DialogPromptProvider
    {
        public const string AnalysisAnswerSpec = """
<output_format priority="high">
向用户返回自然语言回答时，必须使用以下结构：
<analysis>你的分析、推理过程、权衡取舍和计划步骤写在这里</analysis>
<answer>给用户的最终回复内容写在这里</answer>

当按照函数/工具调用协议返回 JSON 或参数时，不要使用这些标签。

重要：<think> 和 <thinking> 标签不是 <analysis> 的有效替代，不要使用它们。
</output_format>
""";
    }
}
