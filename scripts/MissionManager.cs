using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Handles all active missions
/// </summary>
public class MissionManager : MonoBehaviour
{
    public static MissionManager Instance { get; private set; }

    [SerializeField] private int hoursToReset = 24;

    public Mission[] missions;

    public event Action<Mission> OnMissionChanged;
    public event Action<Mission> OnMissionCollected;

    private void Awake()
    {
        if(Instance != null)
        {
            Destroy(this);
            return;
        }
        Instance = this;

        LoadMissions();
    }

    /// <summary>
    /// Loads all missions from the Resources folder and calls the CheckMission function on each of them to check if they need to be reset.
    /// </summary>
    private void LoadMissions()
    {
        missions = Resources.LoadAll<Mission>("Missions");

        for (int i = 0; i < missions.Length; i++)
        {
            missions[i].CheckMission(hoursToReset);
        }
    }

    public void MissionCollected(Mission mission)
    {
        OnMissionCollected?.Invoke(mission);
    }

    /// <summary>
    /// Increases the progress of the mission with the given ID by the given value.
    /// </summary>
    /// <param name="missionID">The ID of the mission to increase the progress of.</param>
    /// <param name="progressIncrement">The value to increase the progress by.</param>
    public void IncreaseMissionProgressIncrement(string missionID, float progressIncrement)
    {
        Mission mission = missions.First(x => x.ID.Equals(missionID));

        if (mission)
        {
            mission.Progress += progressIncrement;
            OnMissionChanged?.Invoke(mission);
        }
        else
        {
            Debug.LogError("No Mission found with ID: " + missionID);
        }
    }

    /// <summary>
    /// Sets the progress of the mission with the given ID to the current goal.
    /// </summary>
    /// <param name="missionID">The ID of the mission to set the progress of.</param>
    public void FinishMission(string missionID)
    {
        Mission mission = missions.First(x => x.ID.Equals(missionID));

        if (mission)
        {
            mission.Progress = mission.CurrentGoal;
            OnMissionChanged?.Invoke(mission);
        }
        else
        {
            Debug.LogError("No Mission found with ID: " + missionID);
        }
    }
}
