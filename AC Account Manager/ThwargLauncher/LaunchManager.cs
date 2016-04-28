﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;

namespace ThwargLauncher
{
    /// <summary>
    /// Called on worker thread
    /// </summary>
    class LaunchManager
    {
        public class LaunchManagerResult
        {
            public bool Success;
            public int ProcessId;
        }
        public delegate void ReportStatusHandler(string status, LaunchItem launchItem);
        public event ReportStatusHandler ReportStatusEvent;
        private void ReportStatus(string status, LaunchItem launchItem)
        {
            if (ReportStatusEvent != null)
            {
                ReportStatusEvent(status, launchItem);
            }
        }

        readonly Dictionary<string, DateTime> _accountLaunchTimes = new Dictionary<string, DateTime>();
        private string _launcherLocation;
        private LaunchItem _launchItem;

        public LaunchManager(string launcherLocation, LaunchItem launchItem)
        {
            _launcherLocation = launcherLocation;
            _launchItem = launchItem;

        }
        public LaunchManagerResult LaunchGameHandlingDelaysAndTitles(BackgroundWorker worker)
        {
            var result = new LaunchManagerResult();
            if (worker.CancellationPending)
            {
                return result;
            }
            DateTime lastLaunch = (_accountLaunchTimes.ContainsKey(_launchItem.AccountName)
                                       ? _accountLaunchTimes[_launchItem.AccountName]
                                       : DateTime.MinValue);
            TimeSpan delay = new TimeSpan(0, 5, 0) - (DateTime.Now - lastLaunch);
            GameLaunchResult gameLaunchResult = null;
            while (delay.TotalMilliseconds > 0)
            {
                if (worker.CancellationPending)
                {
                    return result;
                }
                string context = string.Format("Waiting {0} sec", (int)delay.TotalSeconds + 1);
                ReportStatus(context, _launchItem);

                System.Threading.Thread.Sleep(1000);
                delay = new TimeSpan(0, 0, 10) - (DateTime.Now - lastLaunch);
            }

            ReportStatus("Launching", _launchItem);
            _accountLaunchTimes[_launchItem.AccountName] = DateTime.Now;

            var launcher = new GameLauncher();
            launcher.ReportGameStatusEvent += (o) => { ReportStatus(o, _launchItem); };
            launcher.StopLaunchEvent += (o, eventArgs) => { return worker.CancellationPending; };
            try
            {
                var finder = new ThwargUtils.WindowFinder();
                finder.RecordExistingWindows();
                string launcherPath = GetLaunchItemLauncherLocation(_launchItem);
                OverridePreferenceFile(_launchItem.CustomPreferencePath);
                gameLaunchResult = launcher.LaunchGameClient(
                    launcherPath,
                    _launchItem.ServerName,
                    accountName: _launchItem.AccountName,
                    password: _launchItem.Password,
                    desiredCharacter: _launchItem.CharacterSelected
                    );
                if (!gameLaunchResult.Success)
                {
                    return result;
                }
                string gameCaptionPattern = ConfigSettings.GetConfigString("GameCaptionPattern", null);
                if (gameCaptionPattern != null)
                {
                    var regex = new System.Text.RegularExpressions.Regex(gameCaptionPattern);
                    IntPtr hwnd = finder.FindNewWindow(regex);
                    if (hwnd != IntPtr.Zero)
                    {
                        string newGameTitle = GetNewGameTitle(_launchItem);
                        if (!string.IsNullOrEmpty(newGameTitle))
                        {
                            finder.SetWindowTitle(hwnd, newGameTitle);
                        }
                    }
                }
            }
            catch (Exception exc)
            {
                ReportStatus("Exception launching game launcher: " + exc.Message, _launchItem);
                return result;
            }
            if (gameLaunchResult != null && gameLaunchResult.Success)
            {
                result.Success = true;
                result.ProcessId = gameLaunchResult.ProcessId;
            }
            return result;
        }
        private void OverridePreferenceFile(string customPreferencePath)
        {
            // Non-customizing launches need to restore active copy from base
            if (string.IsNullOrEmpty(customPreferencePath))
            {
                if (File.Exists(Configuration.UserPreferencesBaseFile))
                {
                    File.Copy(Configuration.UserPreferencesBaseFile, Configuration.UserPreferencesFile, overwrite: true);
                }
                return;
            }
            // customizing launches:
            if (!File.Exists(customPreferencePath)) { return; }
            // Backup actual file first

            if (!File.Exists(Configuration.UserPreferencesBaseFile))
            {
                File.Copy(Configuration.UserPreferencesFile, Configuration.UserPreferencesBaseFile, overwrite: false);
                if (!File.Exists(Configuration.UserPreferencesBaseFile)) { return; }
            }
            // Now overwrite
            File.Copy(customPreferencePath, Configuration.UserPreferencesFile, overwrite: true);
        }

        private string GetLaunchItemLauncherLocation(LaunchItem item)
        {
            if (!string.IsNullOrEmpty(item.CustomLaunchPath))
            {
                return item.CustomLaunchPath;
            }
            else
            {
                return _launcherLocation;
            }
        }
        private string GetNewGameTitle(LaunchItem launchItem)
        {
            if (launchItem.CharacterSelected == "None")
            {
                string pattern = ConfigSettings.GetConfigString("NewGameTitleNoChar", "");
                pattern = pattern.Replace("%ACCOUNT%", launchItem.AccountName);
                pattern = pattern.Replace("%SERVER%", launchItem.ServerName);
                return pattern;

            }
            else
            {
                string pattern = ConfigSettings.GetConfigString("NewGameTitle", "");
                pattern = pattern.Replace("%ACCOUNT%", launchItem.AccountName);
                pattern = pattern.Replace("%SERVER%", launchItem.ServerName);
                pattern = pattern.Replace("%CHARACTER%", launchItem.CharacterSelected);
                return pattern;
            }
        }
    }
}
