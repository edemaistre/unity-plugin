using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Scenario.Editor
{
    /// <summary>Shared project settings, committed with the project (ProjectSettings/ScenarioSettings.asset).</summary>
    [FilePath("ProjectSettings/ScenarioSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class ScenarioProjectSettings : ScriptableSingleton<ScenarioProjectSettings>
    {
        [Tooltip("Where results are saved, inside Assets.")]
        public string OutputRoot = "Assets/Scenario";
        [Tooltip("Put each day's results in a dated folder (Assets/Scenario/Image/2026-10-18).")]
        public bool DatedFolders = true;
        [Tooltip("Ask before any request above this many Creative Units.")]
        public int ConfirmAboveCu = 100;
        [Tooltip("Sprites instead of plain textures for images (2D projects).")]
        public bool ImagesAsSprites;

        public void Save() => Save(true);
    }

    /// <summary>Per-user, per-project state (UserSettings/ScenarioUserSettings.asset, not committed).</summary>
    [FilePath("UserSettings/ScenarioUserSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class ScenarioUserSettings : ScriptableSingleton<ScenarioUserSettings>
    {
        public string TeamId = "";
        public string TeamName = "";
        public string ProjectId = "";
        public string ProjectName = "";
        public string Plan = "";

        public string OutputType = "image";
        public List<string> TypeModelKeys = new List<string>();
        public List<string> TypeModelValues = new List<string>();
        public List<string> Favorites = new List<string>();
        public List<string> RecentModels = new List<string>();
        public string LastPrompt = "";
        public string View = "create";
        public float TrayHeight = 220;

        /// <summary>Refuses every paid call (same effect as SCENARIO_NO_SPEND=1).</summary>
        public bool SpendingDisabled;

        public bool AgentEnabled;
        public int AgentPort = 8791;
        public string AgentToken = "";
        public int AgentAutoApproveCu = 20;
        public int AgentSessionBudgetCu = 300;
        public bool AgentCanEditScene = true;

        public string ModelFor(string outputType)
        {
            var index = TypeModelKeys.IndexOf(outputType);
            return index >= 0 && index < TypeModelValues.Count ? TypeModelValues[index] : "";
        }

        public void SetModelFor(string outputType, string modelId)
        {
            var index = TypeModelKeys.IndexOf(outputType);
            if (index >= 0) TypeModelValues[index] = modelId;
            else
            {
                TypeModelKeys.Add(outputType);
                TypeModelValues.Add(modelId);
            }
            RecentModels.Remove(modelId);
            RecentModels.Insert(0, modelId);
            if (RecentModels.Count > 12) RecentModels.RemoveRange(12, RecentModels.Count - 12);
            Save();
        }

        public bool ToggleFavorite(string modelId)
        {
            var added = !Favorites.Remove(modelId);
            if (added) Favorites.Insert(0, modelId);
            Save();
            return added;
        }

        public string EnsureAgentToken()
        {
            if (string.IsNullOrEmpty(AgentToken))
            {
                AgentToken = "sut_" + Core.OAuthClient.RandomToken(24);
                Save();
            }
            return AgentToken;
        }

        public void Save() => Save(true);
    }
}
