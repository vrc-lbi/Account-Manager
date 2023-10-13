﻿using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using System;
using VRC.SDK3.StringLoading;
using VRC.SDK3.Data;

namespace LoliPoliceDepartment.Utilities.AccountManager
{
    /// <summary>
    /// The source of data for the account manager.
    /// </summary>
    public enum DataSource
    {
        Editor = 0,
        Internet = 1
    }
    
    /// <summary>
    /// The expcted data format for parsing.
    /// </summary>
    public enum DataFormat
    {
        CSV = 0,
        JSON = 1
    }

    /// <summary>
    /// Represents the comparison operators that can be used for filtering generated role lists.
    /// </summary>
    public enum Comparator
    {
        EqualTo = 0,
        NotEqualTo = 1,
        GreaterThan = 2,
        LessThan = 3,
        GreaterThanOrEqualTo = 4,
        LessThanOrEqualTo = 5
    }

    /// <summary>
    /// Manages officer account data, including loading and parsing data from a remote source.
    /// </summary>
    public class OfficerAccountManager : UdonSharpBehaviour
    {
        //-------------------------Notes-------------------------//
        #region Notes
        //PLEASE NOTE ALL OFFICERS HAVE ALL ROLES IF THIS IS A CSV FILE
        //If you want a list of staff but are using CSV, please run _CreateFilteredRoleList("Staff", Comparator.EqualTo, "true")

        //Default return values for _Get functions if the officer or role is not found:
        //string: ""
        //bool: false
        //int: 0
        //float: 0
        #endregion


        //-------------------------Variables-------------------------//
        #region Variables
        
        /// <summary>
        /// Log the performance of raw data parsing, _CreateRoleList, and _CreateFilteredRoleList 
        /// </summary>
        [SerializeField] private bool performanceLogging = true;
        /// <inheritdoc cref="OfficerAccountManager.performanceLogging"/>
        private System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();

        /// <summary>
        /// The raw data representing all of the officers.
        /// Editor/Offline data is stored here and is overwritten by Internet data if any is downloaded at runtime.
        /// </summary>
        [SerializeField, HideInInspector] public string rawOfficerData = "";
        
        /// <summary>
        /// The URL for online officer data.
        /// If users have Untrusted URLs disabled then only the following sources are valid:
        /// <list type="bullet">
        ///     <item>GitHub (*.github.io)</item>
        ///     <item>Pastebin (pastebin.com)</item>
        ///     <item>Github Gist (gist.githubusercontent.com)</item>
        /// </list>
        /// See <see href="https://creators.vrchat.com/worlds/udon/string-loading/">the official VRChat documentation</see> for more information.
        /// </summary>
        [SerializeField] private VRCUrl RemoteDataURL = VRCUrl.Empty;

        /// <summary>
        /// The preferred location for fetching account data.
        /// </summary>
        [SerializeField] public DataSource desiredDataSource { get; private set; } = DataSource.Editor;

        /// <summary>
        /// The locaton of the data that is currently loaded.
        /// </summary>
        [NonSerialized, HideInInspector] public DataSource currentDataSource = DataSource.Editor;

        /// <summary>
        /// The expected format of the data for parsing.
        /// </summary>
        [SerializeField] public DataFormat dataFormat = DataFormat.CSV;
        
        /// <summary>
        /// A value indicating whether the <see cref="OfficerAccountManager"/> is ready to be used.
        /// </summary>
        public bool isReady { get; private set; } = false;

        /// <summary>
        /// A value for tracking whether the <see cref="OfficerAccountManager"/> is currently busy initializing.
        /// A false value does not necessarily mean that the <see cref="OfficerAccountManager"/> is ready to be used.
        /// </summary>
        public bool isInitializing { get; private set; } = false;

        /// <summary>
        /// Indicates whether the <see cref="OfficerAccountManager"/> was initialized successfully.
        /// </summary>
        public bool initializedSuccessfully { get; private set; } = false;

        /// <summary>
        /// A dictionary of UdonBehaviors who wish to be notified when the <see cref="OfficerAccountManager"/> has finished initializing.
        /// Keys are typ <see cref="UdonBehavior"/>, values are the string function names to call on each <see cref="UdonBehavior"/> when the data is ready.
        /// </summary>
        [NonSerialized] private DataDictionary OnInitializedListeners = new DataDictionary(); //Dictionary of UdonBehaviors and their String function names to be called when the data is ready

