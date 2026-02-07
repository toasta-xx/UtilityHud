using BepInEx;
using Photon.Pun;
using Photon.Realtime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using UnityEngine;

namespace UtilityHud
{
    internal class ARS : MonoBehaviour
    {
        //Creds to Industry for this
        void Start()
        {
            EasierLog("ARS fully initialized, thank you for helping the gorilla tag modding community!");
        }

        public static List<Player> PlayersChecked = new List<Player>();
        public static string PlayerIDs = string.Empty;
        public static string[] PlayersToReport = new string[0];
        static bool HasChecked = false;

        void Update()
        {
            if (HasChecked && !PhotonNetwork.InRoom)
            {
                HasChecked = false;
                PlayersChecked.Clear();
            }

            if (PlayerIDs == string.Empty)
                GetPlayerIDs();

            if (PlayersToReport.Length < 3)
                GetPlayerIDs();

            if (PlayersToReport == null) return;

            foreach (Player plr in PhotonNetwork.PlayerListOthers)
            {
                if (PlayersChecked.Contains(plr)) continue;

                if (NeedToReport(plr))
                {
                    GorillaPlayerScoreboardLine.ReportPlayer(plr.UserId, GorillaPlayerLineButton.ButtonType.Toxicity, plr.NickName);
                    EasierLog($"Reported user {plr.NickName}.");
                }

                PlayersChecked.Add(plr);
            }

            if (!HasChecked && PhotonNetwork.InRoom)
                HasChecked = true;
        }

        static void GetPlayerIDs()
        {
            if (PlayerIDs == string.Empty)
                PlayerIDs = new WebClient().DownloadString("https://raw.githubusercontent.com/AutoReportSystem/ARSPlayerIDs/refs/heads/main/Player%20Ids.txt").Trim();

            PlayersToReport = PlayerIDs.Split(',').ToArray();

            EasierLog($"Recieved player ids to report. Count of users: {PlayersToReport.Count()}");
        }

        static bool NeedToReport(Player plr)
        {
            for (int i = 0; i < PlayersToReport.Length; i++)
                if (PlayersToReport[i] == plr.UserId)
                    return true;

            return false;
        }
        static void EasierLog(string message)
        {
            Console.WriteLine($"[ARS LOGGING] {message}");
        }
    }
}