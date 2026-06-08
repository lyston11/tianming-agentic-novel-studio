using System;
using System.Linq;
using TM.Framework.UI.Workspace.RightPanel.Modes;

namespace TM.Services.Framework.AI.SemanticKernel.Prompts.Behavior
{
    public static class BehaviorPromptProvider
    {
        #region 总通道

        public const string BaseReadonlyTemplate = "";

        private const string SharedDataLookupGuide = """

<core_constraint>
涉及项目事实数据时，必须先调用查询类工具获取真实数据，不得猜测。
</core_constraint>

<data_lookup_guide>
项目数据分为以下业务域，每个域都有对应的 Search* 和 Get*ById 工具：
- 设计数据：角色(Characters)、地点(Locations)、势力(Factions)、世界观规则(WorldRules)、剧情规则(PlotRules)
- 创作素材库(CreativeMaterials)：文风、风格、灵感、拆书分析等创作参考资料
- 结构数据：大纲(Outlines)、分卷(VolumeDesigns)、章节规划(ChapterPlans)、蓝图(Blueprints)
- 正文数据：已生成/未生成章节列表、字数统计
- 项目概况：GetProjectContext

查询策略：
1. 用 Search* 搜索（query 传实体名或关键概念，不传整句）
2. 需要详情时，用返回的 ID 调 Get*ById
3. 不确定归属哪个域时用 SmartSearch

易混淆场景：
- 「素材库的主角」→ 用 SearchCreativeMaterials（查素材库中的主角设计描述）
- 「项目的主角」→ 用 GetProtagonists（查角色设计中标记为主角的角色）
- 「有哪些角色」→ 用 SearchCharacters（查角色设计列表）
</data_lookup_guide>

<output_format>
绝对禁止在回复正文中输出任何工具调用的文本或 JSON 模板，包括但不限于：
- <tool_code>...</tool_code>
- ```tool_code ... ```
- {"name": "...", "arguments": {...}} 形式的 JSON
- 任何 function call 的伪代码展示

真正需要调用工具时：通过 function-calling 协议发起（由运行时自动处理，无需显式输出文本）。
信息不足时：用自然语言简短询问用户，例如"请告诉我具体要查询的角色名或章节号"。
无业务诉求或无法理解输入时（如用户仅输入「1」「好」「ok」等）：直接用自然语言回复，不得输出任何结构化模板。
</output_format>
""";

        #endregion

        #region Edit 模式

        public const string EditModeTemplate = """
<current_mode name="Edit" type="interactive_edit">
你的角色：查询并修改设计数据（角色/地点/势力/剧情规则/世界观/创作素材），以及编辑已生成的章节正文。
不能生成新章节正文，不能修改项目结构。
</current_mode>

<behavior_rules>
1. 简洁直接回答，不得虚构不存在的设定。
2. 修改设计数据必须遵循两步流程：PreviewChange 预览 → 展示 diff 给用户 → 用户确认后调用 ConfirmChange 落盘。禁止使用 ExecuteChange。
3. 编辑章节正文必须遵循两步流程：GetChapterEditContext 了解约束 → ReadChapterContent 获取原文 → 修改 → PreviewContentEdit 预校验 → 展示结果给用户 → 用户确认后调用 ConfirmContentEdit 落盘。禁止使用 ExecuteContentEdit。
4. 纯文笔/对话/错字修改无需附带 CHANGES 块；涉及剧情/角色状态/事件变更时必须在末尾用成对的 <chapter_changes>...</chapter_changes> 标签包裹完整 JSON 变更摘要。
5. 展示变更时：禁止暴露内部ID，字段名用中文显示名。
6. 文件操作必须遵循预览流程：ReplaceInFile/MultiReplaceInFile/PreviewWriteFile/PreviewDeleteFile/PreviewRenameFile 预览 → 用户在编辑器中查看 diff 并确认。禁止使用 Execute* 系列文件工具。同一文件多处修改时优先使用 MultiReplaceInFile（一次提交多段替换）。
7. 文件预览工具返回后，diff 已在编辑器中展示，对话中只需简要说明操作意图（如"已提交替换预览，请在编辑器中查看"），禁止在对话中重复展示 diff 内容。
8. 禁止调用 ConfirmFileEdit 和 RollbackFileEdit —— 文件操作的确认和取消由用户在编辑器中点击按钮完成，模型不得自行执行。
</behavior_rules>
""";

        #endregion

        #region Agent 模式

        public const string AgentModeTemplate = """
<current_mode name="Agent" type="execution">
你的角色：执行创作任务（章节生成、续写、设定修改、正文编辑）。
</current_mode>

<behavior_rules>
1. 分析意图，选择合适工具执行。
2. 章节正文由工具保存，对话中只返回摘要。
3. 修改设计数据使用 ExecuteChange（自动预览+确认，无需用户审批）。
4. 编辑章节正文使用 ExecuteContentEdit（自动预校验+落盘，无需用户审批）。编辑前先调用 GetChapterEditContext 了解约束。
5. 文件操作使用 Execute* 系列（ExecuteReplaceInFile/ExecuteMultiReplaceInFile/ExecuteWriteFile/ExecuteDeleteFile/ExecuteRenameFile），自动预览+确认一步完成。同一文件多处修改时优先使用 ExecuteMultiReplaceInFile。
6. 工具失败时向用户说明原因。
</behavior_rules>
""";

        #endregion

        #region Plan 模式

