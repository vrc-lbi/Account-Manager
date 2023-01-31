﻿
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using LoliPoliceDepartment.Utilities.AccountManager;

namespace LoliPoliceDepartment.Examples
{
    public class StringLookup : UdonSharpBehaviour
    {
        public OfficerAccountManager accountManager; //Our reference to the account manager
        public UnityEngine.UI.Text text; //The text object to display the string
        [Space]
        public string roleName = "Species"; //Role to look up
        
        //Automatically find the account manager in the scene if it's not assigned
        #if !COMPILER_UDONSHARP && UNITY_EDITOR
            private void OnValidate() {
                if (accountManager == null) accountManager = (OfficerAccountManager) FindObjectOfType(typeof(OfficerAccountManager));
            }
        #endif
        
        //Show the current player's string value for the specified role
        private void Start() {
            string value = accountManager._GetString(roleName); //Note: The default value is "" if the officer or role doesn't exist
            text.text = value;
        }
    }
}