        /// <summary>
        /// A dictionary mapping officer names to their role dictionaries.
        /// </summary>
        [NonSerialized] public DataDictionary nameToRankDictionary = new DataDictionary();

        /// <summary>
        /// An unsorted list of all role names.
        /// </summary>
        [NonSerialized] public DataList roleList = new DataList();
        #endregion       



        //-------------------------Initialization Functions-------------------------//
        #region Initialization
        /// <summary>
        /// Allow <see cref="UdonSharpBehaviour"/> instances to be notified when the <see cref="OfficerAccountManager"/> has finished initializing.
        /// </summary>
        /// <param name="behaviour">The <see cref="UdonSharpBehaviour"/> to notify.</param>
        /// <param name="functionName">The name of the function to call when the <see cref="OfficerAccountManager"/> has finished initializing.</param>
        public void NotifyWhenInitialized(UdonSharpBehaviour behaviour, string functionName)
        {
            //If we are already initialized, send the event immediately
            if (isReady)
            {
                //Send it immediately
                behaviour.SendCustomEvent(functionName);
                return;
            }

            //Subscribe
            if (!OnInitializedListeners.ContainsKey(behaviour))
            {
                OnInitializedListeners.Add(behaviour, functionName);
            }
            //No duplicates pls thx
            else
            {
                _LogWarning(behaviour.name + " attempted to subscribe to the initialized event, but it was already subscribed", behaviour);
                return;
            }
        }

        /// <summary>
        /// Removes a <see cref="UdonSharpBehaviour"/> from the list of listeners that will be notified when the OfficerAccountManager is initialized.
        /// </summary>
        /// <param name="behaviour">The <see cref="UdonSharpBehaviour"/> to remove from the listener list.</param>
        public void RemoveListener(UdonSharpBehaviour behaviour)
        {
            if (OnInitializedListeners.ContainsKey(behaviour))
            {
                OnInitializedListeners.Remove(behaviour);
            }
            else
            {
                _LogWarning("Attempted to remove " + behaviour.name + " from the listener list, but it was not in the list", behaviour);
            }
        }

        /// <summary>
        /// Calls Initialize() on start. Initialize is basically <inheritdoc cref="OfficerAccountManager.Initialize"/>
        /// </summary>
        private void Start() => Initialize();
        /// <summary>
        /// Start but public. Safe to call multiple times if initialization fails.
        /// </summary>
        public void Initialize() {
            if (isInitializing)
            {
                _LogWarning("Initialize was called while the account manager is already initializing", this);
                return;
            }
            isInitializing = true;
            isReady = false;
            initializedSuccessfully = false;
            //Setup
            switch(desiredDataSource)
            {
                case DataSource.Editor:
                    //We are using offline data, it is already ready
                    _Log("Using offline data", this);
                    currentDataSource = DataSource.Editor;
                    initializedSuccessfully = true;
                    DataReady();
                    break;

                case DataSource.Internet:
                    //Request data from the web server
                    _Log("Fetching officer data from " + RemoteDataURL.Get(), this);
                    // currentDataSource = DataSource.Local; //Set later depending on whether the request succeeds
                    VRCStringDownloader.LoadUrl(RemoteDataURL, (VRC.Udon.Common.Interfaces.IUdonEventReceiver) this);
                    break;
            }
        }

        /// <summary>
        /// Called by VRChat after attempting to download officer data from the internet. Please do not call this function manually.
        /// See <see href="https://creators.vrchat.com/worlds/udon/string-loading/">the official VRChat documentation</see> for more information.
        /// </summary>
        /// <param name="result">The raw account data downloaded from the web server.</param>
        public override void OnStringLoadSuccess(IVRCStringDownload result)
        {
            //Overwrite the offline data and continue initializing
            rawOfficerData = result.Result;
            _Log("Officer data downloaded successfully", this);
            currentDataSource = DataSource.Internet;
            initializedSuccessfully = true;
            DataReady();
        }

