using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using NaughtyAttributes;
using System.Text.RegularExpressions;

/// <summary>
/// Base script for every mission in the game.
/// </summary>
[CreateAssetMenu(fileName = "Mission", menuName = "Mission", order = 1)]
public class Mission : ScriptableObject
{
    public string ID = System.Guid.NewGuid().ToString();
    [SerializeField] private string description;
    public Sprite icon;
    public bool Repeatable = true;
    public string RequiredMissionID;
    public float Goal = 1f;
    public bool GoalIsIncrement;
    [ShowIf(nameof(GoalIsIncrement))] public float GoalIncrement = 1f;
    [ShowIf(nameof(GoalIsIncrement))] public float MaxGoal;
    [ShowIf(nameof(GoalIsIncrement))] public bool MissionAvailableAfterIncrement = true;
    public int Reward = 100;

    public bool RewardCollected { get => ES3.Load($"{ID}_RewardCollected", false); set => ES3.Save($"{ID}_RewardCollected", value); }
    public DateTime RewardCollectedTime { get => DateTime.FromBinary(ES3.Load<long>($"{ID}_CollectedTime", 0)); set => ES3.Save($"{ID}_CollectedTime", value.ToBinary()); }
    public float Progress { get => LoadMissionProgress(); set => SaveMissionProgress(value); }
    public bool MissionAvailable { get => IsMissionAvailable(); }
    public float CurrentGoal { get => ES3.Load($"{ID}_CurrentGoal", Goal); set => ES3.Save($"{ID}_CurrentGoal", Mathf.Clamp(value, 0, MaxGoal > 0 ? MaxGoal : float.MaxValue)); }
    public string Description { get => GetDescription(); }
    public bool CanBeCollected { get => Progress == CurrentGoal && !RewardCollected; }

    public event Action<float> OnProgressChanged;


    /// <summary>
    /// Saves the progress of the mission and calls the OnProgressChanged event
    /// </summary>
    /// <param name="progress">Value between 0 and the goal of the mission</param>
    private void SaveMissionProgress(float progress)
    {
        if (string.IsNullOrWhiteSpace(ID)) return;

        ES3.Save(ID, Mathf.Clamp(progress, 0f, CurrentGoal));

        OnProgressChanged?.Invoke(Progress);
    }

    private float LoadMissionProgress()
    {
        if (string.IsNullOrWhiteSpace(ID)) return 0f;

        return ES3.Load(ID, 0f);
    }

    public float GetSliderProgress()
    {
        if (CurrentGoal <= 0) return 1f;

        return ES3.Load(ID, 0f);
    }

    /// <summary>
    /// Gets the reward for the mission and marks it as collected.
    /// If the mission is repeatable and the goal is an increment, the mission is reset.
    /// </summary>
    /// <returns>The reward for the mission.</returns>
    public int GetReward()
    {
        if (!CanBeCollected) return 0;

        if (GoalIsIncrement && MissionAvailableAfterIncrement)
        {
            ResetMission();
        }
        else
        {
            RewardCollectedTime = DateTime.Now;
            RewardCollected = true;
        }
        return Reward;
    }

    /// <summary>
    /// Replaces any {Property} in the description with the value of the corresponding property of this mission.
    /// </summary>
    /// <returns>The description with the replaced values.</returns>
    private string GetDescription()
    {
        string input = description;
        var regex = new Regex("{(.*?)}");
        var matches = regex.Matches(input);
        foreach (Match match in matches)
        {
            var valueWithoutBrackets = match.Groups[1].Value;
            var valueWithBrackets = match.Value;
            string replacedValue = GetType().GetProperty(valueWithoutBrackets)?.GetValue(this)?.ToString();

            if (!string.IsNullOrWhiteSpace(replacedValue))
            {
                input = input.Replace(valueWithBrackets, replacedValue);
            }
        }

        return input;
    }

    /// <summary>
    /// Resets the mission to its initial state. If the goal is incrementable, increase the goal by the goal increment, otherwise set it to the initial goal.
    /// </summary>
    public void ResetMission()
    {
        RewardCollectedTime = DateTime.Now;
        RewardCollected = false;
        if (GoalIsIncrement)
        {
            CurrentGoal += GoalIncrement;
        }
        else
        {
            CurrentGoal = Goal;
        }
        Progress = 0f;
    }

    private bool IsMissionAvailable()
    {
        if (string.IsNullOrWhiteSpace(RequiredMissionID)) return true;

        return ES3.Load($"{RequiredMissionID}_RewardCollected", false);
    }

    /// <summary>
    /// Checks if the mission should be reset due to the time difference
    /// between the current time and the last time the mission was collected.
    /// If the mission is repeatable and the time difference is greater than or equal to
    /// the given hoursToReset, the mission is reset.
    /// </summary>
    /// <param name="hoursToReset">The number of hours after which the mission should be reset.</param>
    public void CheckMission(int hoursToReset = 24)
    {
        TimeSpan difference = DateTime.Now.Subtract(RewardCollectedTime);

        if (RewardCollected && Repeatable && difference.TotalHours >= hoursToReset)
        {
            ResetMission();
        }
    }
}
