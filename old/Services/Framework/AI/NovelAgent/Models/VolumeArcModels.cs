using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TM.Services.Framework.AI.NovelAgent.Models
{
    public sealed class VolumeArcPlanningRequest
    {
        public string UserGoal { get; set; } = string.Empty;
        public string VolumeId { get; set; } = string.Empty;
        public string VolumeTitle { get; set; } = string.Empty;
        public string StartChapterId { get; set; } = string.Empty;
        public string EndChapterId { get; set; } = string.Empty;
        public int ExpectedChapterCount { get; set; } = 12;
        public List<string> CandidateDirections { get; set; } = new();
        public List<string> ForbiddenDirections { get; set; } = new();
    }

    public sealed class VolumeArcPlan
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        [JsonPropertyName("volumeId")]
        public string VolumeId { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("startChapterId")]
        public string StartChapterId { get; set; } = string.Empty;

        [JsonPropertyName("endChapterId")]
        public string EndChapterId { get; set; } = string.Empty;

        [JsonPropertyName("expectedChapterCount")]
        public int ExpectedChapterCount { get; set; }

        [JsonPropertyName("status")]
        public VolumeArcStatus Status { get; set; } = VolumeArcStatus.Draft;

        [JsonPropertyName("volumePromise")]
        public string VolumePromise { get; set; } = string.Empty;

        [JsonPropertyName("entryState")]
        public string EntryState { get; set; } = string.Empty;

        [JsonPropertyName("exitState")]
        public string ExitState { get; set; } = string.Empty;

        [JsonPropertyName("coreQuestion")]
        public string CoreQuestion { get; set; } = string.Empty;

        [JsonPropertyName("mainConflictUpgrade")]
        public string MainConflictUpgrade { get; set; } = string.Empty;

        [JsonPropertyName("midpointReversal")]
        public string MidpointReversal { get; set; } = string.Empty;

        [JsonPropertyName("climax")]
        public string Climax { get; set; } = string.Empty;

        [JsonPropertyName("aftermathHook")]
        public string AftermathHook { get; set; } = string.Empty;

        [JsonPropertyName("chapterBeats")]
        public List<VolumeChapterBeat> ChapterBeats { get; set; } = new();

        [JsonPropertyName("foreshadowingPlan")]
        public List<VolumeForeshadowPlan> ForeshadowingPlan { get; set; } = new();

        [JsonPropertyName("characterArcPlan")]
        public List<VolumeCharacterArc> CharacterArcPlan { get; set; } = new();

        [JsonPropertyName("worldbuildingIncrements")]
        public List<string> WorldbuildingIncrements { get; set; } = new();

        [JsonPropertyName("mustAvoid")]
        public List<string> MustAvoid { get; set; } = new();

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [JsonPropertyName("updatedAt")]
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }

    public sealed class VolumeChapterBeat
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("goal")]
        public string Goal { get; set; } = string.Empty;

        [JsonPropertyName("turn")]
        public string Turn { get; set; } = string.Empty;

        [JsonPropertyName("cost")]
        public string Cost { get; set; } = string.Empty;
    }

    public sealed class VolumeForeshadowPlan
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("setup")]
        public string Setup { get; set; } = string.Empty;

        [JsonPropertyName("payoff")]
        public string Payoff { get; set; } = string.Empty;

        [JsonPropertyName("payoffBeatIndex")]
        public int PayoffBeatIndex { get; set; }
    }

    public sealed class VolumeCharacterArc
    {
        [JsonPropertyName("characterName")]
        public string CharacterName { get; set; } = string.Empty;

        [JsonPropertyName("startingBelief")]
        public string StartingBelief { get; set; } = string.Empty;

        [JsonPropertyName("pressure")]
        public string Pressure { get; set; } = string.Empty;

        [JsonPropertyName("choice")]
        public string Choice { get; set; } = string.Empty;

        [JsonPropertyName("changedState")]
        public string ChangedState { get; set; } = string.Empty;
    }

    public enum VolumeArcStatus
    {
        Draft = 0,
        Proposed = 1,
        Canon = 2,
        Deprecated = 3
    }
}