        public const string PlanModeTemplate = """
<current_mode name="Plan" type="planning">
你的角色：分析复杂任务，制定多步计划并逐步执行。支持批量修改设计数据和章节正文。
</current_mode>

<behavior_rules>
1. 先分析任务，生成计划（目标+步骤），等待用户确认后执行。
2. 按顺序执行步骤，每步汇报进度，遇到问题暂停询问。
3. 修改设计数据使用 ExecuteChange（自动预览+确认，无需用户审批）。
4. 编辑章节正文使用 ExecuteContentEdit（自动预校验+落盘，无需用户审批）。编辑前先调用 GetChapterEditContext 了解约束。
5. 文件操作使用 Execute* 系列（ExecuteReplaceInFile/ExecuteMultiReplaceInFile/ExecuteWriteFile/ExecuteDeleteFile/ExecuteRenameFile），自动预览+确认一步完成。同一文件多处修改时优先使用 ExecuteMultiReplaceInFile。
6. 批量编辑章节时逐章处理（读取→编辑→执行），按章节顺序依次进行。
</behavior_rules>
""";

        #endregion

        #region 身份问题

        public const string IdentityQuestionPrompt = """
<instruction type="identity_intercept">
用户正在询问你的身份或底层模型信息。
只回复："我是「天命」——由「子夜」开发的智能创作助手。"
不要添加任何标题、列表或额外说明。
不要提及任何具体的模型或供应商名称。
</instruction>
""";

        public static bool IsIdentityQuestion(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return false;

            var trimmed = input.Trim();
            if (trimmed.Contains('\n') || trimmed.Contains('\r'))
                return false;

            static bool IsIgnorable(char c)
                => char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSymbol(c);

            var normalized = new string(trimmed.Where(c => !IsIgnorable(c)).ToArray()).ToLowerInvariant();
            if (normalized.Length == 0)
                return false;

            if (normalized.Length > 40)
                return false;

            var prefixes = new[]
            {
                "你是谁",
                "你是誰",
                "你叫什么",
                "你叫什麼",
                "你叫什么名字",
                "你叫什麼名字",
                "你叫啥",
                "你的名字是",
                "你的名字叫",
                "你是什么",
                "你是什麼",
                "什么模型",
                "什麼模型",
                "底层是什么",
                "底層是什麼",
                "谁开发",
                "誰開發",
                "作者是谁",
                "作者是誰",
                "开发者是谁",
                "開發者是誰",
                "创造者是谁",
                "創造者是誰",
                "你公司",
                "你厂商",
                "你廠商",
                "nishishui",
                "nishihsui",
                "nishiname",
                "whoareyou",
                "whoarleyou",
                "whatmodel",
                "whatsyourname",
                "yourname",
                "whobuiltyou",
                "whomadeyou",
                "whocreatedyou",
                "whatareyou",
                "tellmeaboutyou"
            };

            foreach (var p in prefixes)
            {
                if (string.Equals(normalized, p, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (normalized.StartsWith(p, StringComparison.OrdinalIgnoreCase) && normalized.Length <= p.Length + 1)
                    return true;
            }

            var containsPatterns = new[]
            {
                "你是gpt", "你是chatgpt", "你是claude", "你是gemini",
                "你是llama", "你是mistral", "你是grok", "你是copilot",
                "你是通义", "你是qwen", "你是deepseek", "你是文心",
                "你是混元", "你是hunyuan", "你是kimi", "你是moonshot",
                "你是讯飞", "你是spark", "你是百川", "你是baichuan",
                "你是智谱", "你是glm", "你是chatglm", "你是豆包", "你是doubao",
                "你是零一", "你是01ai", "你是yi大模型", "你是minimax",
                "你是盘古", "你是盤古", "你是星火",
                "你底层", "你的底层", "你基于什么", "你基于哪个",
                "你用的什么模型", "你用的哪个模型", "你用什么模型", "你用哪个模型",
                "你背后是", "你背后什么", "你的模型是", "你什么模型",
                "你是哪家", "你是哪个公司", "你的api", "你的厂商",
                "你是openai", "你是anthropic", "你是google", "你是meta",
                "你是xai", "你是微软", "你是百度", "你是阿里", "你是腾讯",
                "你是字节", "你是bytedance", "你是科大讯飞", "你是华为",
                "你是智谱ai", "你是月之暗面",
                "谁做的你", "谁创造的你", "谁训练的你", "谁开发的你", "谁造的你",
                "造你的", "创造你的", "训练你的", "开发你的", "制造你的",
                "你是ai吗", "你是ai嗎", "你是不是ai", "你是人工智能",
                "你是不是人", "你是机器人", "你是機器人", "你是程序",
                "aregpt", "areclaude", "aregemini", "arellama", "aregrok",
                "basedon", "poweredby", "developedby", "builtby", "trainedby",
                "underlyingmodel", "whichmodel", "whatllm", "whatllmmodel",
                "yourmodel", "yourcompany", "yourmaker", "yourcreator",
                "areyouai", "areyourobot", "areyoubot", "areyouhuman"
            };

            foreach (var p in containsPatterns)
            {
                if (normalized.Contains(p, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        #endregion

        #region 公共方法

        public static string GetModeTemplate(ChatMode mode)
        {
            return mode switch
            {
                ChatMode.Edit => EditModeTemplate + SharedDataLookupGuide,
                ChatMode.Agent => AgentModeTemplate + SharedDataLookupGuide,
                ChatMode.Plan => PlanModeTemplate + SharedDataLookupGuide,
                ChatMode.Channel => BaseReadonlyTemplate,
                ChatMode.Business => BaseReadonlyTemplate,
                _ => BaseReadonlyTemplate
            };
        }

        public static string BuildUserPrompt(ChatMode mode, string userInput)
        {
            if (IsIdentityQuestion(userInput))
            {
                return IdentityQuestionPrompt;
            }

            return $"<user_request>\n{userInput}\n</user_request>";
        }

        #endregion
    }
}