        /// <inheritdoc cref="OfficerAccountManager.OnStringLoadSuccess"/>
        public override void OnStringLoadError(IVRCStringDownload result)
        {
            //Use offline data
            _LogWarning("Failed to download officer data, using offline data", this);
            currentDataSource = DataSource.Editor;
            initializedSuccessfully = false;
            DataReady();
        }

        /// <summary>
        /// Callback method that is called when the data has been successfully retrieved and is ready to be parsed.
        /// </summary>
        private void DataReady()
        {
            //Parse whatever data is available
            if (performanceLogging) stopwatch.Start();
            if (dataFormat == DataFormat.CSV)
            {
                //Parse the CSV file
                ParseCSV(rawOfficerData);
            }
            else if (dataFormat == DataFormat.JSON)
            {
                //Parse the JSON file
                ParseJSON(rawOfficerData);
            }

            if (performanceLogging) {
                stopwatch.Stop();
                _Log("Parsing took " + stopwatch.ElapsedMilliseconds + "ms", this);
            }

            //Success
            isInitializing = false;
            isReady = true;

            //Notify subscribers
            DataList keys = OnInitializedListeners.GetKeys();
            for (int i = 0; i < keys.Count; i++)
            {
                UdonSharpBehaviour receiver = (UdonSharpBehaviour)keys[i].Reference;
                string eventName = OnInitializedListeners[keys[i]].ToString();
                receiver.SendCustomEvent(eventName);
            }

            //Performance test
            _Log("Testing List generation performance");
            _CreateRoleDict("Rank");

            _Log("Testing Filtered List generation performance (Staff)");
            DataDictionary staffList = _CreateFilteredRoleDict("Staff", Comparator.EqualTo, "True");
            _Log("Staff List has " + staffList.Count + " entries");

            _Log("Testing Filtered List generation performance (Recruit)");
            DataDictionary recruitList = _CreateFilteredRoleDict("Rank", Comparator.EqualTo, "LPD Recruit");
            float percent = (float) recruitList.Count / (float) nameToRankDictionary.Count;
            _Log(recruitList.Count + " of " + nameToRankDictionary.Count + " officers are recruits!. That's " + percent.ToString("P1") + "!");


            _Log("Testing Filtered List generation performance (Dev)");
            DataDictionary devDict = _CreateFilteredRoleDict("Dev", Comparator.EqualTo, "True");
            DataList devNames = devDict.GetKeys();
            devNames.Sort();
            string devNameList = "";
            for (int i = 0; i < devNames.Count; i++) { devNameList += '\n' + devNames[i].String; }
            _Log("The LPD devs are:" + devNameList);
        }

        /// <summary>
        /// Parse the CSV file
        /// </summary>
        /// <param name="csv">The CSV file to parse.</param>
        public void ParseCSV(string csv)
        {
            string[] lines = csv.Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

            string[] roleNames = lines[0].Split(',');

            nameToRankDictionary = new DataDictionary();
            for (int i = 1; i < lines.Length; i++)
            {
                string[] values = lines[i].Split(',');
                string name = values[0];
                DataDictionary officerRoles = new DataDictionary();
                for (int j = 1; j < values.Length; j++)
                {
                    string value = values[j];
                    //Skip entries which parse to "false"
                    if (bool.TryParse(value, out bool result) && result == false) {
                        //Do not add false entries
                    } else {
                        officerRoles.Add(roleNames[j], values[j]);
                    }
                }
                nameToRankDictionary.Add(name, officerRoles);
            }
        }

        /// <summary>
        /// Parse the JSON file
        /// </summary>
        /// <param name="json">The JSON file to parse.</param>
        public void ParseJSON(string json)
        {
            // Karet's problem
        }
        #endregion


        //-------------------------Getters-------------------------//
        #region Getters
        /// <summary>
        /// Determines if the given player is an officer based on their display name.
        /// </summary>
        /// <param name="player">The player to check.</param>
        /// <param name="name">The display name of the officer.</param>
        /// <returns>True if the player is an officer, false otherwise.</returns>
        public bool _IsOfficer(string name)
        {
            return nameToRankDictionary.TryGetValue(name, out DataToken dont_care);
        }

