using System.Collections.Generic;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Models;

namespace TM.Services.Framework.AI.SemanticKernel.Conversation.Parsing
{
    public interface IPlanParser
    {
        IReadOnlyList<PlanStep> Parse(string content);

        int CountSteps(string content);
    }
}
