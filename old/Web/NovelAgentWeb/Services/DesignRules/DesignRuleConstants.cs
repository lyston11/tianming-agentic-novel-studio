namespace TM.Web.NovelAgentWeb.Services.DesignRules;

/// <summary>
/// Constants for ProjectDesignRule.RuleType values.
/// Maps to the five design rules from the original Tianming kernel.
/// </summary>
public static class DesignRuleTypes
{
    /// <summary>World/setting hard rules - genre laws, physics, magic system limits.</summary>
    public const string WorldCoreRule = "WorldCoreRule";

    /// <summary>Character psychology and behavior rules.</summary>
    public const string CharacterPsycheRule = "CharacterPsycheRule";

    /// <summary>Main conflict engine and escalation pattern.</summary>
    public const string ConflictEngine = "ConflictEngine";

    /// <summary>Writing technique and craft instructions.</summary>
    public const string WritingTech = "WritingTech";

    /// <summary>Promise to readers (what the book delivers).</summary>
    public const string ReaderPromise = "ReaderPromise";

    /// <summary>Style guide (tone, voice, prose patterns).</summary>
    public const string StyleGuide = "StyleGuide";
}

/// <summary>
/// Constants for ProjectDesignRule.ConstraintLevel values.
/// </summary>
public static class DesignRuleConstraintLevels
{
    /// <summary>Soft reference, can be ignored if needed.</summary>
    public const string Reference = "Reference";

    /// <summary>Must be mentioned/considered when relevant.</summary>
    public const string MustMention = "MustMention";

    /// <summary>Hard constraint - violations block chapter commit.</summary>
    public const string MustSatisfy = "MustSatisfy";

    /// <summary>Forbidden - if violated, chapter must be rewritten.</summary>
    public const string Forbidden = "Forbidden";
}

/// <summary>
/// Constants for ProjectDesignRule.Status values.
/// </summary>
public static class DesignRuleStatuses
{
    /// <summary>Active rule, currently in effect.</summary>
    public const string Active = "Active";

    /// <summary>Archived rule, no longer active.</summary>
    public const string Archived = "Archived";

    /// <summary>Superseded by a newer version.</summary>
    public const string Superseded = "Superseded";
}