        /// <inheritdoc cref="OfficerAccountManager._IsOfficer"/>
        public bool _IsOfficer(VRCPlayerApi player)
        {
            return _IsOfficer(player.displayName);
        }

        /// <inheritdoc cref="OfficerAccountManager._IsOfficer"/>
        public bool _IsLocalPlayerOfficer()
        {
            return _IsOfficer(Networking.LocalPlayer.displayName);
        }



        //Main function for getting values. Only returns DataTokens which can be converted to strings, bools, ints, and floats.
        /// <summary>
        /// Gets the data token for the specified player and role with an optional default value.
        /// See <see href="https://creators.vrchat.com/worlds/udon/data-containers/data-tokens/">the official VRChat documentation</see>
        /// for more information on the <see cref="DataToken"/> class.
        /// </summary>
        /// <param name="player">The player to get the data token for.</param>
        /// <param name="displayName">The display name of the officer.</param>
        /// <param name="role">The role associated with the data token.</param>
        /// <param name="defaultValue">The default value to return if no data token is found.</param>
        /// <returns>The data token for the specified player and role, or the default value if no data token is found.</returns>
        /// <inheritdoc cref="OfficerAccountManager._GetLocalPlayerToken"/>
        public DataToken _GetToken(string displayName, string role, DataToken defaultValue = new DataToken())
        {
            bool found = nameToRankDictionary.TryGetValue(displayName, out DataToken officerRoles);
            if (!found)
            {
                _LogWarning("Officer \"" + displayName + "\" not found", this);
                return DataError.KeyDoesNotExist;
            }

            found = officerRoles.DataDictionary.TryGetValue(role, out DataToken value);
            if (!found)
            {
                _LogWarning("Role \"" + role + "\" not found for officer \"" + displayName + "\"", this);
                return DataError.KeyDoesNotExist;
            }

            return value;
        }
        /// <inheritdoc cref="OfficerAccountManager._GetToken"/>
        public DataToken _GetToken(VRCPlayerApi player, string role, DataToken defaultValue = new DataToken()) => _GetToken(player.displayName, role, defaultValue);
        /// <inheritdoc cref="OfficerAccountManager._GetToken"/>
        public DataToken _GetLocalPlayerToken(string role, DataToken defaultValue = new DataToken()) => _GetToken(Networking.LocalPlayer.displayName, role, defaultValue);
        

        
        /// <summary>
        /// Attempts to retrieve the data token associated with the specified officer and role.
        /// See <see href="https://creators.vrchat.com/worlds/udon/data-containers/data-tokens/">the official VRChat documentation</see>
        /// for more information on the <see cref="DataToken"/> class.
        /// </summary>
        /// <param name="player">The player to get the data token for.</param>
        /// <param name="displayName">The display name of the officer.</param>
        /// <param name="role">The role of the officer.</param>
        /// <param name="value">The data token for the officer's display name and role.</param>
        /// <returns>True if the data token was found, false otherwise.</returns>
        public bool _TryGetToken(string displayName, string role, out DataToken value)
        {
            bool found = nameToRankDictionary.TryGetValue(displayName, out DataToken officerRoles);
            if (!found)
            {
                value = DataError.KeyDoesNotExist;
                return false;
            }

            found = officerRoles.DataDictionary.TryGetValue(role, out value);
            if (!found)
            {
                value = DataError.KeyDoesNotExist;
                return false;
            }

            return true;
        }
        /// <inheritdoc cref="OfficerAccountManager._TryGetToken"/>
        public bool _TryGetLocalPlayerToken(string role, out DataToken value) => _TryGetToken(Networking.LocalPlayer.displayName, role, out value);
        /// <inheritdoc cref="OfficerAccountManager._TryGetToken"/>
        public bool _TryGetToken(VRCPlayerApi player, string role, out DataToken value) => _TryGetToken(player.displayName, role, out value);



        /// <summary>
        /// Creates a dictionary of officer names to values for a given role name. Please do not call this function every frame.
        /// </summary>
        /// <param name="roleName">The name of the role to filter by.</param>
        /// <returns>A dictionary of officer names and their corresponding role values for the given role name.</returns>
        public DataDictionary _CreateRoleDict(string roleName)
        {
            if (performanceLogging) stopwatch.Start();

            DataDictionary roleList = new DataDictionary();
            DataList keys = nameToRankDictionary.GetKeys();
            for (int i = 0; i < keys.Count; i++)
            {
                DataToken officerRoles = nameToRankDictionary[keys[i]];
                if (officerRoles.DataDictionary.TryGetValue(roleName, out DataToken value))
                {
                    roleList.Add(keys[i], value);
                }
            }

            if (performanceLogging) {
                stopwatch.Stop();
                _Log("Creating role list took " + stopwatch.ElapsedMilliseconds + "ms", this);
            }

            return roleList;
        }
        
        /// <summary>
        /// Creates a filtered list of officers whose value for a given role passes the specified test. Please do not call this function every frame.
        /// </summary>
        /// <param name="roleName">The name of the role to filter by.</param>
        /// <param name="comparator">The comparator to use for filtering.</param>
        /// <param name="token">The token to compare against.</param>
        /// <returns>A DataDictionary containing the filtered list of officers.</returns>
        public DataDictionary _CreateFilteredRoleDict(string roleName, Comparator comparator, DataToken token)
        {
            if (performanceLogging) stopwatch.Start();

            //Dictionary enumerator
            // var userEnumerator = nameToRankDictionary.GetEnumerator();
            //Pretend dictionary enumerator
            DataList names = nameToRankDictionary.GetKeys();
            DataList rankDicts = nameToRankDictionary.GetValues();
            //Rank index
            int rankIndex = roleList.IndexOf(roleName);

            //Create the dictionary
            DataDictionary filteredDictionary = new DataDictionary();

            //I am so sorry for this abomination
            switch (comparator)
            {
                case Comparator.EqualTo:
                    for (int i = 0; i < names.Count; i++)
                    {
                        string name = names[i].String;
                        DataToken value = rankDicts[i].DataDictionary[roleName];
                        if (!value.IsNull && value.Equals(token)) filteredDictionary.Add(name, value);
                    }
                    break;
                case Comparator.NotEqualTo:
                    for (int i = 0; i < names.Count; i++)
                    {
                        string name = names[i].String;
                        DataToken value = rankDicts[i].DataDictionary[roleName];
                        if (!value.IsNull && !value.Equals(token)) filteredDictionary.Add(name, value);
                    }
                    break;
                case Comparator.GreaterThan:
                    for (int i = 0; i < names.Count; i++)
                    {
                        string name = names[i].String;
                        DataToken value = rankDicts[i].DataDictionary[roleName];
                        if (!value.IsNull && value.CompareTo(token) > 0) filteredDictionary.Add(name, value);
                    }
                    break;
                case Comparator.LessThan:
                    for (int i = 0; i < names.Count; i++)
                    {
                        string name = names[i].String;
                        DataToken value = rankDicts[i].DataDictionary[roleName];
                        if (!value.IsNull && value.CompareTo(token) < 0) filteredDictionary.Add(name, value);
                    }
                    break;
                case Comparator.GreaterThanOrEqualTo:
                    for (int i = 0; i < names.Count; i++)
                    {
                        string name = names[i].String;
                        DataToken value = rankDicts[i].DataDictionary[roleName];
                        if (!value.IsNull && value.CompareTo(token) >= 0) filteredDictionary.Add(name, value);
                    }
                    break;
                case Comparator.LessThanOrEqualTo:
                    for (int i = 0; i < names.Count; i++)
                    {
                        string name = names[i].String;
                        DataToken value = rankDicts[i].DataDictionary[roleName];
                        if (!value.IsNull && value.CompareTo(token) <= 0) filteredDictionary.Add(name, value);
                    }
                    break;
            }
            
            if (performanceLogging) {
                stopwatch.Stop();
                _Log("Creating filtered role list took " + stopwatch.ElapsedMilliseconds + "ms", this);
            }

            return filteredDictionary;
        }
        #endregion



        //-------------------------Backwards Compatibility and Convenience Functions-------------------------//
        #region BackwardsCompatibility&Convenience
        //Different flavors of the _Get function
        /* Default values if the officerID or roleIndex is out of range
         *   string: ""
         *   bool: false
         *   int: 0
         *   float: 0
         */

        //String
        /// <summary>See <see cref="OfficerAccountManager._GetToken"/>. Default value is "" if the officer or role doesn't exist.</summary>
        public string _GetString(string officerName, string roleName)
        {
            //No parsing needed
            return _GetToken(officerName, roleName, "").String;
        }
        /// <summary>See <see cref="OfficerAccountManager._GetToken"/>. Default value is "" if the officer or role doesn't exist.</summary>
        public string _GetString(string roleName) => _GetString(Networking.LocalPlayer.displayName, roleName);

        //Bool
        /// <summary>See <see cref="OfficerAccountManager._GetToken"/>. Default value is false if the officer or role doesn't exist.</summary>
        public bool _GetBool(string officerName, string roleName)
        {
            string value = _GetToken(officerName, roleName, bool.FalseString).String;
            bool success = bool.TryParse(value, out bool result);
            if (!success)
            {
                _LogWarning("Failed to parse bool for officer \"" + officerName + "\" and role \"" + roleName + "\"", this);
                return false;
            }
            return result;
        }
        /// <summary>See <see cref="OfficerAccountManager._GetToken"/>. Default value is false if the officer or role doesn't exist.</summary>
        public bool _GetBool(string roleName) => _GetBool(Networking.LocalPlayer.displayName, roleName);
        //Int
        /// <summary>See <see cref="OfficerAccountManager._GetToken"/>. Default value is 0 if the officer or role doesn't exist.</summary>
        public int _GetInt(string officerName, string roleName)
        {
            string value = _GetToken(officerName, roleName, "0").String;
            bool success = int.TryParse(value, out int result);
            if (!success)
            {
                _LogWarning("Failed to parse int for officer \"" + officerName + "\" and role \"" + roleName + "\"", this);
                return 0;
            }
            return result;
        }
        /// <summary>See <see cref="OfficerAccountManager._GetToken"/>. Default value is 0 if the officer or role doesn't exist.</summary>
        public int _GetInt(string roleName) => _GetInt(Networking.LocalPlayer.displayName, roleName);
        //Float
        /// <summary>See <see cref="OfficerAccountManager._GetToken"/>. Default value is 0 if the officer or role doesn't exist.</summary>
        public float _GetFloat(string officerName, string roleName) 
        {
            string value = _GetToken(officerName, roleName, "0").String;
            bool success = float.TryParse(value, out float result);
            if (!success)
            {
                _LogWarning("Failed to parse float for officer \"" + officerName + "\" and role \"" + roleName + "\"", this);
                return 0f;
            }
            return result;
        }
        /// <summary>See <see cref="OfficerAccountManager._GetToken"/>. Default value is 0 if the officer or role doesn't exist.</summary>
        public float _GetFloat(string roleName) => _GetFloat(Networking.LocalPlayer.displayName, roleName);
        #endregion



        //-------------------------Debug-------------------------//
        #region Debug
        //Logging because I am dumb and stupid and suffer to make things pretty
        internal void _Log(string value, UnityEngine.Object context = null) => _LogInternal(LogType.Log, value, context);
        internal void _LogWarning(string value, UnityEngine.Object context = null) => _LogInternal(LogType.Warning, value, context);
        internal void _LogError(string value, UnityEngine.Object context = null) => _LogInternal(LogType.Error, value, context);
        internal void _LogInternal(LogType type = LogType.Log, string value = "", UnityEngine.Object context = null)
        {
            Color32 color;
            switch (type) {
                case LogType.Warning:
                    color = Color.yellow;
                    break;
                case LogType.Error:
                    color = Color.red;
                    break;
                default:
                    color = Color.white;
                    break;
            }
            string hexColor = color.r.ToString("X2") + color.g.ToString("X2") + color.b.ToString("X2");

            if (context == null)
                UnityEngine.Debug.Log("<color=navy><b>Account Manager:</b></color> <color=#" + hexColor + ">" + value + "</color>");
            else
                UnityEngine.Debug.Log("<color=navy><b>Account Manager:</b></color> <color=#" + hexColor + ">" + value + "</color>", context);
        }
        #endregion
    }
}